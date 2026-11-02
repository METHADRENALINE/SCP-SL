using System;

namespace Methadrenaline.Server.Core;

public sealed class ChaosRoundPlan
{
    private readonly double repeatDelay;

    public ChaosRoundPlan(Random random, double firstMinimum, double firstMaximum, double gapMinimum, double gapMaximum)
    {
        if (!ValidWindow(firstMinimum, firstMaximum) || !ValidWindow(gapMinimum, gapMaximum))
            throw new ArgumentOutOfRangeException(nameof(firstMinimum), "Chaos timing windows must be finite, positive and ordered.");
        PlannedCount = random.Next(1, 3);
        NextAt = Between(random, firstMinimum, firstMaximum);
        repeatDelay = Between(random, gapMinimum, gapMaximum);
    }

    public int PlannedCount { get; }
    public int StartedCount { get; private set; }
    public double? NextAt { get; private set; }
    public bool Completed => StartedCount >= PlannedCount;
    public bool IsDue(double elapsed) => NextAt.HasValue && elapsed >= NextAt.Value;

    public bool TryRecordStart(double elapsed, bool allowEarly = false)
    {
        if (double.IsNaN(elapsed) || double.IsInfinity(elapsed) || elapsed < 0 || Completed || !allowEarly && !IsDue(elapsed)) return false;
        StartedCount++;
        NextAt = Completed ? null : elapsed + repeatDelay;
        return true;
    }

    private static double Between(Random random, double minimum, double maximum)
        => minimum + random.NextDouble() * (maximum - minimum);
    private static bool ValidWindow(double minimum, double maximum)
        => !double.IsNaN(minimum) && !double.IsInfinity(minimum) && !double.IsInfinity(maximum) && minimum > 0 && maximum >= minimum;
}
