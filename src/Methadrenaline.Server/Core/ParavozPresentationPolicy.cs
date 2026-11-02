using System;
using System.Collections.Generic;

namespace Methadrenaline.Server.Core;

public static class ParavozPresentationPolicy
{
    public const int SampleRate = 48000;
    public const float MusicVolume = 0.6f;
    public const float MusicNear = 10;
    public const float MusicRange = 70;
    public const int EmberCount = 4;

    public static uint? Select(uint viewer, uint? previous, IReadOnlyDictionary<uint, double> distances)
    {
        bool Audible(double d) => !double.IsNaN(d) && !double.IsInfinity(d) && d >= 0 && d <= MusicRange * MusicRange;
        if (distances.TryGetValue(viewer, out double own) && Audible(own)) return viewer;
        uint? best = null;
        double bestDistance = double.PositiveInfinity;
        foreach (var pair in distances)
        {
            if (!Audible(pair.Value) || pair.Value > bestDistance || pair.Value == bestDistance && best.HasValue && pair.Key >= best.Value) continue;
            best = pair.Key;
            bestDistance = pair.Value;
        }
        if (!best.HasValue) return null;
        if (previous.HasValue && distances.TryGetValue(previous.Value, out double current) && Audible(current) &&
            Math.Sqrt(current) <= Math.Sqrt(bestDistance) + 0.75) return previous;
        return best;
    }

    // Blend the end into the beginning, keeping a whole number of native 10ms
    // packets. AudioPlayerApi otherwise pads the final partial packet with zeros.
    public static float[] LoopSamples(float[] samples)
    {
        const int packet = 480;
        int blend = 2400 + samples.Length % packet;
        if (samples.Length < blend * 2 + packet) throw new ArgumentException("Music clip is too short to loop.");
        var output = new float[samples.Length - blend];
        Array.Copy(samples, blend, output, 0, samples.Length - blend * 2);
        for (int i = 0; i < blend; i++)
        {
            float mix = (float)i / (blend - 1);
            output[output.Length - blend + i] = samples[samples.Length - blend + i] * (1 - mix) + samples[i] * mix;
        }
        return output;
    }
}
