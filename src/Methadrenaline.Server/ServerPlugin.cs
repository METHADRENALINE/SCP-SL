using System;
using System.Linq;
using CommandSystem;
using HarmonyLib;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Arguments.ServerEvents;
using LabApi.Events.Handlers;
using LabApi.Features.Console;
using LabApi.Features.Wrappers;
using LabApi.Loader.Features.Plugins;
using MEC;
using PlayerRoles;
using PlayerStatsSystem;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;

namespace Methadrenaline.Server;

public sealed class ServerPlugin : Plugin<Config>
{
    private const string PatchId = "methadrenaline.server";
    private Harmony? harmony;
    internal static ServerPlugin? Instance { get; private set; }
    internal EventDirector Director { get; private set; } = null!;
    internal LocalizationService? Localization { get; private set; }
    internal PerkRoundController? PerkRounds { get; private set; }
    internal BraceletController? Bracelets { get; private set; }
    internal ParavozController? Paravoz { get; private set; }
    internal ModeVotingController? ModeVoting { get; private set; }
    public override string Name => "METHADRENALINE";
    public override string Author => "Wechirok";
    public override string Description => "Private community gameplay and temporary facility anomalies.";
    public override Version Version => new(1, 15, 2);
    public override LabApi.Loader.Features.Plugins.Enums.LoadPriority Priority => LabApi.Loader.Features.Plugins.Enums.LoadPriority.Lowest;
    public override Version RequiredApiVersion => new(1, 1, 7);

    public override void Enable()
    {
        Config.Validate();
        Instance = this;
        Director = new EventDirector(Config);
        try
        {
            harmony = new Harmony(PatchId);
            Localization = new LocalizationService();
            PerkRounds = new PerkRoundController();
            Bracelets = new BraceletController();
            Paravoz = new ParavozController();
            harmony.PatchAll(typeof(ServerPlugin).Assembly);
            Localization.Enable();
            PerkRounds.Initialize();
            Bracelets.Enable();
            Paravoz.Enable();
            if (Config.ModeVotingEnabled)
            {
                ModeVoting = new ModeVotingController(Config, Localization);
                ModeVoting.Enable();
            }
            ServerEvents.RoundStarted += RoundStarted;
            ServerEvents.RoundEnded += RoundEnded;
            ServerEvents.WaitingForPlayers += Reset;
            ServerEvents.RoundRestarted += Reset;
            ServerEvents.WaveRespawned += WaveRespawned;
            ServerEvents.LczDecontaminationStarting += DecontaminationStarting;
            WarheadEvents.Starting += WarheadStarting;
            PlayerEvents.ChangedRole += ChangedRole;
            PlayerEvents.ChangingRole += ChangingRole;
            PlayerEvents.Left += Left;
            PlayerEvents.Hurting += Hurting;
            PlayerEvents.InteractingDoor += InteractingDoor;
            PlayerEvents.UsingRadio += UsingRadio;
            Logger.Info($"[METHADRENALINE] Enabled: preferences={Config.PreferredScpRoles}, maximum SCPs=1, radios={Config.RadiosForHumans}, events={Config.EventsEnabled}, chaos=1-2 bundles/round, chaos notifications=off; modes=Vanilla (almost)/Perks/Paravoz; radios=infinite; random_grenades=off.");
        }
        catch
        {
            Disable();
            throw;
        }
    }

    public override void Disable()
    {
        ServerEvents.RoundStarted -= RoundStarted;
        ServerEvents.RoundEnded -= RoundEnded;
        ServerEvents.WaitingForPlayers -= Reset;
        ServerEvents.RoundRestarted -= Reset;
        ServerEvents.WaveRespawned -= WaveRespawned;
        ServerEvents.LczDecontaminationStarting -= DecontaminationStarting;
        WarheadEvents.Starting -= WarheadStarting;
        PlayerEvents.ChangedRole -= ChangedRole;
        PlayerEvents.ChangingRole -= ChangingRole;
        PlayerEvents.Left -= Left;
        PlayerEvents.Hurting -= Hurting;
        PlayerEvents.InteractingDoor -= InteractingDoor;
        PlayerEvents.UsingRadio -= UsingRadio;
        ModeVoting?.Dispose();
        ModeVoting = null;
        Paravoz?.Dispose();
        Paravoz = null;
        Director?.Dispose();
        Bracelets?.Dispose();
        Bracelets = null;
        PerkRounds?.Dispose();
        PerkRounds = null;
        Localization?.Dispose();
        Localization = null;
        harmony?.UnpatchAll(PatchId);
        harmony = null;
        if (Instance == this) Instance = null;
    }

