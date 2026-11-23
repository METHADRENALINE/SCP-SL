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
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;

namespace Methadrenaline.Server;

internal sealed class FortuneCoin : IDisposable
{
    internal const int MisfortuneChance = 5;
    private const float FlipCooldown = 1f;
    private const float PocketAudioLength = 7;
    private const float GroundCoinScale = 2.6f;
    private static readonly string[] ClockPositions =
    {
        "", "один час", "два часа", "три часа", "четыре часа", "пять часов", "шесть часов",
        "семь часов", "восемь часов", "девять часов", "десять часов", "одиннадцать часов", "двенадцать часов"
    };

    private readonly System.Random random = new();
    private readonly HashSet<ushort> serials = new();
    private readonly Dictionary<Player, Session> sessions = new();
    private readonly Dictionary<Player, float> cooldowns = new();
    private readonly Dictionary<Player, PocketTeleport> promisedExits = new();
    private readonly FortuneCoinVoice voice = new();
    private FortuneCoinNavigation? navigation;
    private float nextBriefing;

    private sealed class Session(Player player, ushort serial)
    {
        internal readonly Player Player = player;
        internal readonly int Life = player.LifeId;
        internal readonly ushort Serial = serial;
        internal readonly CancellationTokenSource Cancellation = new();
        internal readonly FortuneCoinVoice.Listener Listener = new(player);
        internal CoroutineHandle Coroutine;
        internal bool PocketTrip;
        internal FortuneCoinVoice.MisfortuneEmitter? Emitter;
    }

    internal bool Selected { get; private set; }

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

    internal bool BeginRound(bool mapUnavailable)
    {
        Reset();
        if (!mapUnavailable && random.Next(2) != 0) return false;
        try
        {
            foreach (var position in SiteMapController.NavigationSpawnPositions(random, preferWalkways: true))
            {
                var pickup = Pickup.Create(ItemType.Coin, position + Vector3.up * .09f, Quaternion.Euler(0, random.Next(360), 0),
                    scale: Vector3.one * GroundCoinScale, networkSpawn: false);
                if (pickup == null) continue;
                serials.Add(pickup.Serial);
                pickup.Spawn();
            }
            if (serials.Count == 0) return false;
            navigation = new FortuneCoinNavigation();
            Selected = true;
            Logger.Info($"[METHADRENALINE] Fortune Coin selected; {serials.Count} pickups.");
            return true;
        }
        catch (Exception error)
        {
            Reset();
            Logger.Error("[METHADRENALINE] Fortune Coin setup failed: " + error);
            return false;
        }
    }

    internal const string Equipped = "Fortune Coin equipped.";

