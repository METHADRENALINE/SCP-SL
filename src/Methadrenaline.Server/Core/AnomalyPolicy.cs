using System;
using System.Collections.Generic;
using System.Linq;

namespace Methadrenaline.Server.Core;

public static class AnomalyPolicy
{
    public const float LevitationDuration = 20f;
    public static readonly IReadOnlyList<string> ChaosEffects = Array.AsReadOnly(new[] { "levitation", "blackout", "doors", "signals" });
    public static readonly string[] Names = { "gravity", "levitation", "blackout", "doors", "signals" };

    public static bool CanStart(string name, IEnumerable<string> active, int limit)
    {
        var running = active.ToArray();
        return Names.Contains(name) && running.Length < limit &&
            !running.Any(other => other == name || IsGravity(other) && IsGravity(name));
    }

    public static bool IsGravity(string name) => name is "gravity" or "levitation";
    public static bool ProtectsFromFall(string name, bool fromChaos = false) => name == "gravity" || fromChaos && name == "levitation";
    public static float Duration(string name, float normalDuration)
        => name == "levitation" ? LevitationDuration : normalDuration;
    public static float GravityY(string name, float original, float multiplier, float liftAcceleration)
        => name == "levitation" ? Math.Abs(liftAcceleration) : original * multiplier;
}
