using System;

namespace Methadrenaline.Server.Core;

public static class BraceletSignal
{
    public const int SampleRate = 48000;

    // Original short electronic cues, generated once as mono PCM. No downloads,
    // external audio files, transcoding processes or playback loops are needed.
    public static float[] CreateConfirmation() => ToneTrain(1, 1, 1, 880, 0.14, 0.035);

    public static float[] CreateWarning(int level)
    {
        return level switch
        {
            1 => ToneTrain(3, 0.30, 0.09, 1100),
            2 => ToneTrain(3, 0.22, 0.08, 1250),
            3 => ToneTrain(4, 0.16, 0.07, 1450),
            4 => ToneTrain(6, 0.12, 0.06, 1600),
            5 => ToneTrain(8, 0.08, 0.055, 1850),
            _ => throw new ArgumentOutOfRangeException(nameof(level))
        };
    }

    private static float[] ToneTrain(int count, double interval, double toneLength, double frequency, double peak = 0.18, double fade = 0.005)
    {
        var samples = new float[(int)Math.Ceiling(SampleRate * ((count - 1) * interval + toneLength + 0.01))];
        for (int beep = 0; beep < count; beep++)
        {
            int start = (int)(beep * interval * SampleRate);
            int length = (int)(toneLength * SampleRate);
            for (int i = 0; i < length; i++)
            {
                double t = i / (double)SampleRate;
                double envelope = Math.Min(1, Math.Min(t / fade, (toneLength - t) / fade));
                samples[start + i] = (float)(peak * envelope * Math.Sin(2 * Math.PI * frequency * t));
            }
        }
        return samples;
    }

    public static float[] CreateSequence()
    {
        var samples = new float[(int)(SampleRate * BraceletPolicy.SequenceSeconds)];
        for (int i = 0; i < samples.Length; i++)
        {
            double t = i / (double)SampleRate;
            bool detonating = t >= BraceletPolicy.FillingSeconds;
            double phaseTime = detonating ? t - BraceletPolicy.FillingSeconds : t;
            double rate = detonating ? 28 : 12;
            double onTime = (detonating ? 0.78 : 0.62) / rate;
            double pulseTime = phaseTime % (1 / rate);
            if (pulseTime >= onTime) continue;
            double envelope = Math.Min(1, Math.Min(pulseTime / 0.002, (onTime - pulseTime) / 0.002));
            envelope *= Math.Min(1, Math.Min(t / 0.005, (BraceletPolicy.SequenceSeconds - t) / 0.005));
            // Alternating pitches create a frantic alarm. The second phase has
            // more than twice the pulse rate, with a rising carrier underneath.
            double pitchPhase = 2 * Math.PI * ((detonating ? 2100 : 1500) * phaseTime + 50 * phaseTime * phaseTime);
            double warble = 3 * Math.Sin(2 * Math.PI * 8 * phaseTime);
            samples[i] = (float)(0.24 * envelope * Math.Sin(pitchPhase + warble));
        }
        return samples;
    }
}
