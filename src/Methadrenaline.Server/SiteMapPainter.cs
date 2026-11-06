using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using TMPro;

namespace Methadrenaline.Server;

internal static class SiteMapPainter
{
    internal static string Paint(TMP_FontAsset font, IEnumerable<SiteMapInk> rectangles, string color,
        int offsetX = 0, int offsetY = 0)
    {
        var text = new StringBuilder("<line-height=0>");
        var face = font.faceInfo;
        var glyph = font.characterLookupTable[46].glyph.metrics;
        float padding = ShaderUtilities.GetPadding(font.material, false, false);
        int fills = 0;
        foreach (var height in rectangles.GroupBy(part => part.Bounds.height))
        {
            double size = Math.Round(height.Key * face.pointSize / (face.ascentLine - face.descentLine), 2);
            double scale = size / face.pointSize;
            text.Append("<size=").Append(F(size)).Append('>');
            foreach (var width in height.GroupBy(part => part.Bounds.width))
            {
                double stretch = (width.Key / scale - padding * 2) / glyph.width;
                double spacing = ((glyph.horizontalBearingX + glyph.width - glyph.horizontalAdvance) * stretch + padding) * scale;
                text.Append("<scale=").Append(stretch.ToString("0.####", CultureInfo.InvariantCulture)).Append('>');
                if (color == "0B141D") text.Append("<cspace=").Append(F(spacing)).Append('>');
                foreach (var part in width)
                {
                    var rect = part.Bounds;
                    text.Append("<alpha=#FF><mark=#").Append(color).Append(part.Alpha.ToString("X2"))
                        .Append("><alpha=#00><voffset=").Append(F(-offsetY - rect.y - face.ascentLine * scale))
                        .Append("><pos=").Append(F(offsetX + rect.x - (glyph.horizontalBearingX * stretch - padding) * scale))
                        .Append(">.</mark>.");
                    if (++fills % 32 == 0) text.Append('\n');
                }
            }
            text.Append("</size>");
        }
        return text.Append("</scale></cspace><voffset=0><alpha=#FF>").ToString();
    }

    private static string F(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
