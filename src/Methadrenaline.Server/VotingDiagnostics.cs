using System;
using System.Collections.Generic;
using System.Linq;
using CommandSystem;
using GameCore;
using HarmonyLib;
using LabApi.Events.Arguments.ServerEvents;
using LabApi.Events.Handlers;
using LabApi.Features.Wrappers;
using Methadrenaline.Server.Core;

namespace Methadrenaline.Server;

internal static class VotingDiagnostics
{
    internal static string Run(ServerPlugin plugin)
    {
        if (EventDirector.Participants.Any() || Round.IsRoundStarted)
            return "Voting diagnostics require an empty lobby.";
        var voting = plugin.ModeVoting;
        if (voting == null) return "Voting is disabled.";
        var policy = (ModeVotePolicy)AccessTools.Field(typeof(ModeVotingController), "policy").GetValue(voting);
        bool locked = Round.IsLobbyLocked;
        short timer = RoundStart.singleton.Timer;
        var next = plugin.PerkRounds!.NextMode;
        var checks = new List<string>();
        void Check(string name, bool result) => checks.Add(name + "=" + (result ? "PASS" : "FAIL"));
        try
        {
            var args = new ArraySegment<string>(new[] { "perks" });
            var command = new GameModeCommand();
            Check("automatic_start_still_waits", !CharacterClassManager.ForceRoundStart());
            policy.Tick(2, true, 0, _ => 0);
            policy.Tick(2, true, 31, _ => 0);
            Check("gamemode_after_poll_closes", command.Execute(args, ServerConsole.Scs, out _) && policy.Selected == CommunityRoundMode.Perks);
            Check("gamemode_replaces_countdown_selection", command.Execute(new ArraySegment<string>(new[] { "vanilla" }), ServerConsole.Scs, out _) && policy.Selected == CommunityRoundMode.Plain);
            Check("paravoz_requires_two_players", !command.Execute(new ArraySegment<string>(new[] { "paravoz" }), ServerConsole.Scs, out _));
            Check("invalid_mode_keeps_selection", !command.Execute(new ArraySegment<string>(new[] { "invalid" }), ServerConsole.Scs, out _) && policy.Selected == CommunityRoundMode.Plain);
            policy.Reset();
            policy.Tick(2, true, 0, _ => 0);
            Round.IsLobbyLocked = true;
            Check("native_lobby_unlock_command", new CommandSystem.Commands.RemoteAdmin.LobbyLockCommand().Execute(default, ServerConsole.Scs, out _) && !Round.IsLobbyLocked && !policy.Open);
            policy.Reset();
            policy.Tick(2, true, 0, _ => 0);
            Check("native_force_start_command", new CommandSystem.Commands.Shared.ForceStartCommand().Execute(default, ServerConsole.Scs, out _) && RoundStart.singleton.Timer == -1);
            Check("admin_start_scope_cleaned", !voting.AdministrativeStart);
            Check("force_start_closes_poll", !policy.Open && policy.Selected.HasValue);
            RoundStart.singleton.NetworkTimer = timer;
            ServerEvents.OnRoundRestarted();
            Check("restart_snapshot", voting.SnapshotForDiagnostics()?.Phase == "restarting");
            ServerEvents.OnWaitingForPlayers();
            Check("new_lobby_clears_admin_selection", !policy.AdminSelected && !policy.Selected.HasValue);
        }
        catch (Exception error) { checks.Add("FAIL: " + error); }
        finally
        {
            RoundStart.singleton.NetworkTimer = timer;
            ((System.Diagnostics.Stopwatch)AccessTools.Field(typeof(RoundStart), "RoundStartTimer").GetValue(null)).Reset();
            plugin.PerkRounds.NextMode = next;
            policy.Reset();
            voting.AdministrativeStart = false;
            Round.IsLobbyLocked = locked;
        }
        return string.Join("\n", checks);
    }
}
