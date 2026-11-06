using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using LabApi.Features.Wrappers;
using MapGeneration;
using MapGeneration.Distributors;
using TMPro;
using UnityEngine;

namespace Methadrenaline.Server;

internal sealed class SiteMapLayout
{
    // RueI splits large hints into native string parameters. Bound the total work
    // independently of the 16-bit limit on each individual network string.
    internal const int MaximumBytes = 768 * 1024;
    private const int MapWidth = 1110;
    private const int BodyHeight = 620;
    private const int BodyTop = 105;
    private const string Gold = "FFC75B";
    private const string Paper = "527D95";
    private readonly RoomIdentifier[] rooms;
    private readonly Vector2[] centers, labels;
    private readonly List<SiteMapInk> ink;
    private readonly Dictionary<RoomIdentifier, List<SiteMapInk>> selectedInk = new();
    private readonly Dictionary<string, List<MapLabel>> localizedLabels = new();
    private readonly float side;
    private readonly int width, height;
    private readonly TMP_FontAsset font;
    private string? baseDrawing;
    private readonly Dictionary<RoomIdentifier, string> highlights = new();
    private readonly Dictionary<(RoomIdentifier, string), string> frames = new();

    internal RoomIdentifier[] LabeledRooms { get; }
    internal bool UsesNativeOutlines => rooms.All(room => room.Icon && SiteMapIcon.Contains(room.Icon.name));

    internal SiteMapLayout(RoomIdentifier[] generated)
    {
        font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (!font || font.faceInfo.ascentLine <= font.faceInfo.descentLine)
            throw new InvalidOperationException("The native map font metrics are unavailable.");
        rooms = generated.OrderByDescending(room => room.MainCoords.z)
            .ThenBy(room => room.MainCoords.x).ThenBy(room => room.MainCoords.y).ToArray();
        const float grid = 15f;
        float left = rooms.Min(room => room.transform.position.x) - grid / 2;
        float top = rooms.Max(room => room.transform.position.z) + grid / 2;
        float worldWidth = rooms.Max(room => room.transform.position.x) + grid / 2 - left;
        float worldHeight = top - rooms.Min(room => room.transform.position.z) + grid / 2;
        float scale = Math.Min(MapWidth / worldWidth, BodyHeight / worldHeight);
        side = grid * scale;
        width = (int)Math.Ceiling(worldWidth * scale);
        height = (int)Math.Ceiling(worldHeight * scale);
        centers = rooms.Select(room => new Vector2((room.transform.position.x - left) * scale,
            (top - room.transform.position.z) * scale)).ToArray();
        labels = new Vector2[rooms.Length];
        var pixels = new byte[width * height * SiteMapRaster.Density * SiteMapRaster.Density];
        for (int i = 0; i < rooms.Length; i++)
        {
            var icon = SiteMapIcon.Get(rooms[i].Icon.name);
            float yaw = rooms[i].transform.eulerAngles.y;
            SiteMapRaster.Stamp(pixels, width * SiteMapRaster.Density, height * SiteMapRaster.Density, icon, centers[i], side, yaw);
            float angle = yaw * Mathf.Deg2Rad;
            float dx = (icon.LabelX / icon.Size - .5f) * side, dy = (icon.LabelY / icon.Size - .5f) * side;
            labels[i] = centers[i] + new Vector2(Mathf.Cos(angle) * dx - Mathf.Sin(angle) * dy,
                Mathf.Sin(angle) * dx + Mathf.Cos(angle) * dy);
            float radius = side * .708f;
            var crop = new Vector2(Math.Max(0, Mathf.Floor((centers[i].x - radius) * SiteMapRaster.Density)),
                Math.Max(0, Mathf.Floor((centers[i].y - radius) * SiteMapRaster.Density))) / SiteMapRaster.Density;
            int localWidth = Math.Min(width * SiteMapRaster.Density,
                Mathf.CeilToInt((centers[i].x + radius) * SiteMapRaster.Density)) - Mathf.RoundToInt(crop.x * SiteMapRaster.Density);
            int localHeight = Math.Min(height * SiteMapRaster.Density,
                Mathf.CeilToInt((centers[i].y + radius) * SiteMapRaster.Density)) - Mathf.RoundToInt(crop.y * SiteMapRaster.Density);
            var localCenter = centers[i] - crop;
            var localPixels = new byte[localWidth * localHeight];
            SiteMapRaster.Stamp(localPixels, localWidth, localHeight,
                icon, localCenter, side, yaw);
            selectedInk[rooms[i]] = SiteMapRaster.Rectangles(localPixels,
                localWidth, localHeight).Select(part =>
                new SiteMapInk(new Rect(part.Bounds.position + crop, part.Bounds.size), part.Alpha)).ToList();
        }
        ink = SiteMapRaster.Rectangles(pixels, width * SiteMapRaster.Density, height * SiteMapRaster.Density);
        LabeledRooms = rooms.Where(IsLandmark).ToArray();
    }

