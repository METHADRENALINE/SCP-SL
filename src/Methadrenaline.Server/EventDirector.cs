using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Interactables.Interobjects.DoorUtils;
using LabApi.Features.Enums;
using LabApi.Features.Wrappers;
using MapGeneration;
using MEC;
using Methadrenaline.Server.Core;
using PlayerRoles.FirstPersonControl;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;

namespace Methadrenaline.Server;

internal sealed class EventDirector : IDisposable
{
    private static readonly FieldInfo FlickerDuration = AccessTools.Field(typeof(RoomLightController), "_flickerDuration");
    private readonly Config config;
    private readonly System.Random random = new();
    private readonly Dictionary<string, ActiveEvent> active = new(StringComparer.Ordinal);
    private readonly Dictionary<Player, GravityState> gravity = new();
    private readonly Dictionary<Player, LandingProtection> fallGrace = new();
    private CoroutineHandle scheduler;
    private ChaosRoundPlan? plan;
    private float roundStartedAt;
    private int generation;
    private bool paravozRound;
    private bool disposed;

    public EventDirector(Config config)
    {
        this.config = config;
        global::StaticUnityMethods.OnLateUpdate += UpdateLandingProtection;
    }
    public bool Paused { get; private set; }
    public string Status => $"event={(active.Count == 0 ? "none" : string.Join(",", active.Keys.OrderBy(name => name)))}; paused={Paused}; rollback={active.Values.Sum(run => run.Scope.Count)}; gravity={gravity.Count}; chaos_used={plan?.StartedCount ?? 0}/{plan?.PlannedCount ?? 0}; next_chaos_in={(plan?.NextAt is double at ? Math.Max(0, at - RoundElapsed).ToString("0") : "none")}";
    private double RoundElapsed => Math.Max(0, Time.realtimeSinceStartup - roundStartedAt);
    public static IEnumerable<Player> Participants => Player.List.Where(p => !p.IsHost && !p.IsNpc && !p.IsDummy && p.IsReady);
    private static IEnumerable<Player> Alive => Participants.Where(p => p.IsAlive);
    private static bool Indoor(Room room) => room.Zone is FacilityZone.LightContainment or FacilityZone.HeavyContainment or FacilityZone.Entrance;

    public void BeginRound(CommunityRoundMode? mode)
    {
        Reset();
        paravozRound = mode == CommunityRoundMode.Paravoz;
        if (paravozRound) return;
        roundStartedAt = Time.realtimeSinceStartup;
        plan = new ChaosRoundPlan(random, config.FirstChaosMinimumDelay, config.FirstChaosMaximumDelay, config.MinimumChaosGap, config.MaximumChaosGap);
        if (config.EventsEnabled) scheduler = Timing.RunCoroutine(Schedule(generation));
    }

    public void Reset()
    {
        generation++;
        Timing.KillCoroutines(scheduler);
        Stop();
        fallGrace.Clear();
        plan = null;
        paravozRound = false;
        Paused = false;
    }

    public void Pause(bool pause)
    {
        Paused = pause;
        if (pause) Stop();
    }

    private bool CanRun() => !Paused && Round.IsRoundInProgress && !Warhead.IsDetonationInProgress &&
        !Warhead.IsDetonated && Alive.Count() >= config.MinimumEventPlayers;

    internal bool SpawnRandomGrenade()
    {
        var players = Alive.Where(p => p.RoleBase is IFpcRole).ToArray();
        if (players.Length == 0) return false;
        var target = players[random.Next(players.Length)];
        var grenade = LabApi.Features.Wrappers.TimedGrenadeProjectile.SpawnActive(target.Position + Vector3.down * 0.7f, ItemType.GrenadeHE, owner: target);
        if (grenade == null) { Logger.Error("[METHADRENALINE] Random grenade could not be created."); return false; }
        Logger.Info("[METHADRENALINE] Random live grenade spawned; no player notifications.");
        return true;
    }

    private IEnumerator<float> Schedule(int epoch)
    {
        var current = plan;
        if (current == null) yield break;
        yield return Timing.WaitForSeconds(1);
        while (!disposed && epoch == generation && Round.IsRoundInProgress && !current.Completed)
        {
            if (CanRun() && current.IsDue(RoundElapsed)) TryStartChaos(false, out _);
            yield return Timing.WaitForSeconds(1);
        }
    }

