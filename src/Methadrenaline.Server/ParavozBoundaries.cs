using System;
using System.Collections.Generic;
using System.Linq;
using CustomPlayerEffects;
using HarmonyLib;
using Interactables.Interobjects;
using LabApi.Features.Wrappers;
using MapGeneration;
using Methadrenaline.Server.Core;
using PlayerRoles.FirstPersonControl;
using PlayerStatsSystem;
using UnityEngine;

namespace Methadrenaline.Server;

internal sealed class ParavozBoundaries
{
    internal const string CampingMessage = "Suffocation in {0} s. Change your route!";
    internal const string IdleMessage = "Self-melting in {0} s! Run to survive!";
    private const float IdleWarning = 3;
    private const float SprintDisplacement = 0.1f;

    private sealed class Movement(Vector3 position)
    {
        internal readonly ParavozCamping<Room> Camping = new();
        internal Vector3 PreviousPosition = position;
        internal float? NotSprintingSince;
        internal (string Message, int Seconds)? Warning;
    }

    private sealed class Exposure(Player player, float firstDamage)
    {
        internal readonly Player Player = player;
        internal readonly int Life = player.LifeId;
        internal PocketCorroding? Effect;
        internal float NextDamage = firstDamage;
        internal int Ticks;
        internal UniversalDamageHandler? InFlightDamage;
    }

    private readonly Dictionary<uint, Movement> movement = new();
    private readonly Dictionary<uint, Exposure> exposures = new();

    internal static bool SurfaceLift(ElevatorChamber lift)
        => lift.AssignedGroup is ElevatorGroup.GateA01 or ElevatorGroup.GateA02 or ElevatorGroup.GateB;

    internal bool Owns(PocketCorroding effect)
        => exposures.TryGetValue(effect.Hub.netId, out var state) && state.Effect == effect && state.Life == state.Player.LifeId;

    internal bool IsExposureDamage(Player player, DamageHandlerBase damage)
        => exposures.TryGetValue(player.NetworkId, out var state) && ReferenceEquals(state.InFlightDamage, damage);

    internal (string Message, int Seconds)? Warning(Player player)
        => movement.TryGetValue(player.NetworkId, out var state) ? state.Warning : null;

    internal void Tick(Player player, float now)
    {
        if (player.RoleBase is not IFpcRole role) return;
        uint id = player.NetworkId;
        Vector3 position = player.Position;
        if (!movement.TryGetValue(id, out var state)) movement[id] = state = new Movement(position);
        Vector3 displacement = position - state.PreviousPosition;
        displacement.y = 0;
        state.PreviousPosition = position;
        state.Warning = null;
        uint waypoint = role.FpcModule.RelativePosition.WaypointId;
        if (waypoint != 0 && ElevatorChamber.AllChambers.Any(lift => !lift.IsReady && lift.Network_waypointId == waypoint))
        {
            state.NotSprintingSince = null;
            return;
        }

        var room = Room.GetRoomAtPosition(position);
        if (room == null) return;
        if (room.Zone == FacilityZone.Surface)
        {
            movement.Remove(id);
            Suffocate(player, now, multiplier: 1, initialDelay: 1);
            return;
        }

        if (!Interior(room))
        {
            Forget(id);
            return;
        }

        bool sprinting = role.FpcModule.CurrentMovementState == PlayerMovementState.Sprinting &&
            displacement.sqrMagnitude >= SprintDisplacement * SprintDisplacement;
        if (sprinting) state.NotSprintingSince = null;
        else state.NotSprintingSince ??= now;
        float idleRemaining = state.NotSprintingSince is float stopped
            ? stopped + IdleWarning - now : float.PositiveInfinity;
        var camp = state.Camping.Observe(room, now);
        float campingRemaining = camp is { } camping ? camping.Deadline - now : float.PositiveInfinity;
        if (idleRemaining <= 0 || campingRemaining <= 0)
        {
            float multiplier = idleRemaining <= 0 ? 1.6f : camp!.Value.Rooms switch
            {
                1 => 1.6f,
                2 => 1.35f,
                3 => 1.15f,
                _ => 1f
            };
            Suffocate(player, now, multiplier, initialDelay: 0, corrode: campingRemaining <= 0);
            return;
        }

        ClearExposure(id);
        if (idleRemaining <= IdleWarning && idleRemaining <= campingRemaining)
            state.Warning = (IdleMessage, (int)Math.Ceiling(idleRemaining));
        else if (camp != null)
            state.Warning = (CampingMessage, (int)Math.Ceiling(campingRemaining));
    }

    private void Suffocate(Player player, float now, float multiplier, float initialDelay, bool corrode = true)
    {
        uint id = player.NetworkId;
        if (!exposures.TryGetValue(id, out var state))
            exposures[id] = state = new Exposure(player, now + initialDelay);
        if (corrode)
        {
            if (state.Effect == null && player.GetEffect<PocketCorroding>() is { } effect)
            {
                state.Effect = effect;
                // This native intensity supplies the visuals and movement penalty without a teleport.
                effect.ServerSetState(106);
            }
        }
        // Pocket corrosion blocks sprinting on the client. Self-melting alone must leave a way to recover.
        else ClearCorrosion(state);
        if (now < state.NextDamage) return;
        state.NextDamage = now + 1;
        float damage = player.MaxHealth * multiplier * (0.02f + 0.0035f * state.Ticks++);
        var handler = new UniversalDamageHandler(damage, DeathTranslations.PocketDecay);
        state.InFlightDamage = handler;
        try { player.ReferenceHub.playerStats.DealDamage(handler); }
        finally { state.InFlightDamage = null; }
    }

    private static bool Interior(Room room)
        => room.Name != RoomName.Pocket && room.Zone is FacilityZone.LightContainment or FacilityZone.HeavyContainment or FacilityZone.Entrance;

    private void ClearExposure(uint id)
    {
        if (!exposures.TryGetValue(id, out var state)) return;
        exposures.Remove(id);
        ClearCorrosion(state);
    }

    private static void ClearCorrosion(Exposure state)
    {
        var effect = state.Effect;
        state.Effect = null;
        if (!state.Player.IsDestroyed && state.Player.LifeId == state.Life) effect?.ServerDisable();
    }

    internal void Forget(uint id)
    {
        movement.Remove(id);
        ClearExposure(id);
    }

    internal void Reset()
    {
        movement.Clear();
        foreach (uint id in exposures.Keys.ToArray()) ClearExposure(id);
    }
}

[HarmonyPatch(typeof(PocketCorroding), "OnTick")]
internal static class ParavozCorrosionPatch
{
    private static bool Prefix(PocketCorroding __instance)
        => ServerPlugin.Instance?.Paravoz?.Boundaries.Owns(__instance) != true;
}