    private void RoundStarted()
    {
        Director.BeginRound();
        if (PerkRounds?.Policy.Mode == Methadrenaline.Server.Core.CommunityRoundMode.Paravoz) Paravoz!.BeginRound();
        ConfigureRespawns();
    }

    private void WaveRespawned(WaveRespawnedEventArgs _) => Timing.CallDelayed(0.25f, () =>
    {
        if (Instance == this && Round.IsRoundInProgress) ConfigureRespawns();
    });

    private void ConfigureRespawns()
    {
        if (EventDirector.Participants.Count() > 6) return;
        foreach (var wave in new RespawnWave?[] { RespawnWaves.PrimaryMtfWave, RespawnWaves.PrimaryChaosWave })
        {
            if (wave != null && wave.TimeLeft > Config.SmallServerRespawnLimit)
                wave.TimeLeft = Config.SmallServerRespawnLimit;
        }
    }
    private void RoundEnded(RoundEndedEventArgs _) { Director.Reset(); Paravoz?.Reset(); }
    private void Reset() { Director.Reset(); Paravoz?.Reset(); }
    private void UsingRadio(PlayerUsingRadioEventArgs ev) { ev.Drain = 0; }
    private void DecontaminationStarting(LczDecontaminationStartingEventArgs _) => Director.Stop();
    private void WarheadStarting(LabApi.Events.Arguments.WarheadEvents.WarheadStartingEventArgs _) => Director.Stop();
    private void ChangingRole(PlayerChangingRoleEventArgs ev) => Director.ForgetPlayer(ev.Player);
    private void Left(PlayerLeftEventArgs ev) => Director.ForgetPlayer(ev.Player);
    private void InteractingDoor(PlayerInteractingDoorEventArgs ev) => Director.DoorTouched(ev.Door);

    private void Hurting(PlayerHurtingEventArgs ev)
    {
        Paravoz?.Hurt(ev);
        if (ev.DamageHandler is UniversalDamageHandler damage && damage.TranslationId == DeathTranslations.Falldown.Id && Director.ProtectFromFall(ev.Player))
            ev.IsAllowed = false;
    }

    private void ChangedRole(PlayerChangedRoleEventArgs ev)
    {
        Player player = ev.Player;
        int life = player.LifeId;
        Timing.CallDelayed(0.2f, () =>
        {
            if (Instance != this || player.IsDestroyed || player.LifeId != life) return;
            UltimateHUD.Hints.RefreshAll(player);
            foreach (var target in EventDirector.Participants.Where(p => p.IsAlive)) UltimateHUD.Hints.RefreshSpectatorList(target);
        });
        Timing.CallDelayed(0.25f, () =>
        {
            if (Instance != this || !player.GameObject || !player.IsHuman || player.IsTutorial || player.LifeId != life || player.IsHost || player.IsNpc || player.IsDummy) return;
            if (Paravoz?.IsParavoz(player) == true) return;
            try
            {
                if (Config.RadiosForHumans && !player.Items.Any(item => item.Type == ItemType.Radio))
                {
                    if (player.AddItem(ItemType.Radio) == null)
                        Pickup.Create(ItemType.Radio, player.Position + Vector3.up * 0.3f);
                }
                if (Config.ClassDStartingCard && player.Role == RoleTypeId.ClassD && !player.Items.Any(item => item.Type.ToString().StartsWith("Keycard", StringComparison.Ordinal)))
                    player.AddItem(ItemType.KeycardScientist);
            }
            catch (Exception error) { Logger.Error($"[METHADRENALINE] Starter equipment failed: {error}"); }
        });
    }
}

