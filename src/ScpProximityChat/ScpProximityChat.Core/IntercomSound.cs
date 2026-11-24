namespace ScpProximityChat.Core;

internal sealed class IntercomSound
{
    private const int FirstEcho = 3600;
    private const int SecondEcho = 6000;
    private readonly float[] delay = new float[SecondEcho];
    private int cursor;

    internal void Reset()
    {
        Array.Clear(delay, 0, delay.Length);
        cursor = 0;
    }

    internal void Process(float[] samples, int count)
    {
        int firstTap = cursor - FirstEcho;
        if (firstTap < 0) firstTap += delay.Length;
        for (int i = 0; i < count; i++)
        {
            float dry = 1.9f * samples[i];
            float output = dry + .19f * delay[firstTap] + .09f * delay[cursor];
            delay[cursor] = dry;
            if (++firstTap == delay.Length) firstTap = 0;
            if (++cursor == delay.Length) cursor = 0;

            float magnitude = Math.Abs(output);
            samples[i] = magnitude <= .78f ? output :
                Math.Sign(output) * (.78f + .19f * (magnitude - .78f) / (magnitude - .55f));
        }
    }
}
