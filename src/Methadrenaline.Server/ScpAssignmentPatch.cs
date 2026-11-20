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

    internal static IReadOnlyDictionary<int, int> ChooseRoles(ReferenceHub[] chosen)
    {
        var pool = PlayerRoleLoader.AllRoles.Where(pair => pair.Value is ISpawnableScp).ToDictionary(pair => (int)pair.Key, pair => (ISpawnableScp)pair.Value);
        var ballots = chosen.Select(hub =>
        {
            if (ScpSpawnPreferences.Preferences.TryGetValue(hub.connectionToClient.connectionId, out var value))
                return (IReadOnlyDictionary<int, int>)value.Preferences.ToDictionary(pair => (int)pair.Key, pair => pair.Value);
            return new Dictionary<int, int>();
        }).ToArray();

        return RolePolicy.Allocate(ballots, pool.Keys.ToArray(), Random,
            (role, previous) => pool[role].GetSpawnChance(previous.Select(r => (RoleTypeId)r).ToList()) > 0);
    }

    private static bool Prefix(ref int targetScpNumber)
    {
        var plugin = ServerPlugin.Instance;
        if (plugin?.PerkRounds?.PrepareMode() == CommunityRoundMode.Escort)
        {
            plugin.Escort.Spawn(EventDirector.Participants.Where(p => RoleAssigner.CheckPlayer(p.ReferenceHub)).ToArray());
            return false;
        }
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

            var assignments = ChooseRoles(chosen);
            committed = true;
            foreach (var assignment in assignments)
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
