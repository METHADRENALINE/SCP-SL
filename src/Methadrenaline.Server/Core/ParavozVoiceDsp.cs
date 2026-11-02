using System;

namespace Methadrenaline.Server.Core;

/// <summary>Stateful 48 kHz mono bass shelf and deliberately overdriven voice.</summary>
public sealed class ParavozVoiceDsp
{
    public const int SampleRate = 48000;
    public const double Gain = 16;
    public const double BassBoostDb = 18;
    public const float Ceiling = 0.98f;
    private readonly double b0, b1, b2, a1, a2;
    private readonly double dcDecay = Math.Exp(-2 * Math.PI * 25 / SampleRate);
    private double z1, z2, previousInput, previousHighPass;

    public ParavozVoiceDsp()
    {
        // RBJ low shelf, 250 Hz, slope 1. Keep filter state across packets;
        // restarting it every 10 ms would introduce clicks and lose the bass.
        double amplitude = Math.Pow(10, BassBoostDb / 40);
        double omega = 2 * Math.PI * 250 / SampleRate;
        double cosine = Math.Cos(omega);
        double alpha = Math.Sin(omega) / Math.Sqrt(2);
        double beta = 2 * Math.Sqrt(amplitude) * alpha;
        double denominator = amplitude + 1 + (amplitude - 1) * cosine + beta;
        b0 = amplitude * (amplitude + 1 - (amplitude - 1) * cosine + beta) / denominator;
        b1 = 2 * amplitude * (amplitude - 1 - (amplitude + 1) * cosine) / denominator;
        b2 = amplitude * (amplitude + 1 - (amplitude - 1) * cosine - beta) / denominator;
        a1 = -2 * (amplitude - 1 + (amplitude + 1) * cosine) / denominator;
        a2 = (amplitude + 1 + (amplitude - 1) * cosine - beta) / denominator;
    }

    public void Reset() => z1 = z2 = previousInput = previousHighPass = 0;

    public void Process(float[] samples, int count)
    {
        if (samples == null) throw new ArgumentNullException(nameof(samples));
        if (count < 0 || count > samples.Length) throw new ArgumentOutOfRangeException(nameof(count));
        for (int i = 0; i < count; i++)
        {
            double input = samples[i];
            if (double.IsNaN(input) || double.IsInfinity(input))
            {
                Reset();
                samples[i] = 0;
                continue;
            }
            // Remove DC/subsonic offset before the strong bass amplification.
            double highPass = input - previousInput + dcDecay * previousHighPass;
            previousInput = input;
            previousHighPass = highPass;
            double filtered = b0 * highPass + z1;
            z1 = b1 * highPass - a1 * filtered + z2;
            z2 = b2 * highPass - a2 * filtered;
            samples[i] = (float)(Ceiling * Math.Tanh(filtered * Gain));
        }
    }
}
