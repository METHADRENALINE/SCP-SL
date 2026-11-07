using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CustomPlayerEffects;
using Footprinting;
using InventorySystem.Items.Pickups;
using Interactables.Interobjects;
using Interactables.Interobjects.DoorUtils;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Arguments.ServerEvents;
using LabApi.Events.Handlers;
using LabApi.Features.Enums;
using LabApi.Features.Wrappers;
using LightContainmentZoneDecontamination;
using MapGeneration;
using MEC;
using Methadrenaline.Server.Core;
using Mirror;
using NetworkManagerUtils.Dummies;
using PlayerRoles;
using PlayerRoles.FirstPersonControl;
using PlayerRoles.FirstPersonControl.Spawnpoints;
using PlayerStatsSystem;
using RueI.API;
using RueI.API.Elements;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;

namespace Methadrenaline.Server;

internal sealed class ParavozController : IDisposable
{
    private static readonly Tag CountdownTag = new("methadrenaline.paravoz.countdown");
    private readonly Dictionary<Player, string> countdownViewers = new();
    private readonly ParavozPolicy policy = new();
    private readonly System.Random random = new();
    private readonly HashSet<uint> initializing = new();
    private readonly HashSet<uint> pendingInfections = new();
    private RestorationScope? isolation;
    private RestorationScope? decontamination;
    private Door? isolationDoor;
    private CoroutineHandle timer;
    private float started;
    private bool wasLocked;
    private bool initialized;
    private bool ending;
    private bool gathered;
    private int epoch;
    private uint initialCarrier;
    private Player[]? diagnosticPlayers;
    private float diagnosticOffset;
    private ReferenceHub? soloDummy;
    private uint soloPlayer;
    internal bool SoloActive => soloDummy != null;
    internal bool Active { get; private set; }
    internal ParavozWinner Result { get; private set; }
    internal ParavozPolicy Policy => policy;
    internal ParavozVoice Voice { get; }
    internal ParavozAbilities Abilities { get; } = new();
    internal ParavozBoundaries Boundaries { get; } = new();
    internal ParavozPresentation Presentation { get; } = new();
    internal ParavozController() => Voice = new ParavozVoice(this);
    internal Door? IsolationDoor => isolationDoor;
    internal Player? WaitingCarrier => Active && isolation != null && Player.Get(initialCarrier) is { IsAlive: true } player ? player : null;
    internal string WaitingHud(Player viewer)
    {
        int remaining = ParavozPolicy.WaitingSeconds(Elapsed);
        if (viewer != WaitingCarrier || remaining == 0) return "";
        string time = TimeSpan.FromSeconds(remaining).ToString(@"mm\:ss", CultureInfo.InvariantCulture);
        return "<color=#FF8800>" + string.Format(CultureInfo.InvariantCulture,
            Localizer.Text(viewer.ReferenceHub, ParavozPolicy.WaitingMessage), time) + "</color>";
    }
    internal string GatheringHud(Player viewer)
    {
        int seconds = ParavozPolicy.GatherCountdown(Elapsed);
        if (!Active || gathered || !viewer.IsAlive || !policy.Is(viewer.NetworkId, ParavozMember.Runner) || seconds == 0) return "";
        return CountdownText(viewer, ParavozPolicy.GatherMessage, seconds);
    }

    private static string CountdownText(Player viewer, string template, int seconds)
        => string.Format(CultureInfo.InvariantCulture, Localizer.Text(viewer.ReferenceHub, template), seconds);

    private void UpdateCountdownHud()
    {
        foreach (var viewer in countdownViewers.Keys.ToArray())
            if (!Active || viewer.IsDestroyed || !viewer.IsAlive) RemoveCountdownHud(viewer);
        if (!Active) return;

        foreach (var viewer in Participants.Where(p => p.IsAlive))
        {
            string line = Boundaries.Warning(viewer) is { } warning
                ? CountdownText(viewer, warning.Message, warning.Seconds)
                : GatheringHud(viewer);
            if (line.Length == 0) { RemoveCountdownHud(viewer); continue; }
            if (countdownViewers.TryGetValue(viewer, out string old) && old == line) continue;
            countdownViewers[viewer] = line;
            RueDisplay.Get(viewer).Show(CountdownTag, new BasicElement(58,
                "<align=center><size=34><color=#FF8800><nobr>" + line + "</nobr></color></size></align>"));
        }
    }

