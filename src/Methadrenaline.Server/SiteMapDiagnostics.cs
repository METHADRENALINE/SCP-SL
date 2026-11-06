using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Interactables.Interobjects.DoorUtils;
using InventorySystem.Items.Keycards;
using InventorySystem.Items.Pickups;
using LabApi.Features.Wrappers;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using MapGeneration;
using MEC;
using Methadrenaline.Server.Core;
using Mirror;
using PlayerRoles.FirstPersonControl;
using UnityEngine;
using LabCard = LabApi.Features.Wrappers.KeycardItem;

namespace Methadrenaline.Server;

internal static class SiteMapDiagnostics
{
    internal static IEnumerator<float> Probe(SiteMapController maps, LocalizationService localization, Player player,
        Func<Player, Type, string, string> hud, Action<string, bool> check)
    {
        var pickups = Pickup.List.Where(p => maps.IsMap(p.Type, p.Serial)).ToArray();
        var positions = pickups.Select(p => Room.GetRoomAtPosition(p.Position)).ToArray();
        LabApi.Features.Console.Logger.Info("[METHADRENALINE] Site map placement probe: " + string.Join(", ",
            positions.Select(room => room == null ? "unmapped" : room.Zone + "/" + room.Name + "/" + room.Base.MainCoords)));
        check("site_map_five_distinct_rooms_per_zone", pickups.Length == 15 && positions.All(room => room != null) &&
            positions.Distinct().Count() == 15 && positions.Count(room => room?.Zone == FacilityZone.LightContainment) == 5 &&
            positions.Count(room => room?.Zone == FacilityZone.HeavyContainment) == 5 &&
            positions.Count(room => room?.Zone == FacilityZone.Entrance) == 5);
        Vector3 savedPosition = player.Position;
        try
        {
            foreach (var pickup in pickups)
            {
                string label = pickup.Serial.ToString();
                var colliders = pickup.Base.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger).ToArray();
                var bounds = colliders[0].bounds;
                foreach (var collider in colliders.Skip(1)) bounds.Encapsulate(collider.bounds);
                bool supported = true;
                float largestGap = float.MinValue;
                foreach (float x in new[] { bounds.min.x, bounds.center.x, bounds.max.x })
                    foreach (float z in new[] { bounds.min.z, bounds.center.z, bounds.max.z })
                    {
                        bool hit = Physics.Raycast(new Vector3(x, bounds.max.y + 0.1f, z), Vector3.down, out var floor, 0.2f,
                            FpcStateProcessor.Mask, QueryTriggerInteraction.Ignore);
                        float gap = bounds.min.y - floor.point.y;
                        supported &= hit && gap >= -0.004f && gap <= 0.025f && floor.normal.y > 0.99f;
                        largestGap = Mathf.Max(largestGap, gap);
                    }
                LabApi.Features.Console.Logger.Info($"[METHADRENALINE] Map surface probe {label}: gap={largestGap:F4}m, height={bounds.size.y:F4}m.");
                check("site_map_flat_and_supported_" + label, supported && bounds.size.y < 0.04f);
                check("site_map_native_physics_" + label, pickup.Base.PhysicsModule is PickupStandardPhysics physics &&
                    physics.Rb.useGravity && !physics.Rb.isKinematic && physics.Rb.constraints != RigidbodyConstraints.FreezeAll);
                Vector3 target = new(bounds.center.x, bounds.max.y + 0.01f, bounds.center.z);
                Physics.SyncTransforms();
                bool blocked = Physics.Linecast(target + Vector3.up * 1.5f, target, out var obstacle,
                    FpcStateProcessor.Mask, QueryTriggerInteraction.Ignore);
                if (blocked) LabApi.Features.Console.Logger.Warn($"[METHADRENALINE] Map {label} view blocked by {obstacle.collider.name} at {obstacle.distance:F3}m.");
                check("site_map_unobstructed_from_above_" + label, !blocked);
                player.Position = pickup.Position + Vector3.up * 0.2f;
                check("site_map_native_searchable_" + label,
                    pickup.Base.GetSearchCompletor(player.ReferenceHub.searchCoordinator, 100).ValidateStart());
            }
        }
        finally { player.Position = savedPosition; }
        var rooms = RoomIdentifier.AllRoomIdentifiers.Where(room => room &&
            room.Zone is FacilityZone.LightContainment or FacilityZone.HeavyContainment or FacilityZone.Entrance).ToArray();
        var keys = new HashSet<string>(localization.Catalog.Entries.Select(entry => entry.Source), StringComparer.Ordinal);
        check("site_map_all_generated_room_names_localized", rooms.All(room => keys.Contains(SiteMapNames.Room(room))));
        foreach (var zone in rooms.GroupBy(room => room.Zone))
        {
            var generated = zone.ToArray();
            var plan = new SiteMapLayout(generated);
            check("site_map_game_room_outlines_" + zone.Key, plan.UsesNativeOutlines);
            check("site_map_only_landmarks_labeled_" + zone.Key, plan.LabeledRooms.All(room =>
                SiteMapLayout.IsLandmark(room)) &&
                generated.Where(SiteMapLayout.IsLandmark).All(plan.LabeledRooms.Contains));
            foreach (var language in new[] { ServerLanguage.English, ServerLanguage.Ukrainian, ServerLanguage.Russian })
            {
                string Translate(string key) => localization.Catalog.Translate(key, language);
                string rendered = plan.Render(generated[0], Translate);
                int bytes = generated.Max(room => System.Text.Encoding.UTF8.GetByteCount(plan.Render(room, Translate)));
                LabApi.Features.Console.Logger.Info($"[METHADRENALINE] Site map {zone.Key}/{language}: {bytes} UTF-8 bytes.");
                var labels = plan.GetLabels(Translate);
                check("site_map_inline_names_" + zone.Key + "_" + language,
                    labels.Count == plan.LabeledRooms.Length && labels.All(label =>
                        string.Join(" ", label.Lines) == SiteMapNames.MapLabel(label.Room, Translate).ToUpperInvariant() &&
                        label.Lines.All(line => rendered.Contains(">" + line.Replace(' ', '\u00A0') + "<"))) &&
                    !rendered.Contains(Translate("Room index")) &&
                    !rendered.Contains(Translate("Inspect the item to show or hide the map (I by default)")) &&
                    rendered.Contains("<mark=#0B141D90>") && rendered.Contains("<pos=") &&
                    rendered.Contains("<line-height=") && bytes < SiteMapLayout.MaximumBytes);
                check("site_map_names_do_not_overlap_" + zone.Key + "_" + language,
                    labels.All(label => !labels.Any(other => label.Room != other.Room && label.Bounds.Overlaps(other.Bounds))));
                foreach (var label in labels)
                    foreach (var other in labels.Where(other => label.Room != other.Room && label.Bounds.Overlaps(other.Bounds)))
                        LabApi.Features.Console.Logger.Warn("[METHADRENALINE] Map label overlap: " +
                            string.Join(" ", label.Lines) + " " + label.Bounds + " / " + string.Join(" ", other.Lines) + " " + other.Bounds);
                SiteMapRenderingDiagnostics.Probe(player, rendered, zone.Key + "_" + language, check);
                yield return Timing.WaitForOneFrame;
            }
        }
        if (pickups.Length == 0) yield break;

