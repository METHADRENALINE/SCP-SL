using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CustomPlayerEffects;
using Footprinting;
using InventorySystem;
using InventorySystem.Disarming;
using InventorySystem.Items.ThrowableProjectiles;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Arguments.ServerEvents;
using LabApi.Events.Handlers;
using LabApi.Features;
using LabApi.Features.Wrappers;
using Methadrenaline.Server.Core;
using PlayerRoles;
using RueI.API;
using RueI.API.Elements;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;
using ThrowableItem = InventorySystem.Items.ThrowableProjectiles.ThrowableItem;

namespace Methadrenaline.Server;

internal sealed class BraceletController : IDisposable
{
    internal const string CaptorSequenceWarning = "ELIMINATION PROCESS INITIATED. MOVE AWAY FROM THE PRISONER!";
    private static readonly Tag HudTag = new("methadrenaline.bracelets");
    private readonly Dictionary<BraceletLife, Vector3> lastPositions = new();
    private readonly Dictionary<BraceletLife, Footprint> sequenceOwners = new();
    private readonly HashSet<Player> hudPlayers = new();
    private readonly Queue<(Footprint Owner, Vector3 Position)> explosions = new();
    internal BraceletLedger Ledger { get; } = new();
    internal BraceletAudio Audio { get; } = new();
    internal bool Diagnostic { get; set; }
    internal BraceletPlaytest? Playtest { get; private set; }
    internal int ExplosionCount { get; private set; }
    internal Vector3 LastExplosionPosition { get; private set; }
    private BraceletLife? executing;
    private float nextCheck;
    private float diagnosticTimeOffset;
    private float Now => Time.unscaledTime + (Diagnostic ? diagnosticTimeOffset : 0);
    private bool disposed;
    internal string Status => $"bracelets={Ledger.Active.Count}; bracelet_sequences={Ledger.Active.Count(b => b.Committed)}; bracelet_notices={Ledger.Notices.Count}; bracelet_explosions={ExplosionCount}; bracelet_signals={Audio.SignalsPlayed}";

    internal static BraceletLife Life(Player player) => new(player.NetworkId, player.LifeId);
    internal static bool EligibleCaptor(Player player) => player.IsAlive;
    private static Player? Resolve(BraceletLife life)
    {
        Player? player = Player.Get(life.NetworkId);
        return player != null && !player.IsDestroyed && player.LifeId == life.LifeId ? player : null;
    }

    internal void Enable()
    {
        Audio.Enable();
        Playtest = new BraceletPlaytest(this);
        PlayerEvents.Cuffed += Cuffed;
        PlayerEvents.Uncuffed += Uncuffed;
        PlayerEvents.Death += Death;
        PlayerEvents.ChangedRole += ChangedRole;
        PlayerEvents.Left += Left;
        ServerEvents.RoundEnded += RoundEnded;
        ServerEvents.WaitingForPlayers += Reset;
        ServerEvents.RoundRestarted += Reset;
        StaticUnityMethods.OnFixedUpdate += Tick;
    }

    private void Cuffed(PlayerCuffedEventArgs ev)
    {
        if (disposed || (!Diagnostic && !Round.IsRoundInProgress)) return;
        Player captor = ev.Player, prisoner = ev.Target;
        if (captor == null || prisoner == null || !captor.IsAlive || !EligibleCaptor(captor) || !prisoner.IsHuman || !prisoner.IsAlive || !prisoner.IsDisarmed || prisoner.DisarmedBy != captor || captor.IsHost || prisoner.IsHost) return;
        if (!Diagnostic && Playtest?.AllowsPair(captor, prisoner) != true && (captor.IsNpc || captor.IsDummy || prisoner.IsNpc || prisoner.IsDummy)) return;
        // Duplicate cuff events must not replay activation or reset warning state.
        if (Ledger.TryGet(Life(prisoner), out var existing) && (existing.Committed || existing.Captor.Equals(Life(captor)))) return;
        ReleasePrisoner(prisoner.NetworkId);
        if (Ledger.Bind(Life(prisoner), Life(captor), prisoner.Nickname, Vector3.Distance(captor.Position, prisoner.Position)))
        {
            lastPositions[Life(prisoner)] = prisoner.Position;
            Audio.PlayConfirmation(prisoner);
        }
        UpdateHud();
    }

