using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Cassie;
using InventorySystem;
using InventorySystem.Items.Pickups;
using LabApi.Features.Wrappers;
using MEC;
using Logger = LabApi.Features.Console.Logger;

namespace Methadrenaline.Server;

internal sealed class BlackoutRound
{
    internal const float Delay = 30f;
    internal BlackoutAudio Audio { get; } = new();
    internal BlackoutSoundtrack Soundtrack { get; } = new();
    private CoroutineHandle openingTimer;
    private CoroutineHandle intrusionTimer;
    private BlackoutAnnouncement? announcement;
    private readonly Dictionary<RoomLightController, bool> restore = new();
    private CoroutineHandle timer;
    internal bool Active { get; private set; }
    internal bool Dark { get; private set; }
    internal bool SuppressAnnouncements { get; private set; }

    internal void Begin()
    {
        if (Active) return;
        Active = true;
        SuppressAnnouncements = true;
        ServerPlugin.Instance?.WarheadPlayback.Reset();
        BlackoutWarnings.RefreshClients();
        timer = Timing.CallDelayed(Delay, CutPower);
        foreach (var pickup in Pickup.List.Where(p => p.Type == ItemType.Radio).ToArray())
            RemoveRadio(pickup);
        foreach (var chamber in Locker.List.SelectMany(locker => locker.Chambers))
        {
            var hidden = chamber.Base.ToBeSpawned.Concat(chamber.Base.Content)
                .Where(p => p && p.Info.ItemId == ItemType.Radio).Distinct().ToArray();
            foreach (var radio in hidden) chamber.RemoveItem(Pickup.Get(radio));
        }
        foreach (var player in Player.List.Where(p => !p.IsHost))
        {
            Soundtrack.Apply(player);
            if (player.IsHuman) Equip(player);
        }
    }

    internal void Equip(Player player)
    {
        foreach (var radio in player.Items.Where(i => i.Type == ItemType.Radio).ToArray())
            player.RemoveItem(radio);
        if (player.Items.Any(i => i.Type == ItemType.Flashlight)) return;
        if (player.IsInventoryFull) player.DropItem(player.Items.Last());
        player.AddItem(ItemType.Flashlight);
    }

    internal void PickupAdded(ItemPickupBase native)
    {
        if (!Active || native.Info.ItemId != ItemType.Radio) return;
        var pickup = Pickup.Get(native);
        Timing.CallDelayed(0f, () =>
        {
            if (Active && !pickup.IsDestroyed) RemoveRadio(pickup);
        });
    }

    private static void RemoveRadio(Pickup pickup)
    {
        foreach (var chamber in Locker.List.SelectMany(locker => locker.Chambers))
        {
            chamber.Base.Content.Remove(pickup.Base);
            chamber.Base.ToBeSpawned.Remove(pickup.Base);
        }
        pickup.Destroy();
    }

    private void CutPower()
    {
        if (!Active || Dark) return;
        foreach (var light in LightsController.List)
        {
            restore[light.Base] = light.LightsEnabled;
            light.LightsEnabled = false;
        }
        Dark = true;
        Audio.PlayOutage();
        float intermission = Audio.OpeningSpeechSeconds > 0 ? Audio.OpeningSpeechSeconds + BlackoutAudio.SpeakerWarmup + .5f : 0f;
        announcement = new BlackoutAnnouncement(intermission, FinishAnnouncement);
        if (!CassieAnnouncementDispatcher.AddToQueue(announcement))
        {
            if (Audio.HasIntrusions) ScheduleIntrusion();
            Logger.Warn("CASSIE rejected the Blackout announcement.");
        }
        else if (Audio.HasIntrusions && Audio.OpeningSpeechSeconds == 0) ScheduleIntrusion();
        Logger.Info($"[METHADRENALINE] Blackout: {restore.Count} lighting controllers offline.");
    }

    private void FinishAnnouncement(float tailSeconds)
    {
        if (!Dark || Audio.OpeningSpeechSeconds == 0) return;

        openingTimer = Timing.CallDelayed(tailSeconds, () =>
        {
            Audio.PlayOpeningSpeech();
            if (Audio.HasIntrusions) ScheduleIntrusion();
        });
    }

    private void ScheduleIntrusion()
        => intrusionTimer = Timing.CallDelayed(UnityEngine.Random.Range(55f, 115f), PlayIntrusion);

    private void PlayIntrusion()
    {
        if (!Dark) return;
        if (Announcer.IsSpeaking)
        {
            intrusionTimer = Timing.CallDelayed(4f, PlayIntrusion);
            return;
        }
        Audio.PlayIntrusion();
        ScheduleIntrusion();
    }

    internal void HoldDark(RoomLightController light, ref bool enabled)
    {
        if (!Dark) return;
        restore[light] = enabled;
        enabled = false;
    }

    internal void Reset(bool keepAnnouncementsMuted = false)
    {
        Active = Dark = false;
        if (!keepAnnouncementsMuted && SuppressAnnouncements)
        {
            SuppressAnnouncements = false;
            Soundtrack.Reset();
            BlackoutWarnings.RefreshClients();
        }
        Timing.KillCoroutines(timer);
        Timing.KillCoroutines(openingTimer);
        Timing.KillCoroutines(intrusionTimer);
        if (announcement != null) CassieAnnouncementDispatcher.Cancel(announcement);
        announcement = null;
        Audio.Stop();
        foreach (var pair in restore)
            if (pair.Key) pair.Key.NetworkLightsEnabled = pair.Value;
        restore.Clear();
    }
}

[HarmonyPatch(typeof(RoomLightController), "SetLights")]
internal static class BlackoutLightsPatch
{
    private static void Prefix(RoomLightController __instance, ref bool __0)
        => ServerPlugin.Instance?.Blackout.HoldDark(__instance, ref __0);
}

[HarmonyPatch(typeof(InventoryExtensions), nameof(InventoryExtensions.ServerAddItem))]
internal static class BlackoutRadioGrantPatch
{
    private static bool Prefix(ItemType type)
        => type != ItemType.Radio || ServerPlugin.Instance?.Blackout.Active != true;
}
