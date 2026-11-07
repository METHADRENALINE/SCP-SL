using System;
using System.Collections.Generic;
using UnityEngine;

namespace Methadrenaline.Server;

internal readonly struct SiteMapInk
{
    internal readonly Rect Bounds;
    internal readonly byte Alpha;
    internal SiteMapInk(Rect bounds, byte alpha) { Bounds = bounds; Alpha = alpha; }
}

internal static class SiteMapRaster
{
    internal const int Density = 2;
    private static byte Quantize(byte alpha) => alpha < 16 ? (byte)0 : alpha < 64 ? (byte)33 :
        alpha < 128 ? (byte)94 : alpha < 192 ? (byte)160 : (byte)255;

    internal static void Stamp(byte[] pixels, int width, int height, SiteMapIcon icon, Vector2 center,
        float side, float yaw)
    {
        float angle = yaw * Mathf.Deg2Rad, cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
        float radius = side * .708f;
        int left = Math.Max(0, (int)Math.Floor((center.x - radius) * Density));
        int right = Math.Min(width, (int)Math.Ceiling((center.x + radius) * Density));
        int top = Math.Max(0, (int)Math.Floor((center.y - radius) * Density));
        int bottom = Math.Min(height, (int)Math.Ceiling((center.y + radius) * Density));
        for (int y = top; y < bottom; y++)
        for (int x = left; x < right; x++)
        {
            float dx = ((x + .5f) / Density - center.x) / side;
            float dy = ((y + .5f) / Density - center.y) / side;
            float alpha = icon.Sample(cos * dx + sin * dy + .5f, -sin * dx + cos * dy + .5f);
            int index = y * width + x;
            pixels[index] = (byte)Math.Max(pixels[index], Math.Min(255, (int)Math.Round(alpha)));
        }
    }

    internal static List<SiteMapInk> Rectangles(byte[] pixels, int width, int height)
    {
        var result = new List<SiteMapInk>();
        var active = new Dictionary<(int X, int End, byte Alpha), Rect>();
        for (int y = 0; y < height; y++)
        {
            var next = new Dictionary<(int, int, byte), Rect>();
            int x = 0;
            while (x < width)
            {
                int start = x;
                byte alpha = Quantize(pixels[y * width + x]);
                do { x++; } while (x < width && Quantize(pixels[y * width + x]) == alpha);
                if (alpha == 0) continue;
                var key = (start, x, alpha);
                if (active.TryGetValue(key, out var previous))
                { previous.height += 1f / Density; next.Add(key, previous); active.Remove(key); }
                else next.Add(key, new Rect((float)start / Density, (float)y / Density, (float)(x - start) / Density, 1f / Density));
            }
            foreach (var pair in active) result.Add(new SiteMapInk(pair.Value, pair.Key.Alpha));
            active = next;
        }
        foreach (var pair in active) result.Add(new SiteMapInk(pair.Value, pair.Key.Alpha));
        return result;
    }
}
