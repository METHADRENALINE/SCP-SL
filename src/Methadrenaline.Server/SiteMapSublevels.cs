using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using MapGeneration;
using Methadrenaline.Server.Core;
using TMPro;
using UnityEngine;

namespace Methadrenaline.Server;

// These floor plans are compiled from native collision geometry, doors and
// camera locations. Production never raycasts or rebuilds geometry while moving.
internal sealed class SiteMapSublevels
{
    private static Definition[]? definitions;
    private static Definition[] Definitions => definitions ??= Load();
    private readonly Dictionary<RoomIdentifier, Page[]> pages = new();

    internal void Prepare(IEnumerable<RoomIdentifier> rooms, LocalizationService? localization)
    {
        pages.Clear();
        foreach (var room in rooms)
        {
            var definitions = Definitions.Where(definition => definition.Room == room.Name).ToArray();
            if (definitions.Length == 0) continue;
            var list = definitions.Select(definition => new Page(room, definition)).ToArray();
            foreach (var page in list) page.Prepare(localization);
            pages.Add(room, list);
        }
    }

    internal Page? Find(RoomIdentifier room, Vector3 worldPosition)
    {
        if (!pages.TryGetValue(room, out var list)) return null;
        float y = room.transform.InverseTransformPoint(worldPosition).y - 1f;
        return list.FirstOrDefault(page => y > page.Data.MinimumY + .1f && y < page.Data.MaximumY - .1f);
    }

    internal void Reset() => pages.Clear();
    internal static IEnumerable<Definition> Templates => Definitions;

    internal sealed class Definition
    {
        internal RoomName Room;
        internal float ReferenceY, MinimumY, MaximumY, Left, Top, Step;
        internal int Width, Height;
        internal ushort[] Cells = null!;
        internal List<SiteMapInk> Ink = null!;
        internal readonly Dictionary<int, List<SiteMapInk>> Regions = new();
        internal readonly List<Caption> Captions = new();
    }

    internal sealed class Caption
    {
        internal int Region;
        internal Vector2 Point;
        internal string Key = "";
    }

    internal sealed class Page
    {
        private const int MapWidth = 1110, BodyHeight = 620, BodyTop = 105;
        internal readonly RoomIdentifier Room;
        internal readonly Definition Data;
        private readonly TMP_FontAsset font;
        private readonly float scale;
        private readonly Vector2 offset;
        private readonly string drawing;
        private readonly Dictionary<int, string> highlights = new();
        private readonly Dictionary<(int Region, ServerLanguage Language), string> frames = new();

        internal Page(RoomIdentifier room, Definition data)
        {
            Room = room; Data = data;
            font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            float width = data.Width * data.Step, height = data.Height * data.Step;
            scale = Math.Min(MapWidth / width, BodyHeight / height);
            offset = new Vector2(42 + (MapWidth - width * scale) / 2, BodyTop + (BodyHeight - height * scale) / 2);
            drawing = SiteMapPainter.Paint(font, new[] { new SiteMapInk(new Rect(24, -18, 1154, BodyTop + BodyHeight + 34), 144) }, "0B141D") +
                SiteMapPainter.Paint(font, Project(data.Ink), "527D95");
            foreach (var region in data.Regions)
                highlights.Add(region.Key, SiteMapPainter.Paint(font, Project(region.Value), "FFC75B"));
        }

        private IEnumerable<SiteMapInk> Project(IEnumerable<SiteMapInk> ink) => ink.Select(part =>
            new SiteMapInk(new Rect(offset + part.Bounds.position * (Data.Step * scale),
                part.Bounds.size * (Data.Step * scale)), part.Alpha));

        internal int RegionAt(Vector3 worldPosition)
        {
            Vector3 position = Room.transform.InverseTransformPoint(worldPosition);
            int x = Mathf.FloorToInt((position.x - Data.Left) / Data.Step);
            int y = Mathf.FloorToInt((Data.Top - position.z) / Data.Step);
            if (x >= 0 && y >= 0 && x < Data.Width && y < Data.Height)
            {
                int id = Data.Cells[y * Data.Width + x];
                if (id != 0 && highlights.ContainsKey(id)) return id;
            }
            // Door thresholds and a moving elevator may lie between partitions.
            int maximum = Mathf.CeilToInt(1.2f / Data.Step);
            for (int radius = 1; radius <= maximum; radius++)
                for (int iy = Math.Max(0, y - radius); iy <= Math.Min(Data.Height - 1, y + radius); iy++)
                    for (int ix = Math.Max(0, x - radius); ix <= Math.Min(Data.Width - 1, x + radius); ix++)
                    {
                        int id = Data.Cells[iy * Data.Width + ix];
                        if (id != 0 && highlights.ContainsKey(id)) return id;
                    }
            return 0;
        }

        internal void Prepare(LocalizationService? localization)
        {
            foreach (var language in new[] { ServerLanguage.English, ServerLanguage.Ukrainian, ServerLanguage.Russian })
                foreach (int region in highlights.Keys.Concat(new[] { 0 })) Render(region, language, localization);
        }