    private void Uncuffed(PlayerUncuffedEventArgs ev)
    {
        if (ev.Target != null) ReleasePrisoner(ev.Target.NetworkId);
        UpdateHud();
    }

    private void Death(PlayerDeathEventArgs ev)
    {
        // KillPlayer has already changed the victim to Spectator and removed
        // native cuffs. Match their saved binding, and use the event's old position.
        ReleaseCaptor(ev.Player.NetworkId);
        var binding = Ledger.Active.FirstOrDefault(b => b.Prisoner.NetworkId == ev.Player.NetworkId);
        if (binding != null) Complete(binding, ev.OldPosition, killPrisoner: false);
        UpdateHud();
    }

    private void ChangedRole(PlayerChangedRoleEventArgs ev)
    {
        ReleaseCaptor(ev.Player.NetworkId);
        // Native Death follows ChangedRole(Died). Do not discard its bracelet
        // before the confirmed death event has queued the corpse explosion.
        if (ev.ChangeReason != RoleChangeReason.Died) ReleasePrisoner(ev.Player.NetworkId);
        UpdateHud();
    }

    private void Left(PlayerLeftEventArgs ev)
    {
        ReleaseCaptor(ev.Player.NetworkId);
        var committed = Ledger.Active.FirstOrDefault(b => b.Prisoner.NetworkId == ev.Player.NetworkId && b.Committed);
        if (committed != null) Complete(committed, lastPositions.TryGetValue(committed.Prisoner, out var position) ? position : ev.Player.Position, killPrisoner: false);
        else ReleasePrisoner(ev.Player.NetworkId);
        if (hudPlayers.Remove(ev.Player)) RueDisplay.Get(ev.Player).Remove(HudTag);
        UpdateHud();
    }

    private void ReleasePrisoner(uint networkId)
    {
        foreach (var binding in Ledger.Active.Where(b => b.Prisoner.NetworkId == networkId).ToArray())
        {
            if (Ledger.Release(binding.Prisoner)) Forget(binding.Prisoner);
        }
    }

    private void ReleaseCaptor(uint networkId)
    {
        var captors = Ledger.Active.Select(b => b.Captor).Concat(Ledger.Notices.Select(n => n.Binding.Captor)).Where(c => c.NetworkId == networkId).Distinct().ToArray();
        foreach (var captor in captors)
        {
            foreach (var binding in Ledger.Active.Where(b => b.Captor.Equals(captor) && !b.Committed).ToArray()) Forget(binding.Prisoner);
            Ledger.ReleaseCaptor(captor);
        }
    }

    internal bool IsExecuting(Player player) => executing.HasValue && executing.Value.Equals(Life(player));
    internal bool IsCommitted(Player player) => Ledger.TryGet(Life(player), out var binding) && binding.Committed;

    private void Forget(BraceletLife prisoner)
    {
        lastPositions.Remove(prisoner);
        sequenceOwners.Remove(prisoner);
        Audio.Stop(prisoner);
    }

    // Native cuffs auto-expire after a 90m separation. Preserve only a still
    // valid bracelet pairing until our next check handles a long teleport.
    internal bool KeepCuffs(DisarmedPlayers.DisarmedEntry entry)
    {
        var binding = Ledger.Active.FirstOrDefault(b => b.Prisoner.NetworkId == entry.DisarmedPlayer && b.Captor.NetworkId == entry.Disarmer);
        if (binding == null) return false;
        Player? captor = Resolve(binding.Captor), prisoner = Resolve(binding.Prisoner);
        return captor != null && prisoner != null && captor.IsAlive && EligibleCaptor(captor) && prisoner.IsAlive && prisoner.IsHuman && captor.ReferenceHub.ValidateDisarmament(prisoner.ReferenceHub) && BraceletPolicy.MustDetonate(Vector3.Distance(captor.Position, prisoner.Position));
    }

