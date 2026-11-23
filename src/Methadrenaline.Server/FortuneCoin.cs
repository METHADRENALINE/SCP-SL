using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Arguments.Scp914Events;
using LabApi.Events.Handlers;
using LabApi.Features.Wrappers;
using MEC;
using Methadrenaline.Server.Core;
using Mirror;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;

namespace Methadrenaline.Server;

internal sealed class FortuneCoin : IDisposable
{
    internal const int MisfortuneChance = 5;
    private const float FlipCooldown = 1f;
    private const float PocketAudioLength = 7;
    private const float GroundCoinScale = 2.6f;
    private readonly System.Random random = new();
    private readonly HashSet<ushort> serials = new();
    private readonly Dictionary<Player, Session> sessions = new();
    private readonly Dictionary<Player, float> cooldowns = new();
    private readonly Dictionary<Player, Pickup> targets = new();
    private readonly FortuneCoinVoice voice = new();
    private FortuneCoinNavigation? navigation;
    private float nextBriefing;

    private sealed class Session(Player player, ushort serial)
    {
        internal readonly Player Player = player;
        internal readonly int Life = player.LifeId;
        internal readonly ushort Serial = serial;
        internal readonly CancellationTokenSource Cancellation = new();
        internal readonly FortuneCoinVoice.SpeechPlayer Speech = new(player);
        internal LightSourceToy? ExitLight;
        internal PrimitiveObjectToy? ExitMarker;
        internal PocketTeleport? PromisedExit;
        internal CoroutineHandle Coroutine;
        internal bool PocketTrip;
    }

    internal void Enable(Config config)
    {
        voice.Enable(config);
        PlayerEvents.FlippedCoin += Flipped;
        PlayerEvents.ChangedRole += ChangedRole;
        PlayerEvents.Left += Left;
        PlayerEvents.Hurt += Hurt;
        PlayerEvents.LeavingPocketDimension += LeavingPocket;
        PlayerEvents.LeftPocketDimension += LeftPocket;
        Scp914Events.ProcessingInventoryItem += ProcessingItem;
        Scp914Events.ProcessingPickup += ProcessingPickup;
    }

    internal void BeginRound(IReadOnlyList<Vector3> positions)
    {
        Reset();
        try
        {
            foreach (var position in positions)
            {
                var pickup = Pickup.Create(ItemType.Coin, position + Vector3.up * .09f, Quaternion.Euler(0, random.Next(360), 0),
                    scale: Vector3.one * GroundCoinScale, networkSpawn: false);
                if (pickup == null) continue;
                serials.Add(pickup.Serial);
                pickup.Spawn();
            }
            navigation = new FortuneCoinNavigation();
            Logger.Info($"[METHADRENALINE] Spawned {serials.Count} Fortune Coins.");
        }
        catch (Exception error)
        {
            Reset();
            Logger.Error("[METHADRENALINE] Fortune Coin setup failed: " + error);
        }
    }

    internal const string Equipped = "Fortune Coin equipped.";

    internal string GiveTo(Player player)
    {
        if (navigation == null) return "Fortune Coins are unavailable in this round.";
        if (!player.IsAlive || !player.IsHuman) return "A living human player is required.";
        var item = player.Items.FirstOrDefault(item => IsCoin(item.Type, item.Serial));
        if (item == null)
        {
            item = player.AddItem(ItemType.Coin);
            if (item == null) return "The player cannot carry another item.";
            serials.Add(item.Serial);
        }
        player.CurrentItem = item;
        return Equipped;
    }

    private bool IsCoin(ItemType type, ushort serial) => type == ItemType.Coin && serials.Contains(serial);

    private void ProcessingItem(Scp914ProcessingInventoryItemEventArgs ev)
    {
        if (IsCoin(ev.Item.Type, ev.Item.Serial)) ev.IsAllowed = false;
    }

    private void ProcessingPickup(Scp914ProcessingPickupEventArgs ev)
    {
        if (IsCoin(ev.Pickup.Type, ev.Pickup.Serial)) ev.IsAllowed = false;
    }

    private void Hurt(PlayerHurtEventArgs ev)
    {
        if (navigation != null && ev.Attacker != null) navigation.Hurt(ev.Player, ev.Attacker);
    }

    private void Flipped(PlayerFlippedCoinEventArgs ev)
    {
        var player = ev.Player;
        if (!IsCoin(ItemType.Coin, ev.CoinItem.Serial) || !Round.IsRoundInProgress || !player.IsAlive || !player.IsHuman ||
            player.IsHost || player.IsNpc || player.IsDummy || PocketDimension.IsPlayerInside(player)) return;
        if (sessions.ContainsKey(player)) return;
        float now = Time.unscaledTime;
        if (cooldowns.TryGetValue(player, out float next) && next > now) return;
        cooldowns[player] = now + FlipCooldown;
        StartSession(player, ev.CoinItem.Serial, random.Next(100) < MisfortuneChance);
    }

    private void StartSession(Player player, ushort serial, bool unlucky)
    {
        var session = new Session(player, serial);
        sessions.Add(player, session);
        session.Coroutine = Timing.RunCoroutine(Run(session, unlucky), Segment.Update);
    }

