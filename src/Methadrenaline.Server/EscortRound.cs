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
    private readonly HashSet<uint> pendingEscapes = new();
    private readonly HashSet<BraceletLife> evacuees = new();
    private BraceletLife? guard;
    private bool guardKilled;
    internal bool Active { get; private set; }

    internal void Enable()
    {
        ServerEvents.WaveRespawning += WaveRespawning;
        ServerEvents.RoundEndingConditionsCheck += CheckEnding;
        ServerEvents.RoundEnding += RoundEnding;
        PlayerEvents.Uncuffing += Uncuffing;
        PlayerEvents.ChangingRole += ChangingRole;
        PlayerEvents.Escaping += Escaping;
        PlayerEvents.Escaped += Escaped;
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

    internal bool IsGuardIdentity(Player player) => Active && guard?.NetworkId == player.NetworkId;
    internal bool IsPrisoner(BraceletLife life) => prisoners.Contains(life);
    internal void MarkGuardKilled() => guardKilled = true;

    private static bool IsAlive(BraceletLife life, Team team) =>
        Player.Get(life.NetworkId) is { IsAlive: true } player && player.Team == team && player.LifeId == life.LifeId;

    private RoundSummary.LeadingTeam? Result(out bool livingPrisoners)
    {
        livingPrisoners = prisoners.Any(life => IsAlive(life, Team.ClassD));
        if (livingPrisoners || prisoners.Count == 0) return null;
        if (!evacuees.Any(life => IsAlive(life, Team.FoundationForces))) return RoundSummary.LeadingTeam.Anomalies;
        if (evacuees.Count == prisoners.Count && guard is { } officer && IsAlive(officer, Team.FoundationForces))
            return RoundSummary.LeadingTeam.FacilityForces;
        return null;
    }

    private void CheckEnding(RoundEndingConditionsCheckEventArgs ev)
    {
        if (!Active) return;
        if (Result(out bool livingPrisoners).HasValue) ev.CanEnd = true;
        else if (guardKilled && livingPrisoners) ev.CanEnd = false;
    }

    private void RoundEnding(RoundEndingEventArgs ev)
    {
        if (!Active) return;
        if (Result(out bool livingPrisoners) is { } winner) ev.LeadingTeam = winner;
        else if (guardKilled && livingPrisoners) ev.IsAllowed = false;
    }

    private void Escaping(PlayerEscapingEventArgs ev)
    {
        if (!Active || ev.OldRole != RoleTypeId.ClassD) return;
        var player = ev.Player;
        pendingEscapes.Remove(player.NetworkId);
        if (!ev.IsAllowed || guard is not { } officer || !Locked(player) ||
            ServerPlugin.Instance?.Bracelets?.Ledger.TryGet(BraceletController.Life(player), out var binding) != true ||
            !binding.Captor.Equals(officer)) return;
        if (binding.Committed) { ev.IsAllowed = false; return; }
        if (ev.EscapeScenario == Escape.EscapeScenarioType.CuffedClassD) pendingEscapes.Add(player.NetworkId);
    }

    private void Escaped(PlayerEscapedEventArgs ev)
    {
        if (!Active || !pendingEscapes.Remove(ev.Player.NetworkId)) return;
        if (ev.EscapeScenarioType == Escape.EscapeScenarioType.CuffedClassD &&
            ev.Player.IsAlive && ev.Player.Role == ev.NewRole && ev.Player.Team == Team.FoundationForces)
            evacuees.Add(BraceletController.Life(ev.Player));
    }

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
        pendingEscapes.Clear();
        evacuees.Clear();
        guard = null;
        guardKilled = false;
    }

    internal void Disable()
    {
        ServerEvents.WaveRespawning -= WaveRespawning;
        ServerEvents.RoundEndingConditionsCheck -= CheckEnding;
        ServerEvents.RoundEnding -= RoundEnding;
        PlayerEvents.Uncuffing -= Uncuffing;
        PlayerEvents.ChangingRole -= ChangingRole;
        PlayerEvents.Escaping -= Escaping;
        PlayerEvents.Escaped -= Escaped;
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
