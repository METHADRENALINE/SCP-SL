using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace Methadrenaline.Server;

internal sealed class SiteMapIcon
{
    private static readonly Dictionary<string, SiteMapIcon> Icons = Load();
    internal readonly int Size;
    internal readonly float LabelX, LabelY;
    private readonly byte[] alpha;

    private SiteMapIcon(int size, float labelX, float labelY, byte[] pixels)
    { Size = size; LabelX = labelX; LabelY = labelY; alpha = pixels; }

    internal static SiteMapIcon Get(string name) => Icons.TryGetValue(name, out var icon) ? icon :
        throw new InvalidOperationException("Missing original SCP-106 map icon: " + name);

    internal static bool Contains(string name) => Icons.ContainsKey(name);

    internal float Sample(float x, float y)
    {
        if (x < -.0001f || y < -.0001f || x > 1.0001f || y > 1.0001f) return 0;
        x = x * Size - .5f; y = y * Size - .5f;
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
        float dx = x - ix, dy = y - iy;
        float At(int px, int py) => alpha[Math.Min(Size - 1, Math.Max(0, py)) * Size + Math.Min(Size - 1, Math.Max(0, px))];
        return (At(ix, iy) * (1 - dx) + At(ix + 1, iy) * dx) * (1 - dy) +
               (At(ix, iy + 1) * (1 - dx) + At(ix + 1, iy + 1) * dx) * dy;
    }

    private static Dictionary<string, SiteMapIcon> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "Methadrenaline.Server.Map.native-room-outlines.deflate") ?? throw new InvalidOperationException("Native map outlines are missing.");
        using var deflate = new DeflateStream(stream, CompressionMode.Decompress);
        using var reader = new BinaryReader(deflate);
        var result = new Dictionary<string, SiteMapIcon>(StringComparer.Ordinal);
        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            string name = reader.ReadString(); int size = reader.ReadUInt16();
            float x = reader.ReadSingle(), y = reader.ReadSingle();
            byte[] alpha = reader.ReadBytes(size * size);
            if (alpha.Length != size * size) throw new InvalidDataException("Truncated native map icon.");
            result.Add(name, new SiteMapIcon(size, x, y, alpha));
        }
        return result;
    }
}
