using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using InventorySystem;
using InventorySystem.Items;
using InventorySystem.Items.Keycards;
using InventorySystem.Items.Pickups;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Arguments.Scp914Events;
using LabApi.Events.Handlers;
using LabApi.Features.Wrappers;
using MapGeneration;
using Methadrenaline.Server.Core;
using Mirror;
using PlayerRoles.FirstPersonControl;
using RueI.API;
using RueI.API.Elements;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;
using NativeCard = InventorySystem.Items.Keycards.KeycardItem;

namespace Methadrenaline.Server;

internal sealed class SiteMapController : IDisposable
{
    internal const string CardName = "Site-02 Map";
    internal const ItemType CardType = ItemType.KeycardCustomTaskForce;
    internal const int MapsPerZone = 5;
    private static readonly Tag HudTag = new("methadrenaline.site-map");
    private static readonly FacilityZone[] SpawnZones =
        { FacilityZone.LightContainment, FacilityZone.HeavyContainment, FacilityZone.Entrance };
    private static readonly Action<ushort, ArraySegment<byte>> PublishCard =
        (Action<ushort, ArraySegment<byte>>)AccessTools.Method(typeof(KeycardDetailSynchronizer), "ServerAddDatabaseEntry")
            .CreateDelegate(typeof(Action<ushort, ArraySegment<byte>>));
    private readonly System.Random random = new();
    private readonly HashSet<ushort> serials = new();
    private readonly Dictionary<FacilityZone, SiteMapLayout> layouts = new();
    private readonly SiteMapSublevels sublevels = new();
    private readonly Dictionary<Player, (RoomIdentifier Room, ServerLanguage Language, SiteMapSublevels.Page? Page, int Region)> viewers = new();
    private float nextUpdate;

    internal void Enable()
    {
        StaticUnityMethods.OnUpdate += Update;
        PlayerEvents.ChangedItem += ChangedItem;
        PlayerEvents.Left += Left;
        PlayerEvents.InspectingKeycard += InspectingKeycard;
        Scp914Events.ProcessingInventoryItem += ProcessingItem;
        Scp914Events.ProcessingPickup += ProcessingPickup;
    }

    internal void BeginRound()
    {
        Reset();
        if (ServerPlugin.Instance?.IsParavozRound == true) return;
        try
        {
            var rooms = RoomIdentifier.AllRoomIdentifiers.Where(room => room &&
                room.Zone is FacilityZone.LightContainment or FacilityZone.HeavyContainment or FacilityZone.Entrance).ToArray();
            foreach (var group in rooms.GroupBy(room => room.Zone))
                layouts.Add(group.Key, new SiteMapLayout(group.ToArray()));
            foreach (var layout in layouts.Values) layout.Prepare(Localizer.Current);
            sublevels.Prepare(rooms, Localizer.Current);

            var locations = new Dictionary<RoomIdentifier, Vector3>();
            foreach (var pose in SafeLocationFinder.GetLocations(null, null))
            {
                Vector3 forward = pose.rotation * Vector3.forward;
                AddLocation(locations, pose.position + forward * 1.2f);
                AddLocation(locations, pose.position - forward * 1.2f);
            }
            foreach (var room in rooms.Where(r => SpawnZones.Contains(r.Zone) && !locations.ContainsKey(r)))
                AddLocation(locations, room.transform.position);
            foreach (var zone in SpawnZones)
            {
                var selected = locations.Where(location => location.Key.Zone == zone)
                    .OrderBy(_ => random.Next()).Take(MapsPerZone).ToArray();
                foreach (var location in selected) Spawn(location.Value);
                if (selected.Length != MapsPerZone)
                    Logger.Warn($"[METHADRENALINE] Site-02 Map found {selected.Length} valid rooms in {zone}; expected {MapsPerZone}.");
            }
        }
        catch (Exception error)
        {
            Reset();
            Logger.Error("[METHADRENALINE] Site-02 Map setup failed: " + error);
        }
    }