        internal string Render(int region, ServerLanguage language, LocalizationService? localization)
        {
            if (frames.TryGetValue((region, language), out string cached)) return cached;
            string T(string key) => localization?.Catalog.Translate(key, language) ?? key;
            var text = new StringBuilder("<width=32767><margin=0><indent=0><line-indent=0><line-height=0><align=left><font=\"LiberationSans SDF\" material=\"LiberationSans SDF - Outline\">");
            void Label(float x, float y, float size, string color, string value, bool bold = false)
            {
                text.Append("<line-height=0>\n<nobr><voffset=").Append(F(-y)).Append("><pos=").Append(F(x))
                    .Append("><size=").Append(F(size)).Append("><color=#").Append(color).Append('>');
                if (bold) text.Append("<b>");
                text.Append(value.Replace(' ', '\u00A0'));
                if (bold) text.Append("</b>");
                text.Append("</color></size></nobr>");
            }
            var currentNames = Data.Captions.Where(caption => caption.Region == region).Select(caption => caption.Key).Distinct().ToArray();
            string current = currentNames.Length == 1 ? currentNames[0] : SiteMapNames.Room(Room);
            Label(42, 0, 15, "A0CBD6", T(SiteMapController.CardName) + " · " + T(SiteMapNames.Zone(Room.Zone)));
            Label(42, 42, 28, "F3F9FA", T(SiteMapNames.Room(Room)), true);
            Label(42, 76, 19, "FFC75B", T("You are here") + " · " + T(current));
            foreach (var caption in Data.Captions)
            {
                string value = caption.Key.StartsWith("SCP-", StringComparison.Ordinal) ? caption.Key.Split(' ')[0] : T(caption.Key);
                value = value.ToUpperInvariant();
                Vector2 point = offset + new Vector2(caption.Point.x - Data.Left, Data.Top - caption.Point.y) * scale;
                const float size = 12;
                var lines = new List<string>();
                string line = "";
                foreach (string word in value.Split(' '))
                {
                    string joined = line.Length == 0 ? word : line + " " + word;
                    if (line.Length != 0 && Measure(joined, size) > 112) { lines.Add(line); line = word; }
                    else line = joined;
                }
                if (line.Length != 0) lines.Add(line);
                for (int n = 0; n < lines.Count; n++)
                {
                    float x = Mathf.Clamp(point.x - Measure(lines[n], size) / 2, 42, 1152 - Measure(lines[n], size));
                    float y = point.y + size * (.35f + (n - (lines.Count - 1) / 2f) * 1.1f);
                    Label(x, Mathf.Clamp(y, BodyTop + size, BodyTop + BodyHeight - 2), size, "F3F9FA", lines[n]);
                }
            }
            text.Append("<voffset=0></font><font=\"LiberationSans SDF\">").Append(drawing);
            if (highlights.TryGetValue(region, out string selected)) text.Append(selected);
            return frames[(region, language)] = text.Append("</font></align></width></margin></indent></line-indent>").ToString();
        }

        private float Measure(string value, float size)
        {
            float advance = 0;
            foreach (char character in value)
                if (character >= '\u0400' && character <= '\u04FF') advance += SiteMapFontMetrics.Advance(character) * size;
                else if (font.characterLookupTable.TryGetValue(character, out var glyph)) advance += glyph.glyph.metrics.horizontalAdvance * size / font.faceInfo.pointSize;
                else advance += size * .6f;
            return advance;
        }
    }

    private static string F(float value) => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    private static Definition[] Load()
    {
        using var stream = typeof(SiteMapSublevels).Assembly.GetManifestResourceStream("Methadrenaline.Server.Map.native-room-floors.deflate")
            ?? throw new InvalidOperationException("Native floor atlas is missing.");
        using var compressed = new DeflateStream(stream, CompressionMode.Decompress);
        using var reader = new BinaryReader(compressed);
        if (reader.ReadInt32() != 2) throw new InvalidDataException("Unsupported floor atlas format.");
        int count = reader.ReadInt32();
        if (count < 0 || count > 64) throw new InvalidDataException("Invalid floor count.");
        var result = new Definition[count];
        for (int i = 0; i < count; i++)
        {
            var data = new Definition { Room = (RoomName)Enum.Parse(typeof(RoomName), reader.ReadString()),
                ReferenceY = reader.ReadSingle(), MinimumY = reader.ReadSingle(), MaximumY = reader.ReadSingle(),
                Left = reader.ReadSingle(), Top = reader.ReadSingle(), Step = reader.ReadSingle(), Width = reader.ReadInt32(), Height = reader.ReadInt32() };
            if (data.Width < 1 || data.Height < 1 || (long)data.Width * data.Height > 4000000 || data.Step <= 0)
                throw new InvalidDataException("Invalid floor dimensions.");
            int bytes = checked(data.Width * data.Height * 2);
            var cells = reader.ReadBytes(bytes);
            if (cells.Length != bytes) throw new EndOfStreamException();
            data.Cells = new ushort[data.Width * data.Height]; Buffer.BlockCopy(cells, 0, data.Cells, 0, bytes);
            data.Ink = ReadInk(reader);
            int regions = reader.ReadInt32();
            if (regions < 0 || regions > 4096) throw new InvalidDataException("Invalid floor partition count.");
            for (int region = 0; region < regions; region++) data.Regions.Add(reader.ReadUInt16(), ReadInk(reader));
            int captions = reader.ReadInt32();
            if (captions < 0 || captions > 256) throw new InvalidDataException("Invalid floor caption count.");
            for (int caption = 0; caption < captions; caption++) data.Captions.Add(new Caption {
                Region = reader.ReadUInt16(), Point = new Vector2(reader.ReadSingle(), reader.ReadSingle()), Key = reader.ReadString() });
            result[i] = data;
        }
        return result;
    }

    private static List<SiteMapInk> ReadInk(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        if (count < 0 || count > 65535) throw new InvalidDataException("Invalid floor drawing size.");
        var result = new List<SiteMapInk>(count);
        for (int i = 0; i < count; i++) result.Add(new SiteMapInk(new Rect(reader.ReadUInt16(), reader.ReadUInt16(),
            reader.ReadUInt16(), reader.ReadUInt16()), reader.ReadByte()));
        return result;
    }
}
