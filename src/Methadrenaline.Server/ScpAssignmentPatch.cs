using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LabApi.Features.Console;
using LabApi.Features.Wrappers;
using Methadrenaline.Server.Core;
using PlayerRoles;
using PlayerRoles.PlayableScps;
using PlayerRoles.RoleAssign;

namespace Methadrenaline.Server;

[HarmonyPatch(typeof(ScpSpawner), nameof(ScpSpawner.SpawnScps))]
internal static class ScpAssignmentPatch
{
    private static readonly Random Random = new();

    private static bool Prefix(ref int targetScpNumber)
    {
        var plugin = ServerPlugin.Instance;
        // Keep the native fallback (including disabled preferences) capped too.
        targetScpNumber = Math.Min(targetScpNumber, 1);
        if (plugin == null || !plugin.Config.PreferredScpRoles) return true;
        bool committed = false;
        try
        {
            var participants = Player.List.Where(p => !p.IsHost && !p.IsNpc && !p.IsDummy && RoleAssigner.CheckPlayer(p.ReferenceHub)).ToArray();
            int willing = participants.Count(p => !ScpPlayerPicker.IsOptedOutOfScp(p.ReferenceHub));
            targetScpNumber = RolePolicy.ScpCount(participants.Length, willing);
            if (targetScpNumber == 0) return false;

            var chosen = ScpPlayerPicker.ChoosePlayers(targetScpNumber).ToArray();
            if (chosen.Length != targetScpNumber || chosen.Any(ScpPlayerPicker.IsOptedOutOfScp))
                throw new InvalidOperationException("Native player selection did not produce the expected eligible participants.");

            var pool = PlayerRoleLoader.AllRoles.Where(pair => pair.Value is ISpawnableScp).ToDictionary(pair => (int)pair.Key, pair => (ISpawnableScp)pair.Value);
            var ballots = chosen.Select(hub =>
            {
                if (ScpSpawnPreferences.Preferences.TryGetValue(hub.connectionToClient.connectionId, out var value))
                    return (IReadOnlyDictionary<int, int>)value.Preferences.ToDictionary(pair => (int)pair.Key, pair => pair.Value);
                return new Dictionary<int, int>();
            }).ToArray();

            var assignments = RolePolicy.Allocate(ballots, pool.Keys.ToArray(), Random,
                (role, previous) => pool[role].GetSpawnChance(previous.Select(r => (RoleTypeId)r).ToList()) > 0);

            // Never fall back to a second spawn pass after any roles have been applied.
            committed = true;
            foreach (var assignment in assignments.OrderBy(pair => pool[pair.Value].GetSpawnChance(new List<RoleTypeId>()) > 0 ? 0 : 1))
                chosen[assignment.Key].roleManager.ServerSetRole((RoleTypeId)assignment.Value, RoleChangeReason.RoundStart);
            Logger.Info($"[METHADRENALINE] Assigned {assignments.Count} SCP roles by vanilla preferences for {participants.Length} participants.");
            return false;
        }
        catch (Exception error)
        {
            Logger.Error($"[METHADRENALINE] Role assignment failed; vanilla fallback={!committed}: {error}");
            return !committed;
        }
    }
}
