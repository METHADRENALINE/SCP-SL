using System;
using System.Collections.Generic;
using System.IO;

namespace Methadrenaline.Server;

internal static class SiteMapFontMetrics
{
    // The client's first Cyrillic-capable global fallback is NotoSansCJKsc-RegularDynamic.
    // Unsupported Cyrillic characters use Noto Sans; using the CJK missing-glyph
    // advance for Ukrainian letters made those captions needlessly small.
    // Values are in em units. The dedicated build cannot populate the client atlas.
    private static readonly Dictionary<char, float> Advances = Load();

    internal static float Advance(char character) => Advances.TryGetValue(character, out var width) ? width :
        throw new InvalidOperationException("No native map font metric for U+" + ((int)character).ToString("X4"));

    private static Dictionary<char, float> Load()
    {
        using var stream = typeof(SiteMapFontMetrics).Assembly.GetManifestResourceStream(
            "Methadrenaline.Server.Map.native-cyrillic-advances.bin") ?? throw new InvalidOperationException("Native map font metrics are missing.");
        using var reader = new BinaryReader(stream);
        int count = reader.ReadInt32();
        var result = new Dictionary<char, float>(count);
        for (int i = 0; i < count; i++) result.Add((char)reader.ReadUInt16(), reader.ReadSingle());
        return result;
    }
}
