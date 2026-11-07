using System;
using CommandSystem;
using CommandSystem.Commands.Shared;
using HarmonyLib;
using LabApi.Features.Wrappers;
using Methadrenaline.Server.Core;

namespace Methadrenaline.Server;

[CommandHandler(typeof(RemoteAdminCommandHandler))]
[CommandHandler(typeof(GameConsoleCommandHandler))]
public sealed class GameModeCommand : ICommand
{
    public string Command => "gamemode";
    public string[] Aliases => Array.Empty<string>();
    public string Description => "Override the upcoming round mode: gamemode vanilla|perks|paravoz.";
    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!sender.CheckPermission(PlayerPermissions.RoundEvents, out response)) return false;
        if (arguments.Count != 1) { response = Description; return false; }
        string value = arguments.Array![arguments.Offset].ToLowerInvariant();
        if (value == "vanilla") value = "plain";
        if (!ModeVotePolicy.TryMode(value, out var mode)) { response = Description; return false; }
        var plugin = ServerPlugin.Instance;
        if (plugin == null) { response = "METHADRENALINE is disabled."; return false; }
        if (Round.IsRoundStarted || RoundRestarting.RoundRestart.IsRoundRestarting)
        { response = "Choose a mode in the lobby, before the round starts."; return false; }
        if (mode == CommunityRoundMode.Paravoz && System.Linq.Enumerable.Count(EventDirector.Participants) < 2)
        { response = "Paravoz requires at least two players."; return false; }
        plugin.PerkRounds!.NextMode = mode;
        plugin.ModeVoting?.OverrideMode(mode);
        ServerLogs.AddLog(ServerLogs.Modules.Administrative, sender.LogName + " selected round mode: " + value, ServerLogs.ServerLogType.RemoteAdminActivity_GameChanging);
        response = "Round mode selected: " + ModeVotePolicy.Label(mode);
        return true;
    }
}

[HarmonyPatch(typeof(ForceStartCommand), nameof(ForceStartCommand.Execute))]
internal static class AdministrativeRoundStartPatch
{
    private static void Prefix(ICommandSender sender, out bool __state)
    {
        __state = sender.CheckPermission(PlayerPermissions.RoundEvents) && !Round.IsRoundStarted;
        if (__state && ServerPlugin.Instance?.ModeVoting is { } voting) voting.AdministrativeStart = true;
    }
    private static void Finalizer(bool __state)
    {
        if (__state && ServerPlugin.Instance?.ModeVoting is { } voting) voting.AdministrativeStart = false;
    }
}

[HarmonyPatch(typeof(CommandSystem.Commands.RemoteAdmin.LobbyLockCommand), "Execute")]
internal static class AdministrativeLobbyLockPatch
{
    private static void Postfix(bool __result)
    {
        if (__result) ServerPlugin.Instance?.ModeVoting?.YieldLobbyControl();
    }
}
