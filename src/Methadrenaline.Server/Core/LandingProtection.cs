namespace Methadrenaline.Server.Core;

/// <summary>Protect the first landing, even after a very long airborne descent.</summary>
public sealed class LandingProtection(int life)
{
    public int Life { get; } = life;
    private double? landedAt;

    public bool Update(int currentLife, bool alive, bool grounded, double now)
    {
        if (currentLife != Life || !alive) return false;
        if (grounded && !landedAt.HasValue) landedAt = now;
        // Native fall damage is evaluated on the update after ground contact.
        return !landedAt.HasValue || now - landedAt.Value < 0.5;
    }
}