    public bool TryStart(string name, bool diagnostic, out string result)
    {
        if (paravozRound && !diagnostic)
        {
            result = "Facility events are disabled in Paravoz rounds.";
            return false;
        }
        return name == "chaos" ? TryStartChaos(diagnostic, out result) : TryStartEffect(name, diagnostic, false, out result);
    }

    private bool TryStartChaos(bool diagnostic, out string result)
    {
        if (disposed || active.Count > 0) { result = "A facility event is already active or the director is disabled."; return false; }
        if (!diagnostic && (!CanRun() || plan == null || plan.Completed))
        {
            result = "Chaos requires an eligible active round and an unused round budget.";
            return false;
        }
        foreach (string effect in AnomalyPolicy.ChaosEffects)
        {
            if (TryStartEffect(effect, diagnostic, true, out string detail)) continue;
            Stop();
            result = "Chaos bundle failed and was rolled back: " + detail;
            return false;
        }
        if (!diagnostic) plan!.TryRecordStart(RoundElapsed, allowEarly: true);
        Logger.Info($"[METHADRENALINE] Chaos bundle started: four simultaneous effects; subtitles=None; {Status}");
        result = "Started chaos bundle; " + Status;
        return true;
    }

    private bool TryStartEffect(string name, bool diagnostic, bool fromChaos, out string result)
    {
        if (disposed) { result = "The director is disabled."; return false; }
        if (!AnomalyPolicy.Names.Contains(name)) { result = "Events: " + string.Join(", ", AnomalyPolicy.Names); return false; }
        if (!AnomalyPolicy.CanStart(name, active.Keys, AnomalyPolicy.ChaosEffects.Count))
        {
            result = "Event limit reached, duplicate event, or conflicting gravity event.";
            return false;
        }
        if (!diagnostic && (!Round.IsRoundInProgress || Paused)) { result = "Events require an active round and a resumed director."; return false; }
        if (!diagnostic && (Warhead.IsDetonationInProgress || Warhead.IsDetonated)) { result = "Events are suspended during warhead evacuation."; return false; }

        var rooms = Alive.Select(p => p.Room).Where(r => r != null && Indoor(r)).Cast<Room>().Distinct().ToArray();
        Room? anchor = rooms.Length > 0 ? rooms[random.Next(rooms.Length)] : diagnostic ? Room.List.FirstOrDefault(Indoor) : null;
        if (name == "gravity" && anchor == null) { result = "No occupied indoor area is available."; return false; }

        var run = new ActiveEvent(name, AnomalyPolicy.Duration(name, config.EventDuration), anchor?.Zone, fromChaos);
        active.Add(name, run);
        try
        {
            switch (name)
            {
                case "gravity":
                    UpdateGravity(run);
                    break;
                case "levitation":
                    UpdateGravity(run);
                    break;
                case "blackout":
                    Blackout(run, rooms);
                    break;
                case "doors":
                    PrepareDoors(run, rooms);
                    PulseDoors(run);
                    break;
                case "signals":
                    PulseSignals(run);
                    break;
            }
            Logger.Info($"[METHADRENALINE] Event started: {name}, zone={run.Zone?.ToString() ?? "global"}, duration={run.Duration}, concurrent={active.Count}, rollback={run.Scope.Count}.");
            run.Timer = Timing.RunCoroutine(Lifetime(run));
            result = $"Started {name}; {Status}";
            return true;
        }
        catch (Exception error)
        {
            End(run);
            Logger.Error($"[METHADRENALINE] Event {name} failed and was rolled back: {error}");
            result = "Event failed; its mutations were rolled back. Other events are unchanged.";
            return false;
        }
    }

    private bool IsActive(ActiveEvent run) => active.TryGetValue(run.Name, out var current) && ReferenceEquals(current, run);

    private IEnumerator<float> Lifetime(ActiveEvent run)
    {
        while (!disposed && IsActive(run) && Time.realtimeSinceStartup < run.Until)
        {
            try
            {
                if (AnomalyPolicy.IsGravity(run.Name)) UpdateGravity(run);
                else if (run.Name == "doors" && Time.realtimeSinceStartup >= run.NextPulse) PulseDoors(run);
                else if (run.Name == "signals" && Time.realtimeSinceStartup >= run.NextPulse) PulseSignals(run);
            }
            catch (Exception error)
            {
                Logger.Error($"[METHADRENALINE] Event update failed: {run.Name}: {error}");
                End(run, false);
                yield break;
            }
            yield return Timing.WaitForSeconds(0.2f);
        }
        if (IsActive(run)) End(run, false);
    }

