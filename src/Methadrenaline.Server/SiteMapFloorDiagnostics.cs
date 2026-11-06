using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LabApi.Features.Wrappers;
using MapGeneration;
using MEC;
using Methadrenaline.Server.Core;
using UnityEngine;

namespace Methadrenaline.Server;

internal static class SiteMapFloorDiagnostics
{
    internal static IEnumerator<float> Probe(LocalizationService localization, Player player, Action<string, bool> check)
    {
        var rooms = RoomIdentifier.AllRoomIdentifiers.Where(room => room && room.Zone is
            FacilityZone.LightContainment or FacilityZone.HeavyContainment or FacilityZone.Entrance).ToArray();
        var atlas = new SiteMapSublevels(); atlas.Prepare(rooms, localization);
        var templates = SiteMapSublevels.Templates.ToArray();
        check("floor_atlas_has_combined_containment", templates.Any(template => template.Room == RoomName.Hcz049 &&
            template.Captions.Any(caption => caption.Key == "SCP-049 chamber") &&
            template.Captions.Any(caption => caption.Key == "SCP-173 chamber")));
        check("floor_atlas_has_warhead_and_servers", templates.Any(template => template.Room == RoomName.HczWarhead) &&
            templates.Any(template => template.Room == RoomName.HczServers));
        check("pt00_is_not_labeled_scp173", rooms.Where(room => room.Name == RoomName.Lcz173)
            .All(room => SiteMapNames.Room(room) == "Testing chamber PT00" &&
                !SiteMapNames.MapLabel(room, key => key).Contains("SCP-173")));
        foreach (var template in templates)
        {
            var room = rooms.FirstOrDefault(room => room.Name == template.Room);
            if (!room) continue;
            string name = template.Room + "_" + template.ReferenceY.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            check("floor_atlas_regions_valid_" + name, template.Regions.Count > 0 &&
                template.Cells.Where(value => value != 0).All(value => template.Regions.ContainsKey(value)) &&
                template.Captions.All(caption => template.Regions.ContainsKey(caption.Region) &&
                    localization.Catalog.Entries.Any(entry => entry.Source == caption.Key)));
            Vector2 point = template.Captions.Count != 0 ? template.Captions[0].Point :
                new Vector2(template.Left + template.Width * template.Step / 2, template.Top - template.Height * template.Step / 2);
            Vector3 world = room!.transform.TransformPoint(new Vector3(point.x, template.ReferenceY + 1, point.y));
            var page = atlas.Find(room, world);
            check("floor_switches_at_native_height_" + name, page != null && page.Data == template);
            check("floor_leaves_main_zone_map_intact_" + name, atlas.Find(room, room.transform.position + Vector3.up) == null);
            if (page == null) continue;
            int region = page.RegionAt(world);
            check("floor_native_chamber_partition_" + name, region != 0);
            foreach (var language in new[] { ServerLanguage.English, ServerLanguage.Ukrainian, ServerLanguage.Russian })
            {
                string text = page.Render(region, language, localization);
                check("floor_localized_names_and_drawing_" + name + "_" + language,
                    text.Contains(localization.Catalog.Translate(SiteMapController.CardName, language).Replace(' ', '\u00A0')) &&
                    text.Contains("<mark=#527D95") && text.Contains("<mark=#FFC75B") &&
                    template.Captions.All(caption => (caption.Key.StartsWith("SCP-", StringComparison.Ordinal) ?
                        caption.Key.Split(' ')[0] : localization.Catalog.Translate(caption.Key, language).ToUpperInvariant())
                        .Split(' ').All(word => text.Contains(word))) &&
                    Encoding.UTF8.GetByteCount(text) < SiteMapLayout.MaximumBytes);
                SiteMapRenderingDiagnostics.Probe(player, text, "floor_" + name + "_" + language, check);
                yield return Timing.WaitForOneFrame;
            }
        }
    }
}