    private IEnumerator<float> Run(Session session, bool unlucky)
    {
        try
        {
            if (unlucky && voice.MisfortuneClip is { } clip && voice.Available && !AlphaWarheadController.Detonated &&
                !session.Player.IsDisarmed && PocketReady() && session.Speech.Prepare())
            {
                session.PocketTrip = true;
                using var misfortune = Misfortune(session, clip);
                while (misfortune.MoveNext()) yield return misfortune.Current;
            }
            else
            {
                using var guide = Briefing(session);
                while (guide.MoveNext()) yield return guide.Current;
            }
        }
        finally
        {
            if (sessions.TryGetValue(session.Player, out var current) && current == session)
                Release(session);
        }
    }

    private IEnumerator<float> Briefing(Session session)
    {
        if (navigation == null) yield break;
        var player = session.Player;
        float now = Time.unscaledTime;
        float slot = Mathf.Max(now, nextBriefing);
        nextBriefing = slot + .04f;
        if (slot > now) yield return Timing.WaitForSeconds(slot - now);
        if (!CanPlay(session) || player.CurrentItem?.Serial != session.Serial) yield break;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (player.Room is not { } room) yield break;
            targets.TryGetValue(player, out var preferred);
            var advice = navigation.NextStep(player, room, includeSituation: true, preferred);
            var request = voice.Speak(advice.Speech, session.Cancellation.Token);
            while (!request.IsCompleted && CanPlay(session)) yield return Timing.WaitForSeconds(.08f);
            if (!CanPlay(session) || request.Status != TaskStatus.RanToCompletion || request.Result is not { } samples ||
                !session.Speech.Prepare()) yield break;

            float delay = session.Speech.ReadyIn;
            if (delay > 0) yield return Timing.WaitForSeconds(delay);
            if (!CanPlay(session)) yield break;
            if (advice.Target is { } target && (target.IsDestroyed || target.IsInUse)) continue;
            if (advice.Target != null) targets[player] = advice.Target;
            else targets.Remove(player);
            yield return Timing.WaitForSeconds(session.Speech.Play(samples) + .18f);
            yield break;
        }
    }

    private IEnumerator<float> Misfortune(Session session, string clip)
    {
        var player = session.Player;
        yield return Timing.WaitForSeconds(FortuneCoinVoice.SpeechPlayer.Warmup);
        if (!CanPlay(session)) yield break;
        session.Speech.PlayCue(clip);
        yield return Timing.WaitForSeconds(PocketAudioLength);
        if (!CanPlay(session) || AlphaWarheadController.Detonated ||
            !PocketReady() || PocketDimension.IsPlayerInside(player)) yield break;
        PocketDimension.ForceInside(player);
        yield return Timing.WaitForSeconds(.2f);
        if (!CanPlay(session) || !PocketDimension.IsPlayerInside(player)) yield break;

        var portals = PocketDimension.PocketTeleports.Where(p => p.GameObject && p.Collider).ToArray();
        if (portals.Length == 0) yield break;
        var center = Vector3.zero;
        foreach (var p in portals) center += p.Collider.bounds.center;
        center /= portals.Length;
        center.y = player.Position.y;
        var shown = new HashSet<PocketTeleport>();
        PocketTeleport? previous = null;
        int changes = random.Next(3, 5);
        for (int step = 0; step <= changes; step++)
        {
            if (!InPocket(session)) yield break;
            bool final = step == changes;
            var request = voice.Speak(FortunePocketAdvice.Line(step, final), session.Cancellation.Token);
            while (!request.IsCompleted && InPocket(session)) yield return Timing.WaitForSeconds(.08f);
            if (!InPocket(session)) yield break;

            var portal = FortunePocketAdvice.Pick(PocketDimension.PocketTeleports.Where(p => p.GameObject),
                p => p.IsExit, shown, previous, final, random);
            if (portal == null) yield break;
            PointAt(session, center, portal);
            if (final) session.PromisedExit = portal;
            previous = portal;
            shown.Add(portal);
            float duration = request.Status == TaskStatus.RanToCompletion && request.Result is { } samples
                ? session.Speech.Play(samples) : 1.2f;
            yield return Timing.WaitForSeconds(duration + .25f);
        }

        session.Speech.Dispose();
        while (InPocket(session)) yield return Timing.WaitForSeconds(.5f);
    }

    private bool InPocket(Session session) => CanPlay(session) && PocketDimension.IsPlayerInside(session.Player);

    private static void PointAt(Session session, Vector3 origin, PocketTeleport portal)
    {
        Vector3 offset = Vector3.ProjectOnPlane(portal.Collider.bounds.center - origin, Vector3.up);
        Vector3 position = origin + Vector3.ClampMagnitude(offset * .65f, 6f) + Vector3.up * .35f;
        if (session.ExitMarker == null)
        {
            var marker = PrimitiveObjectToy.Create(position, networkSpawn: false);
            session.ExitMarker = marker;
            marker.Type = PrimitiveType.Sphere;
            marker.Flags = AdminToys.PrimitiveFlags.Visible;
            marker.Color = new Color(1f, .82f, .18f);
            marker.Scale = Vector3.one * .8f;
            SpawnMarker(marker, session.Player);

            var light = LightSourceToy.Create(position, networkSpawn: false);
            session.ExitLight = light;
            light.Type = LightType.Point;
            light.ShadowType = LightShadows.None;
            light.Color = new Color(1f, .78f, .24f);
            light.Range = 5f;
            light.Intensity = 5f;
            SpawnMarker(light, session.Player);
        }

        MoveMarker(session.ExitMarker, position);
        MoveMarker(session.ExitLight!, position - offset.normalized * .85f + Vector3.up * .35f);
    }

    private static void SpawnMarker(AdminToy toy, Player player)
    {
        toy.MovementSmoothing = 0;
        toy.IsStatic = false;
        toy.Base.NetworkPosition = toy.Transform.localPosition;
        toy.Base.NetworkScale = toy.Transform.localScale;
        toy.Base.NetworkRotation = toy.Transform.localRotation;
        toy.Base.netIdentity.visible = Visibility.ForceHidden;
        var connection = player.ReferenceHub.connectionToClient;
        NetworkServer.Spawn(toy.GameObject, connection);
        if (!toy.Base.netIdentity.observers.ContainsKey(connection.connectionId))
            AddMarkerObserver(toy.Base.netIdentity, connection);
    }

    private static readonly Action<NetworkIdentity, NetworkConnectionToClient> AddMarkerObserver =
        HarmonyLib.AccessTools.MethodDelegate<Action<NetworkIdentity, NetworkConnectionToClient>>(
            HarmonyLib.AccessTools.Method(typeof(NetworkIdentity), "AddObserver"));

    private static void MoveMarker(AdminToy toy, Vector3 position)
    {
        toy.Position = position;
        toy.Base.NetworkPosition = position;
    }

    private static bool PocketReady()
    {
        if (PocketDimension.Instance == null || PocketDimensionTeleport.DebugBool) return false;
        int wrong = 0, right = 0;
        foreach (var portal in PocketDimension.PocketTeleports)
        {
            if (!portal.GameObject) continue;
            if (portal.IsExit) right++;
            else wrong++;
        }
        return wrong >= 2 && right > 0;
    }

    private void LeavingPocket(PlayerLeavingPocketDimensionEventArgs ev)
    {
        if (!AlphaWarheadController.Detonated && sessions.TryGetValue(ev.Player, out var session) && session.PromisedExit is { } exit && exit == ev.Teleport)
            ev.IsSuccessful = true;
    }

    private void LeftPocket(PlayerLeftPocketDimensionEventArgs ev)
    {
        if (sessions.TryGetValue(ev.Player, out var session) && session.PocketTrip)
            StopSession(ev.Player);
    }

    private bool CanPlay(Session session) => sessions.TryGetValue(session.Player, out var current) && current == session &&
        !session.Player.IsDestroyed && session.Player.IsAlive && session.Player.LifeId == session.Life;

    private void Release(Session session)
    {
        sessions.Remove(session.Player);
        session.Cancellation.Cancel();
        session.Speech.Dispose();
        if (session.ExitMarker is { IsDestroyed: false } marker) marker.Destroy();
        if (session.ExitLight is { IsDestroyed: false } light) light.Destroy();
        session.Cancellation.Dispose();
    }

    private void StopSession(Player player)
    {
        if (!sessions.TryGetValue(player, out var session)) return;
        Timing.KillCoroutines(session.Coroutine);
        if (sessions.ContainsKey(player)) Release(session);
    }

    private void Forget(Player player)
    {
        StopSession(player);
        cooldowns.Remove(player);
        targets.Remove(player);
    }

    private void ChangedRole(PlayerChangedRoleEventArgs ev) => Forget(ev.Player);
    private void Left(PlayerLeftEventArgs ev) => Forget(ev.Player);

    internal void Reset()
    {
        foreach (var player in sessions.Keys.Concat(cooldowns.Keys).Distinct().ToArray()) Forget(player);
        if (serials.Count > 0)
        {
            foreach (var player in Player.ReadyList)
                foreach (var item in player.Items.Where(item => IsCoin(item.Type, item.Serial)).ToArray()) player.RemoveItem(item);
            foreach (var item in Pickup.List.Where(item => IsCoin(item.Type, item.Serial)).ToArray()) item.Destroy();
        }
        serials.Clear();
        navigation = null;
        nextBriefing = 0;
    }

    public void Dispose()
    {
        PlayerEvents.FlippedCoin -= Flipped;
        PlayerEvents.ChangedRole -= ChangedRole;
        PlayerEvents.Left -= Left;
        PlayerEvents.Hurt -= Hurt;
        PlayerEvents.LeavingPocketDimension -= LeavingPocket;
        PlayerEvents.LeftPocketDimension -= LeftPocket;
        Scp914Events.ProcessingInventoryItem -= ProcessingItem;
        Scp914Events.ProcessingPickup -= ProcessingPickup;
        Reset();
        voice.Dispose();
    }
}