    private static float DistanceFromOccupiedRooms(Vector3 position, Room[] rooms)
        => rooms.Length == 0 ? 0 : rooms.Min(room => Vector3.SqrMagnitude(position - room.Position));

    private void PulseSignals(ActiveEvent run)
    {
        run.NextPulse = Time.realtimeSinceStartup + config.SignalPulseInterval;
        if (Announcer.IsSpeaking) return;
        string cue = global::Cassie.CassieGlitchifier.Glitchify("attention attention", 1f, 0.25f);
        double? cueDuration = Announcer.LineDatabase == null ? null : Announcer.CalculateDuration(cue, default(global::Cassie.CassiePlaybackModifiers));
        if (!AnnouncementPolicy.IsSafe(cueDuration)) throw new InvalidOperationException("Signal cue duration is outside its safe limit.");
        Announcer.Message(SignalPayload(cue), -10f, 0f);
    }

    private static global::Cassie.CassieTtsPayload SignalPayload(string cue)
        => new(cue, autoGenerateSubtitles: false, playBackground: true);

    private void Blackout(ActiveEvent run, Room[] rooms)
    {
        var lights = LightsController.List.Where(l => Indoor(l.Room) && l.LightsEnabled)
            .OrderBy(l => DistanceFromOccupiedRooms(l.Room.Position, rooms)).Take(config.MaximumAffectedRooms).ToArray();
        if (lights.Length == 0) throw new InvalidOperationException("No lit rooms are available.");
        foreach (var light in lights)
        {
            run.Scope.Add(() =>
            {
                if (!light.Base) return;
                float remaining = (float)FlickerDuration.GetValue(light.Base);
                float expected = Math.Max(0, run.Until - Time.realtimeSinceStartup);
                if (Math.Abs(remaining - expected) < 1f) light.FlickerLights(0);
            });
            light.FlickerLights(run.Duration);
        }
    }

    private void PrepareDoors(ActiveEvent run, Room[] rooms)
    {
        var doors = Door.List.Where(CanAffectDoor)
            .OrderBy(d => DistanceFromOccupiedRooms(d.Transform.position, rooms)).Take(config.MaximumAffectedDoors).ToArray();
        if (doors.Length == 0) throw new InvalidOperationException("No ordinary unlocked corridor doors are available.");
        foreach (var door in doors)
        {
            var state = new DoorState(door.IsOpened);
            run.Doors.Add(door, state);
            run.Scope.Add(() =>
            {
                if (CanAffectDoor(door) && !state.Touched && state.Applied.HasValue && door.IsOpened == state.Applied.Value)
                    door.IsOpened = state.Original;
            });
        }
    }

    private void PulseDoors(ActiveEvent run)
    {
        foreach (var pair in run.Doors)
        {
            if (!CanAffectDoor(pair.Key)) continue;
            bool applied = !pair.Key.IsOpened;
            pair.Value.Applied = applied;
            pair.Key.IsOpened = applied;
        }
        run.NextPulse = Time.realtimeSinceStartup + config.DoorPulseInterval;
    }

    private static bool CanAffectDoor(Door door)
        => door.Base is Interactables.Interobjects.BreakableDoor && door.DoorName == DoorName.None &&
           !door.IsDestroyed && !door.IsLocked && door.Permissions == DoorPermissionFlags.None;

    public void DoorTouched(Door door)
    {
        if (active.TryGetValue("doors", out var run) && run.Doors.TryGetValue(door, out var state)) state.Touched = true;
    }