    internal void Tick()
    {
        if (disposed || (Ledger.Active.Count == 0 && Ledger.Notices.Count == 0 && explosions.Count == 0 && hudPlayers.Count == 0 && ServerPlugin.Instance?.Paravoz?.WaitingCarrier == null)) return;
        float now = Now;
        if (now < nextCheck) return;
        nextCheck = now + 0.05f;
        try
        {
            foreach (var binding in Ledger.Active.ToArray())
            {
                if (!Ledger.TryGet(binding.Prisoner, out _)) continue;
                Player? captor = Resolve(binding.Captor), prisoner = Resolve(binding.Prisoner);
                if (binding.Committed)
                {
                    // Once filling starts, neither range, cuffs nor the owner's
                    // life can disarm the independent seven-second sequence.
                    if (prisoner == null || !prisoner.IsAlive)
                        Complete(binding, lastPositions.TryGetValue(binding.Prisoner, out var saved) ? saved : Vector3.zero, killPrisoner: false);
                    else
                    {
                        lastPositions[binding.Prisoner] = prisoner.Position;
                        if (binding.Phase(now) == BraceletPhase.Due) Complete(binding, prisoner.Position, killPrisoner: true);
                    }
                    continue;
                }
                if (captor == null || !captor.IsAlive || !EligibleCaptor(captor)) { ReleaseCaptor(binding.Captor.NetworkId); continue; }
                if (prisoner == null) { Ledger.Release(binding.Prisoner); Forget(binding.Prisoner); continue; }
                if (!prisoner.IsAlive)
                {
                    Complete(binding, lastPositions.TryGetValue(binding.Prisoner, out var position) ? position : prisoner.Position, killPrisoner: false);
                    continue;
                }
                if (!prisoner.IsDisarmed || prisoner.DisarmedBy != captor) { Ledger.Release(binding.Prisoner); Forget(binding.Prisoner); continue; }
                lastPositions[binding.Prisoner] = prisoner.Position;
                double distance = Vector3.Distance(captor.Position, prisoner.Position);
                if (distance < binding.PreviousDistance) Audio.CancelPendingWarning(binding.Prisoner);
                int warning = binding.ObserveDistance(distance, now);
                if (BraceletPolicy.MustDetonate(distance) && binding.BeginSequence(now))
                {
                    sequenceOwners[binding.Prisoner] = new Footprint(captor.ReferenceHub);
                    Audio.PlaySequence(prisoner);
                }
                else if (warning != 0) Audio.Play(prisoner, warning);
                Audio.Flush(binding.Prisoner, distance);
            }
            DrainExplosions();
            Ledger.Prune(now);
            UpdateHud();
        }
        catch (Exception error) { Logger.Error("[METHADRENALINE] Bracelet update failed: " + error); }
    }

    internal void CheckNow() { nextCheck = 0; Tick(); }
    internal void AdvanceDiagnosticTime(float seconds)
    {
        if (!Diagnostic || seconds < 0) throw new InvalidOperationException("A diagnostic clock requires an active diagnostic.");
        diagnosticTimeOffset += seconds;
        CheckNow();
    }

