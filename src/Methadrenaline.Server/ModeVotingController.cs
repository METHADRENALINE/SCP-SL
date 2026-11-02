using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using LabApi.Events.Arguments.ServerEvents;
using LabApi.Events.Handlers;
using LabApi.Features.Wrappers;
using Methadrenaline.Server.Core;
using Methadrenaline.Server.Voting;
using RueI.API;
using RueI.API.Elements;

namespace Methadrenaline.Server;

internal sealed class ModeVotingController : IDisposable
{
    private static readonly Tag HudTag = new("methadrenaline.mode-vote");
    private readonly string bootId = Guid.NewGuid().ToString("D");
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Random random = new();
    private readonly ModeVotePolicy policy = new();
    private readonly ModeVoteBridge bridge = new();
    private readonly HashSet<Player> viewers = new();
    private readonly LocalizationService localization;
    private readonly ModeVoteHttpService http;
    private bool waiting;
    private bool ended;
    private bool ownsLock;
    private bool disposed;
    private bool restarting;
    internal bool AdministrativeStart { get; set; }
    internal ModeVoteStatus? SnapshotForDiagnostics() => bridge.Snapshot == null ? null : System.Text.Json.JsonSerializer.Deserialize<ModeVoteStatus>(bridge.Snapshot.Json);
    private long heartbeat;
    private double nextTick;
    private string? deadlinePoll;
    private string? deadline;

    internal ModeVotingController(Config config, LocalizationService localization)
    {
        this.localization = localization;
        string token = ModeVoteTokenFile.Load(config.ModeVotingTokenFile);
        http = new ModeVoteHttpService(config.ModeVotingBindAddress, config.ModeVotingPort, token, bridge,
            error => LabApi.Features.Console.Logger.Error("[METHADRENALINE] Voting HTTP failure: " + error.GetType().FullName + "\n" + error.StackTrace));
    }

    internal void Enable()
    {
        waiting = !Round.IsRoundStarted;
        http.Start();
        ServerEvents.WaitingForPlayers += Waiting;
        ServerEvents.RoundRestarted += Restarted;
        ServerEvents.RoundStarting += Starting;
        ServerEvents.RoundStarted += Started;
        ServerEvents.RoundEnded += Ended;
        global::StaticUnityMethods.OnFixedUpdate += Update;
        Tick();
    }

    private void Waiting() { ReleaseLock(); policy.Reset(); waiting = true; ended = restarting = false; Tick(); }
    private void Restarted() { ReleaseLock(); policy.Reset(); waiting = false; ended = restarting = true; Tick(); }
    private void Started() { waiting = false; ended = false; ReleaseLock(); Tick(); }
    private void Ended(RoundEndedEventArgs _) { waiting = false; ended = true; ReleaseLock(); Tick(); }
    private void Starting(RoundStartingEventArgs ev)
    {
        Tick();
        if (AdministrativeStart)
        {
            policy.Override(policy.Finish(random.Next));
            ReleaseLock();
            Tick();
        }
        else if (policy.Open || !policy.AdminSelected && policy.Players < ModeVotePolicy.MinimumPlayers) ev.IsAllowed = false;
    }

    private void Update()
    {
        if (clock.Elapsed.TotalSeconds < nextTick) return;
        nextTick = clock.Elapsed.TotalSeconds + 0.1;
        Tick();
    }