    internal string GiveTo(Player player)
    {
        if (!Selected) return "Fortune Coins are unavailable in this round.";
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
                !session.Player.IsDisarmed && PocketReady() && session.Listener.Prepare() &&
                FortuneCoinVoice.MisfortuneEmitter.Create(session.Player) is { } emitter)
            {
                session.PocketTrip = true;
                session.Emitter = emitter;
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
        if (!CanPlay(session) || player.CurrentItem?.Serial != session.Serial || player.Room is not { } room) yield break;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            var advice = navigation.NextStep(player, room, includeSituation: true);
            var request = voice.Speak(advice.Speech, session.Cancellation.Token);
            while (!request.IsCompleted && CanPlay(session)) yield return Timing.WaitForSeconds(.08f);
            if (!CanPlay(session) || request.Status != TaskStatus.RanToCompletion || request.Result is not { } samples ||
                !session.Listener.Prepare()) yield break;

            float delay = session.Listener.ReadyIn;
            if (delay > 0) yield return Timing.WaitForSeconds(delay);
            if (!CanPlay(session)) yield break;
            if (advice.Target is { } target && (target.IsDestroyed || target.IsInUse)) continue;
            yield return Timing.WaitForSeconds(session.Listener.Play(samples) + .18f);
            yield break;
        }
    }

    private IEnumerator<float> Misfortune(Session session, string clip)
    {
        var player = session.Player;
        var emitter = session.Emitter!;
        yield return Timing.WaitForSeconds(FortuneCoinVoice.Listener.Warmup);
        if (!CanPlay(session)) yield break;
        emitter.Follow();
        emitter.Play(clip);
        float end = Time.unscaledTime + PocketAudioLength;
        while (CanPlay(session) && Time.unscaledTime < end)
        {
            emitter.Follow();
            yield return Timing.WaitForSeconds(Mathf.Min(.12f, end - Time.unscaledTime));
        }
        if (!CanPlay(session) || AlphaWarheadController.Detonated ||
            !PocketReady() || PocketDimension.IsPlayerInside(player)) yield break;
        emitter.Dispose();
        session.Emitter = null;
        PocketDimension.ForceInside(player);
        yield return Timing.WaitForSeconds(.2f);
        if (!CanPlay(session) || !PocketDimension.IsPlayerInside(player)) yield break;

        var portals = PocketDimension.PocketTeleports.Where(portal => portal.GameObject).ToArray();
        Vector3 origin = player.Position;
        Vector3 forward = Quaternion.Euler(0, player.Camera.eulerAngles.y, 0) * Vector3.forward;
        PocketTeleport? firstDecoy = null;
        for (int line = 0; line < 3; line++)
        {
            if (!CanPlay(session) || !PocketDimension.IsPlayerInside(player)) yield break;
            var portal = line < 2
                ? portals.Where(candidate => candidate.GameObject && !candidate.IsExit && candidate != firstDecoy)
                    .OrderBy(_ => random.Next()).FirstOrDefault()
                : portals.FirstOrDefault(candidate => candidate.GameObject && candidate.IsExit);
            if (portal == null) yield break;
            if (line == 0) firstDecoy = portal;
            string bearing = ClockPositions[Clock(origin, forward, portal.Position)];
            var request = voice.Speak(PocketLine(line, bearing), session.Cancellation.Token);
            while (!request.IsCompleted && CanPlay(session) && PocketDimension.IsPlayerInside(player)) yield return Timing.WaitForSeconds(.08f);
            if (!CanPlay(session) || !PocketDimension.IsPlayerInside(player)) yield break;
            if (request.Status != TaskStatus.RanToCompletion || request.Result is not { } audio) continue;
            if (line == 2) promisedExits[player] = portal;
            yield return Timing.WaitForSeconds(session.Listener.Play(audio) + .2f);
        }
    }

    internal static string PocketLine(int line, string bearing) => line switch
    {
        0 => $"Ха-ха! Отсчитывай от направления, куда смотрел при появлении. Ладно, тебе на {bearing}.",
        1 => $"Стой, стой! Я пошутила. Тебе на {bearing}.",
        _ => $"Теперь серьёзно. Выход на {bearing}."
    };

    internal static int Clock(Vector3 origin, Vector3 forward, Vector3 target)
    {
        float angle = Vector3.SignedAngle(Vector3.ProjectOnPlane(forward, Vector3.up),
            Vector3.ProjectOnPlane(target - origin, Vector3.up), Vector3.up);
        int hour = (Mathf.RoundToInt(angle / 30) + 12) % 12;
        return hour == 0 ? 12 : hour;
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
        if (!AlphaWarheadController.Detonated && promisedExits.TryGetValue(ev.Player, out var portal) && portal == ev.Teleport)
            ev.IsSuccessful = true;
    }

    private void LeftPocket(PlayerLeftPocketDimensionEventArgs ev)
    {
        if (promisedExits.Remove(ev.Player) || sessions.TryGetValue(ev.Player, out var session) && session.PocketTrip)
            StopSession(ev.Player);
    }

    private bool CanPlay(Session session) => sessions.TryGetValue(session.Player, out var current) && current == session &&
        !session.Player.IsDestroyed && session.Player.IsAlive && session.Player.LifeId == session.Life;

    private void Release(Session session)
    {
        sessions.Remove(session.Player);
        session.Cancellation.Cancel();
        session.Listener.Dispose();
        session.Emitter?.Dispose();
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
        promisedExits.Remove(player);
        cooldowns.Remove(player);
    }

    private void ChangedRole(PlayerChangedRoleEventArgs ev) => Forget(ev.Player);
    private void Left(PlayerLeftEventArgs ev) => Forget(ev.Player);

    internal void Reset()
    {
        foreach (var player in sessions.Keys.Concat(cooldowns.Keys).Concat(promisedExits.Keys).Distinct().ToArray()) Forget(player);
        if (serials.Count > 0)
        {
            foreach (var player in Player.ReadyList)
                foreach (var item in player.Items.Where(item => IsCoin(item.Type, item.Serial)).ToArray()) player.RemoveItem(item);
            foreach (var item in Pickup.List.Where(item => IsCoin(item.Type, item.Serial)).ToArray()) item.Destroy();
        }
        serials.Clear();
        navigation = null;
        nextBriefing = 0;
        Selected = false;
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
