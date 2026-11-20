using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using InventorySystem.Disarming;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Arguments.ServerEvents;
using LabApi.Events.Handlers;
using LabApi.Features.Wrappers;
using Methadrenaline.Server.Core;
using PlayerRoles;
using PlayerRoles.FirstPersonControl.Spawnpoints;
using PlayerRoles.RoleAssign;
using UnityEngine;

namespace Methadrenaline.Server;

internal sealed class EscortRound
{
    private readonly System.Random random = new();
    private readonly HashSet<BraceletLife> prisoners = new();
    private BraceletLife? guard;
    internal bool Active { get; private set; }

    internal void Enable()
    {
        ServerEvents.WaveRespawning += WaveRespawning;
        PlayerEvents.Uncuffing += Uncuffing;
        PlayerEvents.ChangingRole += ChangingRole;
    }

    internal void Spawn(Player[] players)
    {
        if (Active) return;
        if (players.Length < ModeVotePolicy.EscortMinimum)
            throw new InvalidOperationException("Escort needs a guard, a prisoner and an SCP.");

        var cells = ParavozController.ClassDCellDoors();
        var places = new List<Vector3>();
        for (int i = 0; i < Math.Max(12, cells.Length * 2); i++)
        {
            if (cells.Length == 0 || !RoleSpawnpointManager.TryGetSpawnpointForRole(RoleTypeId.ClassD, out var spawn) ||
                !spawn.TryGetSpawnpoint(out var inside, out _)) continue;
            var door = cells.OrderBy(d => (d.Position - inside).sqrMagnitude).First();
            var normal = Vector3.ProjectOnPlane(door.Base.transform.forward, Vector3.up).normalized;
            if (Vector3.Dot(normal, inside - door.Position) > 0) normal = -normal;
            var point = door.Position + normal * 1.5f;
            point.y = inside.y;
            if (places.All(p => (p - point).sqrMagnitude > 0.25f)) places.Add(point);
        }
        if (places.Count == 0) throw new InvalidOperationException("No Class-D corridor spawn points.");
        var center = places.Aggregate(Vector3.zero, (sum, p) => sum + p) / places.Count;
        places = places.OrderBy(p => (p - center).sqrMagnitude).ToList();
        places.RemoveAll(p => (p - places[0]).sqrMagnitude > 144f);

        var candidates = players.Where(p => !ScpPlayerPicker.IsOptedOutOfScp(p.ReferenceHub)).ToArray();
        var picked = candidates.Length > 0 ? PickScp(candidates) : null;
        var scp = candidates.Length == 0 ? players[random.Next(players.Length)] :
            candidates.FirstOrDefault(p => p.ReferenceHub == picked) ?? candidates[random.Next(candidates.Length)];
        var role = (RoleTypeId)ScpAssignmentPatch.ChooseRoles(new[] { scp.ReferenceHub })[0];
        var humans = players.Where(p => p != scp).ToArray();
        var officer = humans[random.Next(humans.Length)];
        Active = true;
        scp.SetRole(role, RoleChangeReason.RoundStart);
        officer.SetRole(RoleTypeId.FacilityGuard, RoleChangeReason.RoundStart, RoleSpawnFlags.AssignInventory);
        officer.Position = places[0];
        guard = BraceletController.Life(officer);
        int slot = 0;
        foreach (var prisoner in humans.Where(p => p != officer))
        {
            prisoner.SetRole(RoleTypeId.ClassD, RoleChangeReason.RoundStart, RoleSpawnFlags.AssignInventory);
            prisoner.Position = places[++slot % places.Count];
            prisoner.DisarmedBy = officer;
            prisoners.Add(BraceletController.Life(prisoner));
            ServerPlugin.Instance!.Bracelets!.Bind(officer, prisoner);
        }
    }

    private static ReferenceHub? PickScp(Player[] candidates) =>
        ScpPlayerPicker.ChoosePlayers(1).FirstOrDefault(h => candidates.Any(p => p.ReferenceHub == h));

    internal bool IsGuard(Player player) => Active && player.Role == RoleTypeId.FacilityGuard &&
        guard.HasValue && guard.Value.Equals(BraceletController.Life(player));

    internal bool Locked(Player? target) => Active && target?.Role == RoleTypeId.ClassD &&
        prisoners.Contains(BraceletController.Life(target)) && target.DisarmedBy is { } owner && IsGuard(owner);

    private void Uncuffing(PlayerUncuffingEventArgs ev)
    {
        if (Locked(ev.Target)) ev.IsAllowed = false;
    }

    private void WaveRespawning(WaveRespawningEventArgs ev)
    {
        if (Active) ev.IsAllowed = false;
    }

    private void ChangingRole(PlayerChangingRoleEventArgs ev)
    {
        if (Active && ev.ChangeReason is RoleChangeReason.Respawn or RoleChangeReason.RespawnMiniwave
            or RoleChangeReason.LateJoin or RoleChangeReason.Revived or RoleChangeReason.Resurrected)
            ev.IsAllowed = false;
    }

    internal void Reset()
    {
        Active = false;
        prisoners.Clear();
        guard = null;
    }

    internal void Disable()
    {
        ServerEvents.WaveRespawning -= WaveRespawning;
        PlayerEvents.Uncuffing -= Uncuffing;
        PlayerEvents.ChangingRole -= ChangingRole;
        Reset();
    }
}

[HarmonyPatch(typeof(HumanSpawner), nameof(HumanSpawner.SpawnHumans))]
internal static class EscortHumanSpawnPatch
{
    private static bool Prefix() => ServerPlugin.Instance?.Escort.Active != true;
}

[HarmonyPatch(typeof(RoleAssigner), "CheckLateJoin")]
internal static class EscortLateJoinPatch
{
    private static bool Prefix() => ServerPlugin.Instance?.Escort.Active != true;
}