    private void RemoveCountdownHud(Player viewer)
    {
        if (countdownViewers.Remove(viewer) && !viewer.IsDestroyed)
            RueDisplay.Get(viewer).Remove(CountdownTag);
    }
    internal static Door[] ClassDCellDoors() => Door.List.Where(d => !d.IsDestroyed &&
        d.Rooms.Length == 1 && d.Rooms[0].Name == RoomName.LczClassDSpawn).ToArray();
    private static Door ClosestCell(Door[] cells, Vector3 position)
        => cells.OrderBy(d => (d.Position - position).sqrMagnitude).First();
    private IEnumerable<Player> Participants => diagnosticPlayers ?? EventDirector.Participants;
    internal float Elapsed => Time.realtimeSinceStartup - started + diagnosticOffset;
    internal void AdvanceDiagnostic(float seconds)
    {
        if (diagnosticPlayers == null || seconds < 0) throw new InvalidOperationException("An active native diagnostic is required.");
        diagnosticOffset += seconds;
    }
    internal string Status => $"paravoz={Active}; solo={SoloActive}; result={Result}; runners={policy.Members.Values.Count(x => x == ParavozMember.Runner)}; carriers={policy.Members.Values.Count(x => x == ParavozMember.Paravoz)}; hunters={policy.Members.Values.Count(x => x == ParavozMember.Hunter)}; isolation={isolation != null}; elapsed={(Active ? Time.realtimeSinceStartup - started : 0):0}";
    internal bool IsParavoz(Player? player) => Active && player != null && policy.Is(player.NetworkId, ParavozMember.Paravoz);
    internal bool IsHunter(Player? player) => Active && player != null && policy.Is(player.NetworkId, ParavozMember.Hunter);
    internal bool BlocksHealing(ReferenceHub hub) => Active && policy.Is(hub.netId, ParavozMember.Paravoz) && !initializing.Contains(hub.netId);

    internal void Enable()
    {
        Presentation.Enable();
        ItemPickupBase.OnPickupAdded += PickupAdded;
        PlayerEvents.Escaping += Escaping;
        PlayerEvents.Escaped += Escaped;
        PlayerEvents.Death += Death;
        PlayerEvents.Left += Left;
        PlayerEvents.ChangedRole += ChangedRole;
        PlayerEvents.UpdatingEffect += EffectUpdating;
        PlayerEvents.DroppingItem += DroppingItem;
        PlayerEvents.InteractingElevator += InteractingElevator;
        ServerEvents.WaveRespawning += WaveRespawning;
        ServerEvents.RoundEnding += RoundEnding;
        ServerEvents.RoundEndingConditionsCheck += RoundEndingConditionsCheck;
    }

    internal string BeginSolo(int playerId, bool asCarrier)
    {
        var players = EventDirector.Participants.ToArray();
        if (!Round.IsRoundInProgress || players.Length != 1)
            return "Solo Paravoz test requires exactly one connected player and an active round.";
        var player = players.Single();
        if (player.PlayerId != playerId) return "Player not found.";
        Reset();
        var hub = DummyUtils.SpawnDummy("ParavozTest");
        try
        {
            hub.authManager.UserId = "ID_Dummy";
            var dummy = Player.Get(hub)!;
            var plugin = ServerPlugin.Instance!;
            plugin.PerkRounds!.Reset();
            var mode = plugin.PerkRounds.Policy.BeginMode(() => CommunityRoundMode.Paravoz);
            plugin.Director.BeginRound(mode);
            BeginRound(new[] { player, dummy }, asCarrier ? player : dummy);
            soloDummy = hub;
            soloPlayer = player.NetworkId;
            LabApi.Features.Wrappers.Server.SendBroadcast(PerkRoundPolicy.ParavozMessage, 10, Broadcast.BroadcastFlags.Normal, true);
            return "Solo Paravoz test started.";
        }
        catch { Reset(); if (hub != null) NetworkServer.Destroy(hub.gameObject); throw; }
    }