    internal static bool IsLandmark(RoomIdentifier room) => room.Name is not
        (RoomName.Unnamed or RoomName.LczAirlock or RoomName.HczRampTunnel or RoomName.HczTesla) &&
        (IsTransition(room) || room.Name is
        RoomName.LczClassDSpawn or RoomName.LczComputerRoom or RoomName.LczToilets or RoomName.LczArmory or
        RoomName.Lcz173 or RoomName.LczGlassroom or RoomName.Lcz330 or RoomName.Lcz914 or RoomName.LczGreenhouse or
        RoomName.Hcz049 or RoomName.Hcz079 or RoomName.Hcz096 or RoomName.Hcz106 or RoomName.Hcz939 or RoomName.Hcz127 or
        RoomName.HczMicroHID or RoomName.HczArmory or RoomName.HczServers or RoomName.HczWarhead or RoomName.HczTestroom or
        RoomName.EzRedroom or RoomName.EzEvacShelter or RoomName.EzIntercom or
        RoomName.EzOfficeStoried or RoomName.EzOfficeLarge or RoomName.EzOfficeSmall);

    internal void Prepare(LocalizationService? localization)
    {
        foreach (var language in new[] { Methadrenaline.Server.Core.ServerLanguage.English,
                     Methadrenaline.Server.Core.ServerLanguage.Ukrainian, Methadrenaline.Server.Core.ServerLanguage.Russian })
            foreach (var room in rooms)
                Render(room, key => localization?.Catalog.Translate(key, language) ?? key);
    }

    internal static bool IsTransition(RoomIdentifier room) =>
        room.Name is RoomName.LczCheckpointA or RoomName.LczCheckpointB or
            RoomName.HczCheckpointA or RoomName.HczCheckpointB or
            RoomName.HczCheckpointToEntranceZone or RoomName.EzGateA or RoomName.EzGateB ||
        room.ConnectedRooms.Any(neighbor => neighbor.Zone != room.Zone) ||
        room.GetComponentsInChildren<Interactables.Interobjects.ElevatorDoor>(true).Length > 0;

    internal string Render(RoomIdentifier current, Func<string, string> localize)
    {
        string language = localize(SiteMapController.CardName);
        if (frames.TryGetValue((current, language), out var cached)) return cached;
        int originX = 42 + (MapWidth - width) / 2;
        int originY = BodyTop + (BodyHeight - height) / 2;
        var text = new StringBuilder();

        void Label(double y, double x, double size, string color, string value, bool bold = false)
        {
            text.Append("<line-height=0>\n<nobr><voffset=").Append(Format(-y)).Append("><pos=").Append(Format(x)).Append("><size=").Append(Format(size)).Append("><color=#").Append(color).Append('>');
            if (bold) text.Append("<b>");
            text.Append(value.Replace(' ', '\u00A0'));
            if (bold) text.Append("</b>");
            text.Append("</color></size></nobr>");
        }

        baseDrawing ??= SiteMapPainter.Paint(font,
            new[] { new SiteMapInk(new Rect(24, -18, 1154, BodyTop + BodyHeight + 34), 144) }, "0B141D") +
            SiteMapPainter.Paint(font, ink, Paper, originX, originY);
        if (!highlights.TryGetValue(current, out string highlight))
            highlights[current] = highlight = SiteMapPainter.Paint(font, selectedInk[current], Gold, originX, originY);

        // Labels are explicit zero-height lines. A nonbreaking space prevents
        // client fallback fonts from moving a word to the beginning of the line.
        text.Append("<width=32767><margin=0><indent=0><line-indent=0><line-height=0><align=left><font=\"LiberationSans SDF\" material=\"LiberationSans SDF - Outline\">");
        Label(0, 42, 15, "A0CBD6", localize(SiteMapController.CardName));
        Label(42, 42, 35, "F3F9FA", localize(SiteMapNames.Zone(current.Zone)), true);
        Label(76, 42, 19, Gold, localize("You are here") + " · " + localize(SiteMapNames.Room(current)));

        foreach (var label in GetLabels(localize))
        {
            for (int line = 0; line < label.Lines.Length; line++)
            {
                string value = label.Lines[line];
                double x = originX + label.Bounds.center.x - Measure(value, label.Size) / 2;
                double y = originY + label.Bounds.y + label.Size * (.8 + line * 1.1);
                Label(y, x, label.Size, "F3F9FA", value);
            }
        }
        return frames[(current, language)] = text.Append("<voffset=0></font><font=\"LiberationSans SDF\">").Append(baseDrawing).Append(highlight).Append("</font></align></width></margin></indent></line-indent>").ToString();
    }