    private static void AddLocation(Dictionary<RoomIdentifier, Vector3> locations, Vector3 candidate)
    {
        if (!Physics.Raycast(candidate + Vector3.up * 0.8f, Vector3.down, out var floor, 2,
                FpcStateProcessor.Mask, QueryTriggerInteraction.Ignore) || floor.normal.y < 0.99f) return;
        Vector3 ground = floor.point;
        if (Physics.CheckCapsule(ground + Vector3.up * 0.36f, ground + Vector3.up * 1.45f, 0.35f,
                FpcStateProcessor.Mask, QueryTriggerInteraction.Ignore)) return;
        foreach (var offset in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
            if (!Physics.Raycast(ground + offset * 0.14f + Vector3.up * 0.1f, Vector3.down, out var edge, 0.13f,
                    FpcStateProcessor.Mask, QueryTriggerInteraction.Ignore) || edge.normal.y < 0.99f ||
                Mathf.Abs(edge.point.y - ground.y) > 0.01f) return;
        Vector3 position = ground + Vector3.up * 0.03f;
        var room = Room.GetRoomAtPosition(position)?.Base;
        if (!room || !SpawnZones.Contains(room!.Zone) ||
            room.Name is RoomName.HczTesla or RoomName.HczWaysideIncinerator or RoomName.Hcz096) return;
        if (!locations.ContainsKey(room)) locations.Add(room, position);
    }

    private void Spawn(Vector3 position)
    {
        if (!CardType.TryGetTemplate(out NativeCard template))
            throw new InvalidOperationException("Site-02 Map template is unavailable.");
        ushort serial = ItemSerialGenerator.GenerateNext();
        var pickup = InventoryExtensions.ServerCreatePickup(template, new PickupSyncInfo(CardType, template.Weight, serial),
            position, Quaternion.Euler(0, random.Next(360), 0), spawn: false);
        try
        {
            if (pickup.PhysicsModule is PickupStandardPhysics physics)
            {
                physics.Rb.WakeUp();
            }
            using var writer = NetworkWriterPool.Get();
            foreach (var detail in template.Details.OfType<SyncedDetail>())
            {
                if (detail is CustomItemNameDetail || detail is NametagDetail) writer.WriteString(CardName);
                else if (detail is CustomTintDetail) writer.WriteColor32(new Color32(31, 100, 116, 255));
                else detail.WriteDefault(writer);
            }
            var payload = new byte[2048];
            var encoded = writer.ToArraySegment();
            Array.Copy(encoded.Array!, encoded.Offset, payload, 0, encoded.Count);
            PublishCard(serial, new ArraySegment<byte>(payload, 0, encoded.Count));
            serials.Add(serial);
            NetworkServer.Spawn(pickup.gameObject);
        }
        catch
        {
            pickup.DestroySelf();
            throw;
        }
    }

    internal bool IsMap(ItemType type, ushort serial) => type == CardType && serials.Contains(serial);

    internal string GiveTo(Player player)
    {
        if (ServerPlugin.Instance?.IsParavozRound == true) return "Site-02 Maps are unavailable in Paravoz mode.";
        if (!player.IsAlive || !player.IsHuman) return "A living human player is required.";
        var item = player.Items.FirstOrDefault(item => IsMap(item.Type, item.Serial));
        if (item == null)
        {
            var pickup = Pickup.List.FirstOrDefault(pickup => IsMap(pickup.Type, pickup.Serial));
            if (pickup == null) return "No Site-02 Maps are available in this round.";
            item = player.AddItem(pickup);
            if (item == null) return "The player cannot carry another item.";
            if (!pickup.IsDestroyed) pickup.Destroy();
        }
        player.CurrentItem = item;
        RefreshViewer(player);
        return "Site-02 Map equipped.";
    }

    private void ProcessingItem(Scp914ProcessingInventoryItemEventArgs ev)
    {
        if (IsMap(ev.Item.Type, ev.Item.Serial)) ev.IsAllowed = false;
    }

    private void ProcessingPickup(Scp914ProcessingPickupEventArgs ev)
    {
        if (IsMap(ev.Pickup.Type, ev.Pickup.Serial)) ev.IsAllowed = false;
    }

    private void Update()
    {
        if (serials.Count == 0 || Time.unscaledTime < nextUpdate) return;
        nextUpdate = Time.unscaledTime + 0.2f;
        RefreshViewers(Player.ReadyList);
    }

    private void ChangedItem(PlayerChangedItemEventArgs ev)
    {
        if (ev.OldItem?.Serial == ev.NewItem?.Serial) return;
        if (ev.NewItem is not { } item || !IsMap(item.Type, item.Serial)) Hide(ev.Player);
        else RefreshViewer(ev.Player);
    }

    private void Left(PlayerLeftEventArgs ev)
    {
        viewers.Remove(ev.Player);
    }

    internal void InspectingKeycard(PlayerInspectingKeycardEventArgs ev)
    {
        if (!IsMap(ev.KeycardItem.Type, ev.KeycardItem.Serial)) return;
        ev.IsAllowed = false;
    }

    internal void RefreshViewers(IEnumerable<Player> players)
    {
        var ready = new HashSet<Player>(players);
        foreach (var player in ready) RefreshViewer(player);
        foreach (var player in viewers.Keys.Where(player => !ready.Contains(player)).ToArray()) Hide(player);
    }

    private void RefreshViewer(Player player)
    {
        if (!player.IsAlive || player.CurrentItem is not { } item || !IsMap(item.Type, item.Serial))
        {
            Hide(player);
            return;
        }
        if (player.Room?.Base is not { } room || !layouts.TryGetValue(room.Zone, out var layout))
        {
            Hide(player);
            return;
        }

        var language = Localizer.Current?.Language(player.ReferenceHub) ?? ServerLanguage.English;
        var page = sublevels.Find(room, player.Position);
        int region = page?.RegionAt(player.Position) ?? 0;
        if (viewers.TryGetValue(player, out var last) && last == (room, language, page, region)) return;
        string text = page?.Render(region, language, Localizer.Current) ??
            layout.Render(room, key => Localizer.Current?.Catalog.Translate(key, language) ?? key);
        RueDisplay.Get(player).Show(HudTag, new SiteMapElement(text));
        viewers[player] = (room, language, page, region);
    }

    private void Hide(Player player)
    {
        if (!viewers.Remove(player) || player.IsDestroyed) return;
        var display = RueDisplay.Get(player);
        display.Remove(HudTag);
        display.Update();
    }

    internal void Reset()
    {
        foreach (var player in viewers.Keys.ToArray()) Hide(player);
        foreach (var player in Player.ReadyList)
            foreach (var item in player.Items.Where(item => IsMap(item.Type, item.Serial)).ToArray())
                player.RemoveItem(item);
        if (serials.Count > 0)
            foreach (var identity in NetworkServer.spawned.Values.ToArray())
                if (identity && identity.TryGetComponent<ItemPickupBase>(out var pickup) &&
                    IsMap(pickup.Info.ItemId, pickup.Info.Serial)) pickup.DestroySelf();
        serials.Clear();
        layouts.Clear();
        sublevels.Reset();
    }

    public void Dispose()
    {
        StaticUnityMethods.OnUpdate -= Update;
        PlayerEvents.ChangedItem -= ChangedItem;
        PlayerEvents.Left -= Left;
        PlayerEvents.InspectingKeycard -= InspectingKeycard;
        Scp914Events.ProcessingInventoryItem -= ProcessingItem;
        Scp914Events.ProcessingPickup -= ProcessingPickup;
        Reset();
    }
}