    internal void BeginRound(Player[]? diagnostic = null, Player? firstCarrierOverride = null)
    {
        Reset();
        diagnosticPlayers = diagnostic;
        var players = Participants.ToArray();
        if (players.Length < 2) throw new InvalidOperationException("Paravoz requires at least two players.");
        var cells = ClassDCellDoors();
        if (cells.Length < 2) throw new InvalidOperationException("Class-D isolation cells are unavailable.");
        Player carrier = firstCarrierOverride ?? players[random.Next(players.Length)];
        if (!players.Contains(carrier)) throw new ArgumentException("Initial Paravoz must be a participant.");
        initialCarrier = carrier.NetworkId;
        policy.Begin(players.Select(p => p.NetworkId), carrier.NetworkId);
        Result = ParavozWinner.None;
        started = Time.realtimeSinceStartup;
        wasLocked = Round.IsLocked;
        Round.IsLocked = true;
        Active = true;
        ServerPlugin.Instance?.SiteMaps?.Reset();
        RemoveSurfacePasses();
        int run = ++epoch;
        try
        {
            SuspendDecontamination();
            foreach (var player in players) player.SetRole(RoleTypeId.ClassD, RoleChangeReason.RoundStart);
            Timing.CallDelayed(0.2f, () =>
            {
                if (!Active || epoch != run) return;
                try
                {
                    foreach (var player in players.Where(p => p.IsAlive)) player.IsGodModeEnabled = false;
                    Vector3 inside = carrier.Position;
                    var chamber = ClosestCell(cells, inside);
                    foreach (var other in players.Where(p => p != carrier && p.IsAlive && ClosestCell(cells, p.Position) == chamber))
                    {
                        bool relocated = false;
                        for (int attempt = 0; attempt < 32; attempt++)
                        {
                            if (!RoleSpawnpointManager.TryGetSpawnpointForRole(RoleTypeId.ClassD, out var spawn) ||
                                !spawn.TryGetSpawnpoint(out var alternative, out _) || ClosestCell(cells, alternative) == chamber) continue;
                            other.Position = alternative;
                            relocated = true;
                            break;
                        }
                        if (!relocated) throw new InvalidOperationException("Cannot clear the Paravoz isolation cell.");
                    }
                    ApplyCarrier(carrier, inside);
                    foreach (var runner in players.Where(p => p.IsAlive && policy.Is(p.NetworkId, ParavozMember.Runner)))
                        runner.AddItem(ItemType.SCP207);
                    bool opened = chamber.IsOpened;
                    bool alreadyLocked = (chamber.LockReason & DoorLockReason.AdminCommand) != 0;
                    isolation = new RestorationScope(error => Logger.Error("[METHADRENALINE] Isolation cleanup: " + error));
                    isolationDoor = chamber;
                    if (chamber.Base is Interactables.Interobjects.BreakableDoor breakable)
                    {
                        DoorDamageType original = breakable.IgnoredDamageSources;
                        breakable.IgnoredDamageSources = (DoorDamageType)byte.MaxValue;
                        isolation.Add(() => { if (breakable) breakable.IgnoredDamageSources = original; });
                    }
                    isolation.Add(() =>
                    {
                        if (chamber.IsDestroyed) return;
                        if (!alreadyLocked) chamber.Lock(DoorLockReason.AdminCommand, false);
                        if (!chamber.IsOpened) chamber.IsOpened = opened;
                    });
                    chamber.IsOpened = false;
                    chamber.Lock(DoorLockReason.AdminCommand, true);
                    initialized = true;
                    ServerPlugin.Instance?.Bracelets?.CheckNow();
                    foreach (var player in players) UltimateHUD.Hints.RefreshAll(player);
                }
                catch (Exception error) { Logger.Error("[METHADRENALINE] Paravoz setup failed: " + error); Reset(); }
            });
            timer = Timing.RunCoroutine(Run(run));
            Logger.Info("[METHADRENALINE] Paravoz round started: 60s in a random Class-D cell; 360s gathering; health from native SCP-106 template.");
        }
        catch { Reset(); throw; }
    }

    private IEnumerator<float> Run(int run)
    {
        while (Active && run == epoch && (Round.IsRoundInProgress || diagnosticPlayers != null))
        {
            float elapsed = Elapsed;
            if (SoloActive && elapsed >= 900) { Reset(); yield break; }
            if (elapsed >= ParavozPolicy.IsolationSeconds) ReleaseIsolation(releaseCarrier: true);
            if (!gathered && elapsed >= ParavozPolicy.GatherSeconds)
            {
                gathered = true;
                var carriers = Participants.Where(p => p.IsAlive && IsParavoz(p)).ToArray();
                foreach (var runner in Participants.Where(p => p.IsAlive && policy.Is(p.NetworkId, ParavozMember.Runner)))
                    if (carriers.Length > 0) runner.Position = carriers[random.Next(carriers.Length)].Position + Vector3.up * 0.1f;
            }
            if (initialized)
            {
                foreach (var player in Participants.Where(p => p.IsAlive && IsParavoz(p)))
                {
                    if (player != WaitingCarrier) Boundaries.Tick(player, Time.realtimeSinceStartup);
                    if (!player.IsAlive) continue;
                    Abilities.Tick(player);
                    BreachDoors(player);
                }
                Evaluate();
                UpdateCountdownHud();
            }
            yield return Timing.WaitForSeconds(0.2f);
        }
    }