        var database = (Dictionary<ushort, ArraySegment<byte>>)AccessTools.Field(typeof(KeycardDetailSynchronizer), "Database").GetValue(null);
        var payload = database[pickups[0].Serial];
        string ReadName(ArraySegment<byte> data) { using var reader = NetworkReaderPool.Get(data); return reader.ReadString(); }
        check("site_map_native_pickup_name_localized", ReadName(payload) == SiteMapController.CardName &&
            ReadName(CustomCardLocalization.Translate(payload, ServerLanguage.Ukrainian)) == "Мапа Зони-02" &&
            ReadName(CustomCardLocalization.Translate(payload, ServerLanguage.Russian)) == "Карта Зоны-02");

        Vector3 previous = player.Position;
        var previousItem = player.CurrentItem;
        var languages = (Dictionary<ReferenceHub, ServerLanguage>)AccessTools.Field(typeof(LocalizationService), "languages").GetValue(localization);
        bool hadLanguage = languages.TryGetValue(player.ReferenceHub, out var previousLanguage);
        LabCard? card = null;
        try
        {
            player.Position = pickups[0].Position + Vector3.up * 0.2f;
            bool searchable = pickups[0].Base.GetSearchCompletor(player.ReferenceHub.searchCoordinator, 100).ValidateStart();
            ushort serial = pickups[0].Serial;
            card = player.AddItem(pickups[0]) as LabCard;
            check("site_map_acquisition_preserves_identity_without_permissions", searchable && card != null &&
                card.Serial == serial && card.Permissions == DoorPermissionFlags.None);
            if (card == null) yield break;
            if (!pickups[0].IsDestroyed) pickups[0].Destroy();
            player.CurrentItem = card;
            foreach (var zone in new[] { FacilityZone.LightContainment, FacilityZone.HeavyContainment, FacilityZone.Entrance })
            {
                var room = Room.Get(zone).First(r => Room.GetRoomAtPosition(r.Position + Vector3.up) == r);
                player.Position = room.Position + Vector3.up;
                maps.RefreshViewers(new[] { player });
                string text = hud(player, typeof(SiteMapController), "HudTag").Replace('\u00A0', ' ');
                check("site_map_hud_follows_" + zone, text.Contains("You are here") &&
                    text.Contains(SiteMapNames.Room(room.Base)) && text.Contains("<mark=#FFC75B") &&
                    text.Contains("<pos=") && !text.Contains("Room index"));
            }
            foreach (var sample in new[] { (ServerLanguage.Ukrainian, "Вхідна зона (офіси)", "Ви тут"),
                (ServerLanguage.Russian, "Входная зона (офисы)", "Вы здесь") })
            {
                languages[player.ReferenceHub] = sample.Item1;
                maps.RefreshViewers(new[] { player });
                string text = hud(player, typeof(SiteMapController), "HudTag").Replace('\u00A0', ' ');
                check("site_map_hud_localized_" + sample.Item1, text.Contains(sample.Item2) && text.Contains(sample.Item3));
            }
            var inspect = new PlayerInspectingKeycardEventArgs(player.ReferenceHub,
                (InventorySystem.Items.Keycards.KeycardItem)card.Base);
            PlayerEvents.OnInspectingKeycard(inspect);
            maps.RefreshViewers(new[] { player });
            check("site_map_inspect_does_not_dismiss", !inspect.IsAllowed && hud(player, typeof(SiteMapController), "HudTag") != "");
            player.CurrentItem = null;
            maps.RefreshViewers(new[] { player });
            check("site_map_hud_removed_when_put_away", hud(player, typeof(SiteMapController), "HudTag") == "");
            player.CurrentItem = card;
            maps.RefreshViewers(new[] { player });
            check("site_map_reopens_after_reequipping", hud(player, typeof(SiteMapController), "HudTag") != "");
        }
        finally
        {
            if (hadLanguage) languages[player.ReferenceHub] = previousLanguage;
            else languages.Remove(player.ReferenceHub);
            if (card != null) player.RemoveItem(card);
            player.Position = previous;
            player.CurrentItem = previousItem;
        }
    }
}
