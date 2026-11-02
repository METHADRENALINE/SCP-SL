using System;

namespace Methadrenaline.Server.Core;

public static class AnnouncementPolicy
{
    // A dedicated server may not load the client TTS database. In that case the
    // fixed native-formatted cue is still valid, but its duration is unknown here.
    public static bool IsSafe(double? seconds)
        => !seconds.HasValue || !double.IsNaN(seconds.Value) && !double.IsInfinity(seconds.Value) && seconds.Value > 0 && seconds.Value <= 12;
}