    private void UpdateGravity(ActiveEvent run)
    {
        bool levitation = run.Name == "levitation";
        var affected = new HashSet<Player>(Alive.Where(p => p.RoleBase is IFpcRole && (levitation || p.Zone == run.Zone)));
        foreach (var player in gravity.Keys.Where(p => !affected.Contains(p)).ToArray()) RestoreGravity(player, run);
        foreach (var player in affected)
        {
            if (gravity.TryGetValue(player, out var existing))
            {
                if (existing.Life != player.LifeId) RestoreGravity(player, run);
                else
                {
                    if (levitation && player.RoleBase is IFpcRole role && role.FpcModule.CharController.isGrounded) player.Jump(3);
                    continue;
                }
            }
            Vector3 original = player.Gravity;
            Vector3 applied = new(original.x, AnomalyPolicy.GravityY(run.Name, original.y, config.GravityMultiplier, config.LevitationAcceleration), original.z);
            var state = new GravityState(run, player.LifeId, original, applied);
            gravity.Add(player, state);
            run.Scope.Add(() => RestoreGravity(player, run));
            if (levitation) fallGrace.Remove(player);
            player.Gravity = applied;
            if (levitation) player.Jump(3);
        }
    }

    private void RestoreGravity(Player player, ActiveEvent? owner = null)
    {
        if (!gravity.TryGetValue(player, out var state) || owner != null && !ReferenceEquals(owner, state.Owner)) return;
        gravity.Remove(player);
        if (!player.GameObject) return;
        bool sameLife = player.LifeId == state.Life;
        if (sameLife && AnomalyPolicy.ProtectsFromFall(state.Owner.Name, state.Owner.FromChaos)) fallGrace[player] = new LandingProtection(state.Life);
        if ((FpcGravityController.GetGravity(player.ReferenceHub) - state.Applied).sqrMagnitude < 0.01f)
        {
            player.Gravity = state.Original;
            if (sameLife && player.IsAlive && state.Owner.Name == "levitation" && player.RoleBase is IFpcRole)
                player.Jump(0);
        }
    }

    public bool ProtectFromFall(Player player)
        => active.Values.Any(run => run.FromChaos) ||
           gravity.TryGetValue(player, out var state) && state.Life == player.LifeId && AnomalyPolicy.ProtectsFromFall(state.Owner.Name, state.Owner.FromChaos) ||
           fallGrace.TryGetValue(player, out var grace) && grace.Life == player.LifeId;

    private void UpdateLandingProtection()
    {
        foreach (var player in fallGrace.Keys.ToArray())
        {
            bool exists = player.GameObject && !player.IsDestroyed;
            bool grounded = exists && player.RoleBase is IFpcRole role && role.FpcModule.IsGrounded;
            if (!fallGrace[player].Update(exists ? player.LifeId : -1, exists && player.IsAlive, grounded, Time.realtimeSinceStartup))
                fallGrace.Remove(player);
        }
    }

    public void ForgetPlayer(Player? player)
    {
        if (player == null) return;
        RestoreGravity(player);
        fallGrace.Remove(player);
    }

    private void End(ActiveEvent run, bool killTimer = true)
    {
        if (!IsActive(run)) return;
        active.Remove(run.Name);
        if (killTimer) Timing.KillCoroutines(run.Timer);
        run.Scope.Dispose();
        Logger.Info($"[METHADRENALINE] Event ended: {run.Name}; rollback complete; remaining={active.Count}.");
        if (run.FromChaos && !active.Values.Any(other => other.FromChaos))
            Logger.Info("[METHADRENALINE] Chaos bundle ended; all effects restored.");
    }

    public void Stop()
    {
        foreach (var run in active.Values.ToArray()) End(run);
        foreach (var player in gravity.Keys.ToArray()) RestoreGravity(player);
    }