[CommandHandler(typeof(GameConsoleCommandHandler))]
[CommandHandler(typeof(RemoteAdminCommandHandler))]
public sealed class CommunityCommand : ICommand
{
    public string Command => "md";
    public string[] Aliases => Array.Empty<string>();
    public string Description => "Facility events: status, event <name>, stop, pause, resume, diagnose, localization.";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!sender.CheckPermission(PlayerPermissions.FacilityManagement)) { response = "FacilityManagement permission required."; return false; }
        var plugin = ServerPlugin.Instance;
        if (plugin == null) { response = "METHADRENALINE is disabled."; return false; }
        string action = arguments.Count == 0 ? "status" : arguments.Array![arguments.Offset].ToLowerInvariant();
        switch (action)
        {
            case "status":
                bool patched = Harmony.GetPatchInfo(AccessTools.Method(typeof(PlayerRoles.RoleAssign.ScpSpawner), nameof(PlayerRoles.RoleAssign.ScpSpawner.SpawnScps)))?.Prefixes.Any(p => p.owner == "methadrenaline.server") == true;
                response = $"{plugin.Director.Status}; role_patch={patched}; mtf_wait={RespawnWaves.PrimaryMtfWave?.TimeLeft:0}; chaos_wait={RespawnWaves.PrimaryChaosWave?.TimeLeft:0}; {plugin.PerkRounds?.Status}; {plugin.Bracelets?.Status}; {plugin.Paravoz?.Status}";
                return patched;
            case "stop": plugin.Director.Stop(); response = plugin.Director.Status; return true;
            case "pause": plugin.Director.Pause(true); response = plugin.Director.Status; return true;
            case "resume": plugin.Director.Pause(false); response = plugin.Director.Status; return true;
            case "diagnose": response = plugin.Director.Diagnostics(); return !response.Contains("FAIL");
            case "gameplay": response = GameplayDiagnostics.Run(plugin); return response == "Gameplay diagnostics started.";
            case "voting": response = VotingDiagnostics.Run(plugin); return !response.Contains("FAIL") && !response.Contains("require");
            case "localization": response = plugin.Localization == null ? "Localization is disabled." : plugin.Localization.Diagnostics() + "\n" + LocalizationDiagnostics.Run(plugin.Localization); return !response.Contains("FAIL");
            case "perks": response = arguments.Count == 2 && arguments.Array![arguments.Offset + 1] == "diagnose" ? PerkRoundDiagnostics.Run(plugin.PerkRounds!) : plugin.PerkRounds!.Status; return !response.Contains("FAIL");
            case "bracelets":
                if (arguments.Count == 3 && arguments.Array![arguments.Offset + 1] == "captor-test" && int.TryParse(arguments.Array![arguments.Offset + 2], out int captorId))
                { response = plugin.Bracelets!.Playtest!.StartCaptor(captorId); return response == "Captor bracelet playtest preparing."; }
                if (arguments.Count == 3 && arguments.Array![arguments.Offset + 1] == "test" && int.TryParse(arguments.Array![arguments.Offset + 2], out int targetId))
                { response = plugin.Bracelets!.Playtest!.Start(targetId); return response == "Bracelet playtest preparing."; }
                if (arguments.Count == 2 && arguments.Array![arguments.Offset + 1] == "cancel")
                { plugin.Bracelets!.Playtest!.Dispose(); response = plugin.Bracelets.Status; return true; }
                response = arguments.Count == 2 && arguments.Array![arguments.Offset + 1] == "diagnose" ? BraceletDiagnostics.Run(plugin.Bracelets!) : plugin.Bracelets!.Status;
                return !response.Contains("FAIL");
            case "mode":
                if (arguments.Count == 4 && arguments.Array![arguments.Offset + 1].Equals("solo", StringComparison.OrdinalIgnoreCase) &&
                    (arguments.Array[arguments.Offset + 2].Equals("carrier", StringComparison.OrdinalIgnoreCase) || arguments.Array[arguments.Offset + 2].Equals("runner", StringComparison.OrdinalIgnoreCase)) &&
                    int.TryParse(arguments.Array[arguments.Offset + 3], out int soloId))
                { response = plugin.Paravoz!.BeginSolo(soloId, arguments.Array[arguments.Offset + 2].Equals("carrier", StringComparison.OrdinalIgnoreCase)); return response == "Solo Paravoz test started."; }
                if (arguments.Count == 2 && arguments.Array![arguments.Offset + 1].Equals("cancel", StringComparison.OrdinalIgnoreCase))
                {
                    if (!plugin.Paravoz!.SoloActive) { response = "Solo Paravoz test is not active."; return false; }
                    plugin.Paravoz.Reset(); response = "Solo Paravoz test stopped."; return true;
                }
                if (arguments.Count == 3 && arguments.Array![arguments.Offset + 1].Equals("next", StringComparison.OrdinalIgnoreCase) &&
                    Enum.TryParse<Methadrenaline.Server.Core.CommunityRoundMode>(arguments.Array[arguments.Offset + 2], true, out var nextMode) && Enum.IsDefined(typeof(Methadrenaline.Server.Core.CommunityRoundMode), nextMode))
                { plugin.PerkRounds!.NextMode = nextMode; response = "Next round mode: " + nextMode; return true; }
                response = plugin.Paravoz!.Status + "; next=" + (plugin.PerkRounds!.NextMode?.ToString() ?? "random"); return true;
            case "grenade":
                if (!Round.IsRoundInProgress) { response = "An active round is required."; return false; }
                response = plugin.Director.SpawnRandomGrenade() ? "Grenade spawned." : "No eligible player."; return response == "Grenade spawned.";
            case "event":
                if (arguments.Count != 2) { response = "Usage: md event chaos|gravity|levitation|blackout|doors|signals"; return false; }
                return plugin.Director.TryStart(arguments.Array![arguments.Offset + 1].ToLowerInvariant(), false, out response);
            default: response = Description; return false;
        }
    }
}