    private void Complete(BraceletBinding binding, Vector3 position, bool killPrisoner)
    {
        if (!Ledger.Complete(binding.Prisoner, Now, out _)) return;
        Player? captor = Resolve(binding.Captor), prisoner = Resolve(binding.Prisoner);
        Footprint owner = sequenceOwners.TryGetValue(binding.Prisoner, out var originalOwner) ? originalOwner : new Footprint(captor?.ReferenceHub);
        explosions.Enqueue((owner, position + Vector3.up * 0.5f));
        Forget(binding.Prisoner);
        if (prisoner != null && ServerPlugin.Instance?.Paravoz is { Active: true } mode && mode.IsParavoz(prisoner) && !mode.IsHunter(captor)) killPrisoner = false;
        if (!killPrisoner || prisoner == null || !prisoner.IsAlive) return;
        bool godMode = prisoner.IsGodModeEnabled;
        SpawnProtected protection = prisoner.GetEffect<SpawnProtected>()!;
        byte intensity = protection.Intensity;
        float duration = protection.Duration;
        executing = binding.Prisoner;
        try
        {
            // The wearer must die even with AHP, spawn protection or a saving
            // perk. Everyone around them still goes through native HE damage.
            prisoner.IsGodModeEnabled = false;
            protection.ServerSetState(0);
            prisoner.ReferenceHub.playerStats.DealDamage(BraceletDeathPresentation.WearerDeath());
            if (prisoner.IsAlive) throw new InvalidOperationException("The bracelet execution did not kill its wearer.");
        }
        finally
        {
            executing = null;
            prisoner.IsGodModeEnabled = godMode;
            if (prisoner.IsAlive && intensity > 0) protection.ServerSetState(intensity, duration);
        }
    }

    internal void DrainExplosions()
    {
        // Deaths caused by one blast enqueue their own bracelets. Drain them
        // sequentially, never recursively inside Unity's overlap enumeration.
        while (explosions.Count > 0)
        {
            var blast = explosions.Dequeue();
            try
            {
                if (!InventoryItemLoader.TryGetItem(ItemType.GrenadeHE, out ThrowableItem item) || item.Projectile is not ExplosionGrenade grenade)
                    throw new InvalidOperationException("The native HE grenade prefab is unavailable.");
                bool previous = BraceletDeathPresentation.Exploding;
                try
                {
                    BraceletDeathPresentation.Exploding = true;
                    ExplosionGrenade.Explode(blast.Owner, blast.Position, grenade, ExplosionType.Grenade);
                }
                finally { BraceletDeathPresentation.Exploding = previous; }
                TimedGrenadeProjectile.PlayEffect(blast.Position, ItemType.GrenadeHE);
                LastExplosionPosition = blast.Position;
                ExplosionCount++;
            }
            catch (Exception error) { Logger.Error("[METHADRENALINE] Bracelet explosion failed: " + error); }
        }
    }

    internal string Hud(Player viewer)
    {
        var rows = new List<string>();
        string waiting = ServerPlugin.Instance?.Paravoz?.WaitingHud(viewer) ?? "";
        if (waiting.Length > 0) rows.Add(waiting);
        string gathering = ServerPlugin.Instance?.Paravoz?.GatheringHud(viewer) ?? "";
        if (gathering.Length > 0) rows.Add(gathering);
        string Name(string name) => "<noparse>" + BraceletPolicy.SafeName(name) + "</noparse>";
        string Text(string template, params object[] values) => string.Format(CultureInfo.InvariantCulture, Localizer.Text(viewer.ReferenceHub, template), values);
        string CaptorStatus(string name, string status, string color)
        {
            string safeName = BraceletPolicy.SafeName(name);
            string visible = Text("Prisoner: {0} ({1})", safeName, status);
            string content = Text("Prisoner: {0} ({1})", Name(name), status);
            // RueI measures explicit line breaks, not client-side wrapping.
            // Keep the existing status on one line, fitting long translations
            // into its width rather than letting TMP add unmeasured lines.
            float size = Math.Min(25, 1750f / Math.Max(1, new StringInfo(visible).LengthInTextElements));
            return "<size=" + size.ToString("0.##", CultureInfo.InvariantCulture) + "><nobr><color=" + color + ">" + content + "</color></nobr></size>";
        }
        foreach (var binding in Ledger.Active)
        {
            Player? prisoner = Resolve(binding.Prisoner), captor = Resolve(binding.Captor);
            if (prisoner == null) continue;
            bool captiveView = binding.Prisoner.Equals(Life(viewer));
            if (!captiveView && !binding.Captor.Equals(Life(viewer))) continue;
            if (!captiveView && binding.Committed)
            {
                rows.Add(CaptorStatus(prisoner.Nickname, Text(CaptorSequenceWarning), "#FF3300"));
                continue;
            }
            if (captor != null)
            {
                float distance = Vector3.Distance(prisoner.Position, captor.Position);
                string line = Text(captiveView ? "Captor: {0} · {1} m" : "Prisoner: {0} · {1} m", Name(captiveView ? captor.Nickname : prisoner.Nickname), BraceletPolicy.FormatDistance(distance, Localizer.Current?.Language(viewer.ReferenceHub) ?? ServerLanguage.English));
                rows.Add("<color=" + BraceletPolicy.DistanceColor(distance) + ">" + line + "</color>");
            }
            if (captiveView && binding.Committed)
            {
                string message = binding.Phase(Now) == BraceletPhase.Filling ? "Сливание взрывчатых веществ в камеру" : "детонация";
                rows.Add("<color=#FF3300><b>" + Text(message) + "</b></color>");
            }
        }
        foreach (var notice in Ledger.Notices.Where(n => n.Binding.Captor.Equals(Life(viewer)) && Now < n.ExpiresAt))
            rows.Add(CaptorStatus(notice.Binding.PrisonerName, Text("eliminated"), "#FF8800"));
        return rows.Count == 0 ? "" : "<align=left><size=25>" + string.Join("\n", rows) + "</size></align>";
    }