    public string Diagnostics()
    {
        if (Participants.Any()) return "Diagnostics require an empty server.";
        bool wasPaused = Paused;
        Pause(true);
        var results = new List<string>();
        bool Restored(Dictionary<LightsController, bool> lights, Dictionary<Door, bool> doors)
            => lights.All(pair => !pair.Key.Base || pair.Key.LightsEnabled == pair.Value) &&
               doors.All(pair => pair.Key.IsDestroyed || pair.Key.IsOpened == pair.Value) && active.Count == 0 && gravity.Count == 0;
        try
        {
            foreach (string name in AnomalyPolicy.Names)
            {
                var lights = LightsController.List.ToDictionary(l => l, l => l.LightsEnabled);
                var doors = Door.List.ToDictionary(d => d, d => d.IsOpened);
                bool started = TryStart(name, true, out string detail);
                int mutations = active.TryGetValue(name, out var run) ? run.Scope.Count : 0;
                Stop();
                bool passed = started && (mutations > 0 || name == "signals" || AnomalyPolicy.IsGravity(name)) && Restored(lights, doors);
                results.Add($"{name}: {(passed ? "PASS" : "FAIL")} ({mutations} mutations; {detail})");
            }
            var lightsBefore = LightsController.List.ToDictionary(l => l, l => l.LightsEnabled);
            var doorsBefore = Door.List.ToDictionary(d => d, d => d.IsOpened);
            bool overlapping = TryStartChaos(true, out _) && active.Count == AnomalyPolicy.ChaosEffects.Count;
            Stop();
            results.Add($"chaos bundle and cleanup: {(overlapping && Restored(lightsBefore, doorsBefore) ? "PASS" : "FAIL")}");
            results.Add($"signal subtitles: {(SignalPayload("attention").SubtitleSource == global::Cassie.CassieTtsPayload.SubtitleMode.None ? "PASS (None)" : "FAIL")}");
            results.Add(KeycardDoorDiagnostics());
            results.Add("gravity and levitation movement/ceiling collision require an in-game client");
            return string.Join("\n", results);
        }
        finally { Stop(); Pause(wasPaused); }
    }

    private string KeycardDoorDiagnostics()
    {
        var probe = Door.List.FirstOrDefault(CanAffectDoor);
        if (probe == null) return "keycard door exclusion: FAIL (no ordinary door available)";
        var originalPermissions = probe.Permissions;
        bool originalOpened = probe.IsOpened;
        bool passed = true;
        try
        {
            foreach (bool opened in new[] { false, true })
            {
                probe.Permissions = DoorPermissionFlags.ContainmentLevelOne;
                probe.IsOpened = opened;
                if (!TryStart("doors", true, out _)) return "keycard door exclusion: FAIL (event did not start)";
                var run = active["doors"];
                passed &= !run.Doors.ContainsKey(probe);
                PulseDoors(run);
                passed &= probe.IsOpened == opened;
                Stop();
                passed &= probe.IsOpened == opened;
            }

            probe.Permissions = DoorPermissionFlags.None;
            if (!TryStart("doors", true, out _)) return "keycard door exclusion: FAIL (event did not start)";
            var changing = active["doors"];
            passed &= changing.Doors.ContainsKey(probe);
            bool protectedState = probe.IsOpened;
            probe.Permissions = DoorPermissionFlags.ArmoryLevelOne;
            PulseDoors(changing);
            passed &= probe.IsOpened == protectedState;
            Stop();
            passed &= probe.IsOpened == protectedState;
            return $"keycard door exclusion: {(passed ? "PASS (closed, open, added during event; pulse and cleanup)" : "FAIL")}";
        }
        finally
        {
            Stop();
            probe.Permissions = originalPermissions;
            probe.IsOpened = originalOpened;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        global::StaticUnityMethods.OnLateUpdate -= UpdateLandingProtection;
        Reset();
    }

    private sealed class ActiveEvent
    {
        public ActiveEvent(string name, float duration, FacilityZone? zone, bool fromChaos)
        {
            Name = name;
            FromChaos = fromChaos;
            Duration = duration;
            Zone = name == "gravity" ? zone : null;
            Until = Time.realtimeSinceStartup + duration;
        }
        public string Name { get; }
        public bool FromChaos { get; }
        public float Duration { get; }
        public FacilityZone? Zone { get; }
        public float Until { get; }
        public float NextPulse { get; set; }
        public CoroutineHandle Timer { get; set; }
        public RestorationScope Scope { get; } = new(error => Logger.Error($"[METHADRENALINE] Event restoration failed: {error}"));
        public Dictionary<Door, DoorState> Doors { get; } = new();
    }

    private sealed class DoorState
    {
        public DoorState(bool original) => Original = original;
        public bool Original { get; }
        public bool? Applied { get; set; }
        public bool Touched { get; set; }
    }

    private sealed class GravityState
    {
        public GravityState(ActiveEvent owner, int life, Vector3 original, Vector3 applied)
        { Owner = owner; Life = life; Original = original; Applied = applied; }
        public ActiveEvent Owner { get; }
        public int Life { get; }
        public Vector3 Original { get; }
        public Vector3 Applied { get; }
    }
}
