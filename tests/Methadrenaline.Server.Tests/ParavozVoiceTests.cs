using Methadrenaline.Server.Core;
using Xunit;

namespace Methadrenaline.Server.Tests;

public sealed class ParavozVoiceTests
{
    private static float[] Tone(double frequency, double amplitude, int count = 48000)
        => Enumerable.Range(0, count).Select(i => (float)(amplitude * Math.Sin(2 * Math.PI * frequency * i / 48000))).ToArray();
    private static double Rms(float[] samples) => Math.Sqrt(samples.Skip(samples.Length / 2).Average(x => (double)x * x));

    [Fact]
    public void QuietVoiceIsStronglyAmplified()
    {
        var samples = Tone(1000, 0.01);
        double before = Rms(samples);
        new ParavozVoiceDsp().Process(samples, samples.Length);
        Assert.True(Rms(samples) / before > 12);
    }

    [Fact]
    public void BassIsBoostedFarMoreThanUpperVoiceFrequencies()
    {
        var bass = Tone(100, 0.0001);
        var treble = Tone(2000, 0.0001);
        new ParavozVoiceDsp().Process(bass, bass.Length);
        new ParavozVoiceDsp().Process(treble, treble.Length);
        Assert.True(Rms(bass) / Rms(treble) > 6);
    }

    [Fact]
    public void HeavyOverdriveStaysFiniteAndWithinPcmRange()
    {
        var samples = Tone(100, 100);
        new ParavozVoiceDsp().Process(samples, samples.Length);
        Assert.All(samples, x => Assert.InRange(x, -ParavozVoiceDsp.Ceiling, ParavozVoiceDsp.Ceiling));
        Assert.True(Rms(samples) > 0.9);
    }

    [Fact]
    public void PacketBoundariesDoNotChangeFilterOutput()
    {
        var whole = Tone(113, 0.003);
        var source = (float[])whole.Clone();
        new ParavozVoiceDsp().Process(whole, whole.Length);
        var streamed = new List<float>();
        var effect = new ParavozVoiceDsp();
        for (int offset = 0; offset < source.Length; offset += 480)
        {
            var packet = source.Skip(offset).Take(480).ToArray();
            effect.Process(packet, packet.Length);
            streamed.AddRange(packet);
        }
        Assert.Equal(whole, streamed.ToArray());
    }

    [Fact]
    public void ResetAndSeparateSpeakersCannotLeakPreviousAudio()
    {
        var effect = new ParavozVoiceDsp();
        var samples = Tone(100, 0.5, 480);
        effect.Process(samples, samples.Length);
        effect.Reset();
        var silence = new float[480];
        effect.Process(silence, silence.Length);
        Assert.All(silence, x => Assert.Equal(0, x));
        new ParavozVoiceDsp().Process(silence, silence.Length);
        Assert.All(silence, x => Assert.Equal(0, x));
    }

    [Fact]
    public void InvalidSamplesRecoverWithoutPoisoningTheStream()
    {
        var samples = new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0.01f, -0.01f };
        new ParavozVoiceDsp().Process(samples, samples.Length);
        Assert.All(samples, x => Assert.True(float.IsFinite(x)));
        Assert.NotEqual(0, samples[3]);
    }

    [Fact]
    public void UnusedPacketTailIsNotProcessed()
    {
        var samples = new[] { 0f, 0.01f, 123f };
        new ParavozVoiceDsp().Process(samples, 2);
        Assert.Equal(123f, samples[2]);
    }
}