    private void Tick()
    {
        if (disposed) return;
        double now = clock.Elapsed.TotalSeconds;
        Player[] players = EventDirector.Participants.ToArray();
        policy.Tick(players.Length, waiting, now, random.Next);
        if (policy.Open)
        {
            if (!Round.IsLobbyLocked) { ownsLock = true; Round.IsLobbyLocked = true; }
        }
        else ReleaseLock();
        if (deadlinePoll != policy.PollId)
        {
            deadlinePoll = policy.PollId;
            deadline = policy.PollId == null ? null : DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, policy.Deadline - now)).ToString("O", CultureInfo.InvariantCulture);
        }
        bridge.Drain(request => request.BootId == bootId && ModeVotePolicy.TryMode(request.Mode, out var mode)
            ? policy.Vote(request.PollId, request.UserId, mode, clock.Elapsed.TotalSeconds) : "closed");
        CommunityRoundMode? selected = players.Length == 0 ? null : waiting ? policy.Selected : ServerPlugin.Instance?.PerkRounds?.Policy.Mode;
        bridge.Publish(new ModeVoteStatus
        {
            BootId = bootId,
            HeartbeatSequence = ++heartbeat,
            HeartbeatAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            OnlineNames = players.Take(128).Select(p => ModeVoteNames.PlayerName(p.Nickname, p.DisplayName)).ToArray(),
            Phase = restarting ? "restarting" : players.Length == 0 ? "empty" : waiting ? policy.Open ? "voting" : "countdown" : ended ? "ended" : "round",
            Mode = selected.HasValue ? ModeVotePolicy.Id(selected.Value) : null,
            ModeNameRu = selected.HasValue ? RussianName(selected.Value) : null,
            AvailableModes = policy.Available.Select(m => new ModeVoteOption { Id = ModeVotePolicy.Id(m), NameRu = RussianName(m) }).ToArray(),
            Poll = new ModeVotePoll { Id = policy.PollId, Deadline = deadline, Open = policy.Open }
        });
        UpdateHud(waiting && players.Length >= ModeVotePolicy.MinimumPlayers ? players : Array.Empty<Player>());
    }

    private string RussianName(CommunityRoundMode mode) => localization.Catalog.Translate(ModeVotePolicy.Label(mode), ServerLanguage.Russian);

    internal CommunityRoundMode SelectedMode()
    {
        Tick();
        return policy.Finish(random.Next);
    }

    internal void OverrideMode(CommunityRoundMode mode)
    {
        policy.Override(mode);
        ReleaseLock();
        Tick();
    }

    internal void YieldLobbyControl()
    {
        if (!policy.Open) return;
        bool locked = Round.IsLobbyLocked;
        policy.Override(policy.Finish(random.Next));
        ownsLock = false;
        Round.IsLobbyLocked = locked;
        Tick();
    }

    private string Hud(Player player)
    {
        if (!waiting || policy.Players < ModeVotePolicy.MinimumPlayers || disposed) return "";
        var language = localization.Language(player.ReferenceHub);
        string line = policy.Open
            ? string.Format(CultureInfo.InvariantCulture, localization.Catalog.Translate(ModeVotePolicy.VotingLabel, language), Math.Max(0, (int)Math.Ceiling(policy.Deadline - clock.Elapsed.TotalSeconds)))
            : policy.Selected.HasValue ? localization.Catalog.Translate(ModeVotePolicy.Label(policy.Selected.Value), language) : "";
        return "<align=center><size=22>" + line + "</size></align>";
    }

    private void UpdateHud(Player[] players)
    {
        foreach (var player in viewers.Where(p => !players.Contains(p)).ToArray())
        {
            viewers.Remove(player);
            if (!player.IsDestroyed) RueDisplay.Get(player).Remove(HudTag);
        }
        foreach (var player in players)
            if (viewers.Add(player)) RueDisplay.Get(player).Show(HudTag,
                new DynamicElement(120, hub => hub == player.ReferenceHub ? Hud(player) : "") { UpdateInterval = TimeSpan.FromSeconds(0.1) });
    }

    private void ReleaseLock()
    {
        if (!ownsLock) return;
        Round.IsLobbyLocked = false;
        ownsLock = false;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        ServerEvents.WaitingForPlayers -= Waiting;
        ServerEvents.RoundRestarted -= Restarted;
        ServerEvents.RoundStarting -= Starting;
        ServerEvents.RoundStarted -= Started;
        ServerEvents.RoundEnded -= Ended;
        global::StaticUnityMethods.OnFixedUpdate -= Update;
        ReleaseLock();
        UpdateHud(Array.Empty<Player>());
        bridge.Dispose();
        http.Dispose();
    }
}
