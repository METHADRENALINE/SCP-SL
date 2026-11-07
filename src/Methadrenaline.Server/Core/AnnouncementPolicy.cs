using System;

namespace Methadrenaline.Server.Core;

public static class AnnouncementPolicy
{
    public static bool IsSafe(double? seconds)
        => !seconds.HasValue || !double.IsNaN(seconds.Value) && !double.IsInfinity(seconds.Value) && seconds.Value > 0 && seconds.Value <= 12;
}
