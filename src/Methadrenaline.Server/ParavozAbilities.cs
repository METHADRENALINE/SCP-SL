using System;
using System.Collections.Generic;
using System.Linq;
using CustomPlayerEffects;
using Footprinting;
using InventorySystem.Items;
using InventorySystem.Disarming;
using LabApi.Features.Wrappers;
using PlayerRoles;
using PlayerRoles.FirstPersonControl;
using PlayerStatsSystem;
using UnityEngine;

namespace Methadrenaline.Server;

// Halloween effects disappear from both clients' and servers' effect registries
// after the event. Use ordinary synchronized movement effects and server damage
// instead of adding seasonal effects back and corrupting their network indices.
internal sealed class ParavozAbilities
{
    private sealed class State(Player player) : IInteractionBlocker
    {
        internal readonly Player Player = player;
        internal readonly int Life = player.LifeId;
        internal readonly byte Movement = player.GetEffect<MovementBoost>()!.Intensity;
        internal readonly float Interval = Math.Max(0.05f, player.ReferenceHub.playerEffectsController.effectsGameObject
            .GetComponentsInChildren<Spicy>(true).FirstOrDefault()?.TimeBetweenTicks ?? 1f);
        internal float NextTick = Time.realtimeSinceStartup;
        internal int Ticks;
        internal bool Active = true;
        public BlockedInteraction BlockedInteractions => BlockedInteraction.OpenInventory;
        public bool CanBeCleared => !Active;
    }

    private readonly Dictionary<uint, State> states = new();

    internal bool Enabled(Player player) => states.TryGetValue(player.NetworkId, out var state) &&
        state.Active && state.Life == player.LifeId && player.IsAlive;

    internal void Apply(Player player)
    {
        Forget(player.NetworkId);
        var state = new State(player);
        state.NextTick += state.Interval;
        states.Add(player.NetworkId, state);
        player.ReferenceHub.interCoordinator.AddBlocker(state);
        Maintain(player);
        RefreshInventoryView(player);
    }

    internal static void RefreshInventoryView(Player player)
    {
        if (!player.IsDestroyed && player.ReferenceHub.connectionToClient is { isReady: true } connection)
            connection.Send(new DisarmedPlayersListMessage(DisarmedPlayers.Entries));
    }

    internal void Maintain(Player player)
    {
        if (!Enabled(player)) return;
        Item? radio = null;
        foreach (Item item in player.Items.ToArray())
        {
            if (item.Type == ItemType.Radio && radio == null) radio = item;
            else player.RemoveItem(item);
        }
        bool created = radio == null;
        radio ??= player.AddItem(ItemType.Radio);
        if (radio is RadioItem transmitter)
        {
            transmitter.BatteryPercent = 100;
            if (created) transmitter.Base.ServerProcessCmd(InventorySystem.Items.Radio.RadioMessages.RadioCommand.Enable);
        }
        if (player.ReferenceHub.inventory.UserInventory.ReserveAmmo.Values.Any(amount => amount > 0)) player.ClearAmmo();
        var movement = player.GetEffect<MovementBoost>()!;
        if (movement.Intensity < 50) movement.ServerSetState(50);
        var stamina = player.ReferenceHub.playerStats.GetModule<StaminaStat>();
        if (stamina.CurValue < stamina.MaxValue) stamina.CurValue = stamina.MaxValue;
    }

    internal void Tick(Player player)
    {
        if (!Enabled(player)) return;
        Maintain(player);
        var state = states[player.NetworkId];
        float now = Time.realtimeSinceStartup;
        if (now < state.NextTick) return;
        // Match Spicy's cadence and four harmless startup ticks. Never burst
        // delayed damage ticks after a stalled server frame.
        state.NextTick += state.Interval;
        if (state.NextTick <= now) state.NextTick = now + state.Interval;
        if (++state.Ticks > 4) FireTick(player);
    }

    internal void FireTick(Player carrier)
    {
        if (!Enabled(carrier) || carrier.RoleBase is not IFpcRole sourceRole) return;
        var hub = carrier.ReferenceHub;
        Vector3 origin = sourceRole.FpcModule.Position;
        bool hitmarker = false;
        foreach (Player target in Player.List.ToArray())
        {
            if (!carrier.IsAlive) break;
            if (target == carrier || !target.IsAlive || target.RoleBase is not IFpcRole targetRole) continue;
            Vector3 delta = targetRole.FpcModule.Position - origin;
            float squared = delta.sqrMagnitude;
            if (squared > 25f) continue;
            float distance = Mathf.Sqrt(squared);
            Vector3 direction = Mathf.Approximately(distance, 0) ? Vector3.zero : delta / distance;
            if (distance >= 0.7f && Vector3.Dot(direction, hub.PlayerCameraReference.forward) < 0.75f) continue;
            if (Physics.Raycast(origin, direction, distance, PlayerRolesUtils.AttackMask)) continue;
            // Retain native attacker attribution, shields, friendly damage
            // handling and the existing lethal-fire infection path.
            float damage = 15f;
            if (target.ReferenceHub.playerStats.TryGetModule<HumeShieldStat>(out var shield))
                damage = damage * 3 < shield.CurValue ? damage * 3 : damage + shield.CurValue / 3;
            var handler = new ExplosionDamageHandler(new Footprint(hub), Vector3.zero, damage, 100, ExplosionType.Custom);
            if (!target.ReferenceHub.playerStats.DealDamage(handler)) continue;
            hitmarker |= Hitmarker.CheckHitmarkerPerms(handler, target.ReferenceHub);
            if (target.IsAlive) target.ReferenceHub.playerEffectsController.EnableEffect<Burned>(60);
        }
        if (hitmarker) Hitmarker.SendHitmarkerDirectly(hub, 1);
    }

    internal void Forget(uint id)
    {
        if (!states.TryGetValue(id, out var state)) return;
        states.Remove(id);
        state.Active = false;
        var player = state.Player;
        if (!player.IsDestroyed) player.ReferenceHub.interCoordinator.RemoveBlocker(state);
        RefreshInventoryView(player);
        if (player.IsDestroyed || player.LifeId != state.Life) return;
        player.GetEffect<MovementBoost>()?.ServerSetState(state.Movement);
    }

    internal void Reset()
    {
        foreach (uint id in states.Keys.ToArray()) Forget(id);
    }
}