    private static string Format(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    internal readonly struct MapLabel
    {
        internal readonly RoomIdentifier Room;
        internal readonly string[] Lines;
        internal readonly float Size;
        internal readonly Rect Bounds;
        internal MapLabel(RoomIdentifier room, string[] lines, float size, Rect bounds)
        { Room = room; Lines = lines; Size = size; Bounds = bounds; }
    }

    internal List<MapLabel> GetLabels(Func<string, string> localize)
    {
        string language = localize(SiteMapController.CardName);
        if (localizedLabels.TryGetValue(language, out var cached)) return cached;
        var result = new List<MapLabel>();
        foreach (var group in LabeledRooms.GroupBy(room =>
                     new Vector2(Mathf.Floor(centers[Array.IndexOf(rooms, room)].x / side),
                         Mathf.Floor(centers[Array.IndexOf(rooms, room)].y / side))))
        {
            var shared = group.OrderBy(room => room.MainCoords.y).ToArray();
            for (int slot = 0; slot < shared.Length; slot++)
            {
                var room = shared[slot];
                int index = Array.IndexOf(rooms, room);
                string name = SiteMapNames.MapLabel(room, localize).ToUpperInvariant();
                float availableWidth = side * .92f;
                float availableHeight = side * .70f / shared.Length;
                float size = Math.Min(15, side * .21f);
                string[] lines;
                while (true)
                {
                    lines = Wrap(name, availableWidth, size);
                    float widest = lines.Max(line => Measure(line, size));
                    if ((widest <= availableWidth && lines.Length * size * 1.1f <= availableHeight) || size <= 8) break;
                    size -= .25f;
                }
                float labelWidth = lines.Max(line => Measure(line, size));
                float labelHeight = lines.Length * size * 1.1f;
                // Keep every name inside its own tile, including rooms sharing an
                // elevator column. Text remains upright when the room rotates.
                Vector2 center = shared.Length == 1 ? centers[index] : (group.Key + Vector2.one * .5f) * side;
                Vector2 anchor = labels[index];
                anchor.x = Mathf.Clamp(anchor.x, center.x - (availableWidth - labelWidth) / 2, center.x + (availableWidth - labelWidth) / 2);
                anchor.y = shared.Length == 1
                    ? Mathf.Clamp(anchor.y, center.y - (side * .92f - labelHeight) / 2, center.y + (side * .92f - labelHeight) / 2)
                    : center.y + (slot - (shared.Length - 1) / 2f) * availableHeight;
                result.Add(new MapLabel(room, lines, size,
                    new Rect(anchor.x - labelWidth / 2, anchor.y - labelHeight / 2, labelWidth, labelHeight)));
            }
        }
        localizedLabels.Add(language, result);
        return result;
    }

    private string[] Wrap(string text, float width, float size)
    {
        var result = new List<string>();
        string line = "";
        foreach (string word in text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string joined = line.Length == 0 ? word : line + " " + word;
            if (line.Length != 0 && Measure(joined, size) > width) { result.Add(line); line = word; }
            else line = joined;
        }
        if (line.Length != 0) result.Add(line);
        return result.ToArray();
    }

    private float Measure(string text, float size)
    {
        float width = 0;
        foreach (char c in text)
        {
            if (!font.characterLookupTable.TryGetValue(c, out var character))
            {
                if (c >= '\u0400' && c <= '\u04FF') { width += SiteMapFontMetrics.Advance(c) * size; continue; }
                character = TMP_FontAssetUtilities.GetCharacterFromFontAsset(c, font, true, FontStyles.Normal, FontWeight.Regular, out _);
            }
            if (character == null)
                foreach (var fallback in TMP_Settings.fallbackFontAssets)
                {
                    if (!fallback.characterLookupTable.TryGetValue(c, out character))
                        character = TMP_FontAssetUtilities.GetCharacterFromFontAsset(c, fallback, true, FontStyles.Normal, FontWeight.Regular, out _);
                    if (character != null) break;
                }
            if (character == null && TMP_Settings.defaultFontAsset)
                character = TMP_FontAssetUtilities.GetCharacterFromFontAsset(c, TMP_Settings.defaultFontAsset, true, FontStyles.Normal, FontWeight.Regular, out _);
            if (character == null) { width += SiteMapFontMetrics.Advance(c) * size; continue; }
            width += character.glyph.metrics.horizontalAdvance * size / ((TMP_FontAsset)character.textAsset).faceInfo.pointSize;
        }
        return width;
    }
}