    private void UpdateHud()
    {
        var viewers = new HashSet<Player>();
        if (ServerPlugin.Instance?.Paravoz?.WaitingCarrier is { } waiting) viewers.Add(waiting);
        if (ServerPlugin.Instance?.Paravoz is { } paravoz)
            foreach (var runner in paravoz.GatheringRunners) viewers.Add(runner);
        foreach (var life in Ledger.Active.SelectMany(b => new[] { b.Prisoner, b.Captor }).Concat(Ledger.Notices.Select(n => n.Binding.Captor)))
        {
            Player? player = Resolve(life);
            if (player != null) viewers.Add(player);
        }
        foreach (var player in hudPlayers.Where(p => !viewers.Contains(p)).ToArray())
        {
            hudPlayers.Remove(player);
            if (!player.IsDestroyed) RueDisplay.Get(player).Remove(HudTag);
        }
        foreach (var player in viewers)
        {
            var display = RueDisplay.Get(player);
            if (hudPlayers.Add(player))
                display.Show(HudTag, new DynamicElement(100, hub => hub == player.ReferenceHub ? Hud(player) : "") { UpdateInterval = TimeSpan.FromSeconds(0.1) });
        }
    }

    private void RoundEnded(RoundEndedEventArgs ev)
    {
        Ledger.ReleaseAll();
        foreach (var life in lastPositions.Keys.Where(p => !Ledger.TryGet(p, out _)).ToArray()) Forget(life);
        DrainExplosions();
        UpdateHud();
    }

    internal void Reset()
    {
        Ledger.Reset();
        lastPositions.Clear();
        sequenceOwners.Clear();
        Audio.Reset();
        explosions.Clear();
        nextCheck = 0;
        diagnosticTimeOffset = 0;
        BraceletDeathPresentation.Reset();
        UpdateHud();
    }

    public void Dispose()
    {
        if (disposed) return;
        Playtest?.Dispose();
        Playtest = null;
        disposed = true;
        PlayerEvents.Cuffed -= Cuffed;
        PlayerEvents.Uncuffed -= Uncuffed;
        PlayerEvents.Death -= Death;
        PlayerEvents.ChangedRole -= ChangedRole;
        PlayerEvents.Left -= Left;
        ServerEvents.RoundEnded -= RoundEnded;
        ServerEvents.WaitingForPlayers -= Reset;
        ServerEvents.RoundRestarted -= Reset;
        StaticUnityMethods.OnFixedUpdate -= Tick;
        Reset();
        Audio.Dispose();
    }
}