    internal void BreachDoors(Player player)
    {
        if (!IsParavoz(player) || !player.IsAlive || player.NetworkId == initialCarrier && isolation != null) return;
        Vector3 direction = player.ReferenceHub.transform.forward;
        direction.y = 0;
        if (!Physics.SphereCast(player.Position, 0.35f, direction.normalized, out var hit, 3, FpcStateProcessor.Mask, QueryTriggerInteraction.Ignore)) return;
        var door = hit.collider.GetComponentInParent<DoorVariant>();
        if (door == null) return;
        YieldDoor(player, door);
    }

    internal void RemoveSurfacePasses()
    {
        if (!Active) return;
        foreach (var pickup in Pickup.List.Where(p => p.Type == ItemType.SurfaceAccessPass).ToArray()) pickup.Destroy();
        foreach (var player in Player.List)
            foreach (var item in player.Items.Where(i => i.Type == ItemType.SurfaceAccessPass).ToArray()) player.RemoveItem(item);
    }

    private void PickupAdded(ItemPickupBase pickup)
    {
        if (Active && pickup.Info.ItemId == ItemType.SurfaceAccessPass) pickup.DestroySelf();
    }

    internal void YieldDoor(Player player, DoorVariant door)
    {
        if (!IsParavoz(player) || player.NetworkId == initialCarrier && isolation != null) return;
        if (door is Interactables.Interobjects.ElevatorDoor) return;
        if (door is Interactables.Interobjects.BreakableDoor breakable && !breakable.IsDestroyed)
        {
            var ignored = breakable.IgnoredDamageSources;
            try
            {
                breakable.IgnoredDamageSources &= ~DoorDamageType.ServerCommand;
                breakable.ServerDamage(Math.Max(breakable.MaxHealth, breakable.RemainingHealth) + 1, DoorDamageType.ServerCommand, new Footprint(player.ReferenceHub));
            }
            finally { breakable.IgnoredDamageSources = ignored; }
            if (breakable.IsDestroyed) return;
        }
        if (!door.TargetState) door.NetworkTargetState = true;
    }

    internal void Hurt(PlayerHurtingEventArgs ev)
    {
        if (!Active || !initialized) return;
        Player? attacker = ev.DamageHandler is AttackerDamageHandler damage ? Player.Get(damage.Attacker.Hub) : null;
        if (IsParavoz(ev.Player) && !IsHunter(attacker) && !Boundaries.IsExposureDamage(ev.Player, ev.DamageHandler)) ev.IsAllowed = false;
        if (IsParavoz(attacker) && IsParavoz(ev.Player)) ev.IsAllowed = false;
        if (diagnosticPlayers != null) Logger.Info($"[METHADRENALINE] Damage probe victim={ev.Player.NetworkId}; attacker={attacker?.NetworkId}; hunter={IsHunter(attacker)}; carrier={IsParavoz(ev.Player)}; allowed={ev.IsAllowed}; damage={(ev.DamageHandler as StandardDamageHandler)?.Damage}; hp={ev.Player.Health}; god={ev.Player.IsGodModeEnabled}; protection={ev.Player.GetEffect<SpawnProtected>()?.Intensity}");
    }

    private void Death(PlayerDeathEventArgs ev)
    {
        Boundaries.Forget(ev.Player.NetworkId);
        RemoveCountdownHud(ev.Player);
        Voice.Forget(ev.Player.NetworkId);
        Abilities.Forget(ev.Player.NetworkId);
        Presentation.Forget(ev.Player.NetworkId);
        if (!Active || !initialized || ending || !policy.Members.ContainsKey(ev.Player.NetworkId)) return;
        var victim = ev.Player;
        bool fire = ev.DamageHandler is ExplosionDamageHandler blast && blast.ExplosionType == ExplosionType.Custom;
        if (fire && ev.Attacker != null && policy.Infect(victim.NetworkId, ev.Attacker.NetworkId))
        {
            uint id = victim.NetworkId;
            int run = epoch;
            pendingInfections.Add(id);
            Vector3 position = ev.OldPosition;
            Timing.CallDelayed(0.2f, () =>
            {
                if (!Active || run != epoch) return;
                pendingInfections.Remove(id);
                if (victim.IsDestroyed) { policy.Eliminate(id); Evaluate(); return; }
                victim.SetRole(RoleTypeId.ClassD, RoleChangeReason.Respawn, RoleSpawnFlags.None);
                ApplyCarrier(victim, position);
                UltimateHUD.Hints.RefreshAll(victim);
                Evaluate();
            });
        }
        else policy.Eliminate(victim.NetworkId);
        Evaluate();
    }

