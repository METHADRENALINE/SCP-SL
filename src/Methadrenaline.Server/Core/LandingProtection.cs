namespace Methadrenaline.Server.Core;

public sealed class LandingProtection(int life)
{
    public int Life { get; } = life;
    private double? landedAt;

    public bool Update(int currentLife, bool alive, bool grounded, double now)
    {
        if (currentLife != Life || !alive) return false;
        if (grounded && !landedAt.HasValue) landedAt = now;
        return !landedAt.HasValue || now - landedAt.Value < 0.5;
    }
}