    private void ApplyCarrier(Player player, Vector3 position)
    {
        if (!PlayerRoleLoader.AllRoles.TryGetValue(RoleTypeId.Scp106, out var template) || template is not IHealthbarRole health)
            throw new InvalidOperationException("The native SCP-106 health template is unavailable.");
        initializing.Add(player.NetworkId);
        try
        {
            player.IsDisarmed = false;
            player.ClearInventory();
            player.Position = position;
            player.MaxHealth = player.Health = health.MaxHealth;
            player.IsGodModeEnabled = false;
            player.DisableEffect<SpawnProtected>();
            Abilities.Apply(player);
            Presentation.Apply(player);
        }
        finally { initializing.Remove(player.NetworkId); }
    }

    private void EffectUpdating(PlayerEffectUpdatingEventArgs ev)
    {
        if (IsParavoz(ev.Player) && Abilities.Enabled(ev.Player) && ev.Effect is MovementBoost && ev.Player.IsAlive && ev.Intensity == 0 && !initializing.Contains(ev.Player.NetworkId))
            ev.IsAllowed = false;
    }

    private void DroppingItem(PlayerDroppingItemEventArgs ev)
    {
        if (IsParavoz(ev.Player)) ev.IsAllowed = false;
    }

    private void InteractingElevator(PlayerInteractingElevatorEventArgs ev)
    {
        if (IsParavoz(ev.Player) && ParavozBoundaries.SurfaceLift(ev.Elevator.Base)) ev.IsAllowed = false;
    }

    private void Escaping(PlayerEscapingEventArgs ev)
    {
        if (!Active) return;
        if (IsParavoz(ev.Player)) { ev.IsAllowed = false; return; }
        if (policy.Is(ev.Player.NetworkId, ParavozMember.Runner)) ev.NewRole = RoleTypeId.NtfPrivate;
    }
    private void Escaped(PlayerEscapedEventArgs ev)
    {
        if (!Active || !policy.Escape(ev.Player.NetworkId)) return;
        var hunter = ev.Player;
        int life = hunter.LifeId;
        hunter.IsGodModeEnabled = false;
        hunter.DisableEffect<SpawnProtected>();
        int run = epoch;
        Timing.CallDelayed(0.2f, () =>
        {
            if (!Active || epoch != run || hunter.IsDestroyed || !hunter.IsAlive || hunter.LifeId != life || !IsHunter(hunter)) return;
            hunter.ClearInventory();
            hunter.IsGodModeEnabled = false;
            hunter.DisableEffect<SpawnProtected>();
            foreach (ItemType item in new[] { ItemType.GunE11SR, ItemType.ArmorCombat, ItemType.KeycardMTFOperative, ItemType.Radio, ItemType.Medkit, ItemType.Medkit, ItemType.Medkit, ItemType.Adrenaline }) hunter.AddItem(item);
            hunter.SetAmmo(ItemType.Ammo556x45, 240);
            UltimateHUD.Hints.RefreshAll(hunter);
            Evaluate();
        });
    }
    private void ChangedRole(PlayerChangedRoleEventArgs ev)
    {
        Boundaries.Forget(ev.Player.NetworkId);
        Voice.Forget(ev.Player.NetworkId);
        Abilities.Forget(ev.Player.NetworkId);
        Presentation.Forget(ev.Player.NetworkId);
        if (!Active || !initialized || !policy.Members.ContainsKey(ev.Player.NetworkId) || pendingInfections.Contains(ev.Player.NetworkId) || ev.ChangeReason is RoleChangeReason.Died or RoleChangeReason.Escaped) return;
        if (!ev.Player.IsAlive) { policy.Eliminate(ev.Player.NetworkId); Evaluate(); }
    }
    private void Left(PlayerLeftEventArgs ev)
    {
        Boundaries.Forget(ev.Player.NetworkId);
        RemoveCountdownHud(ev.Player);
        if (SoloActive && ev.Player.NetworkId == soloPlayer) { Reset(); return; }
        Voice.Forget(ev.Player.NetworkId);
        Abilities.Forget(ev.Player.NetworkId);
        Presentation.Forget(ev.Player.NetworkId);
        if (!Active) return;
        pendingInfections.Remove(ev.Player.NetworkId);
        policy.Eliminate(ev.Player.NetworkId);
        Evaluate();
    }
    private void WaveRespawning(WaveRespawningEventArgs ev) { if (Active) ev.IsAllowed = false; }
    private void RoundEndingConditionsCheck(RoundEndingConditionsCheckEventArgs ev)
    {
        if (!Active) return;
        ev.CanEnd = ending;
    }
    private void RoundEnding(RoundEndingEventArgs ev)
    {
        if (!Active) return;
        if (!ending) { ev.IsAllowed = false; return; }
        ev.LeadingTeam = Result == ParavozWinner.Humans ? RoundSummary.LeadingTeam.ChaosInsurgency : Result == ParavozWinner.Virus ? RoundSummary.LeadingTeam.Anomalies : RoundSummary.LeadingTeam.Draw;
    }
    private void Evaluate()
    {
        if (!Active || !initialized || ending || pendingInfections.Count > 0) return;
        if ((diagnosticPlayers == null || SoloActive) && wasLocked && Round.IsLocked) return;
        Result = policy.Winner;
        if (Result == ParavozWinner.None) return;
        ending = true;
        ReleaseIsolation();
        Round.IsLocked = wasLocked;
        Logger.Info("[METHADRENALINE] Paravoz round result=" + Result);
        if (diagnosticPlayers == null || SoloActive) Round.End(force: true);
    }
    private void ReleaseIsolation(bool releaseCarrier = false)
    {
        var door = isolationDoor;
        isolation?.Dispose();
        isolation = null;
        isolationDoor = null;
        if (releaseCarrier && door != null && !door.IsDestroyed && !door.IsLocked) door.IsOpened = true;
    }

    private void SuspendDecontamination()
    {
        var controller = DecontaminationController.Singleton;
        if (controller == null) return;
        var previous = controller.DecontaminationOverride;
        double startTime = controller.NetworkRoundStartTime;
        decontamination = new RestorationScope(error => Logger.Error("[METHADRENALINE] Decontamination cleanup: " + error));
        decontamination.Add(() =>
        {
            if (!controller) return;
            controller.NetworkRoundStartTime = startTime;
            controller.DecontaminationOverride = previous;
        });
        controller.DecontaminationOverride = DecontaminationController.DecontaminationStatus.Disabled;
        controller.NetworkRoundStartTime = 0;
    }

    internal void Reset()
    {
        bool active = Active;
        Active = false;
        decontamination?.Dispose();
        decontamination = null;
        UpdateCountdownHud();
        Voice.Reset();
        Abilities.Reset();
        Boundaries.Reset();
        Presentation.Reset();
        epoch++;
        Timing.KillCoroutines(timer);
        ReleaseIsolation();
        foreach (var player in Player.List.Where(p => policy.Is(p.NetworkId, ParavozMember.Paravoz)))
        {
            if (player.IsAlive && player.RoleBase is IHealthbarRole health) { player.MaxHealth = health.MaxHealth; player.Health = Math.Min(player.Health, player.MaxHealth); }
        }
        pendingInfections.Clear();
        initializing.Clear();
        initialized = ending = gathered = false;
        diagnosticPlayers = null;
        diagnosticOffset = 0;
        if (active) Round.IsLocked = wasLocked;
        policy.Clear();
        var dummy = soloDummy;
        soloDummy = null;
        soloPlayer = 0;
        if (dummy != null) NetworkServer.Destroy(dummy.gameObject);
    }
    public void Dispose()
    {
        Reset();
        Presentation.Dispose();
        ItemPickupBase.OnPickupAdded -= PickupAdded;
        PlayerEvents.Escaping -= Escaping;
        PlayerEvents.Escaped -= Escaped;
        PlayerEvents.Death -= Death;
        PlayerEvents.Left -= Left;
        PlayerEvents.ChangedRole -= ChangedRole;
        PlayerEvents.UpdatingEffect -= EffectUpdating;
        PlayerEvents.DroppingItem -= DroppingItem;
        PlayerEvents.InteractingElevator -= InteractingElevator;
        ServerEvents.WaveRespawning -= WaveRespawning;
        ServerEvents.RoundEnding -= RoundEnding;
        ServerEvents.RoundEndingConditionsCheck -= RoundEndingConditionsCheck;
    }
}
