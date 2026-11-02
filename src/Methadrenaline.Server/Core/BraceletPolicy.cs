using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Methadrenaline.Server.Core;

public static class BraceletPolicy
{
    public const double YellowDistance = 7.62;
    public const double OrangeDistance = 15.24;
    public const double DetonationDistance = 22.86;
    public const double DeathNoticeSeconds = 10;
    public const double FillingSeconds = 3;
    public const double SequenceSeconds = 7;

    public const double WarningRearmDistance = 0.5;
    public const double WarningCooldown = 1;
    public static double WarningDistance(int level) => level switch
    {
        1 => 10, 2 => 15, 3 => 18, 4 => 20, 5 => 22,
        _ => throw new ArgumentOutOfRangeException(nameof(level))
    };
    public static int WarningCrossing(double previous, double current)
    {
        for (int level = 5; level >= 1; level--)
            if (previous < WarningDistance(level) && current >= WarningDistance(level)) return level;
        return 0;
    }
    public static string FormatDistance(double distance, ServerLanguage language)
        => distance.ToString("0.00", language == ServerLanguage.English ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(language == ServerLanguage.Ukrainian ? "uk-UA" : "ru-RU"));

    public static bool MustDetonate(double distance) => distance >= DetonationDistance;

    public static string DistanceColor(double distance)
    {
        if (double.IsNaN(distance)) distance = 0;
        double t = Math.Max(0, Math.Min(1, (distance - YellowDistance) / (OrangeDistance - YellowDistance)));
        int green = (int)Math.Round(221 + (136 - 221) * t);
        if (distance > OrangeDistance)
            green = (int)Math.Round(136 * Math.Max(0, 1 - (distance - OrangeDistance) / (DetonationDistance - OrangeDistance)));
        return "#FF" + green.ToString("X2") + "00";
    }

    public static string SafeName(string name) => new string((name ?? "").Select(c => char.IsControl(c) || c == '\u2028' || c == '\u2029' ? ' ' : c == '<' ? '‹' : c == '>' ? '›' : c).Take(64).ToArray());
}

public readonly struct BraceletLife(uint networkId, int lifeId) : IEquatable<BraceletLife>
{
    public uint NetworkId { get; } = networkId;
    public int LifeId { get; } = lifeId;
    public bool Equals(BraceletLife other) => NetworkId == other.NetworkId && LifeId == other.LifeId;
    public override bool Equals(object? other) => other is BraceletLife life && Equals(life);
    public override int GetHashCode() => unchecked((int)NetworkId * 397 ^ LifeId);
}

public enum BraceletPhase { Armed, Filling, Detonating, Due }

public sealed class BraceletBinding(BraceletLife prisoner, BraceletLife captor, string prisonerName, double distance = 0)
{
    public BraceletLife Prisoner { get; } = prisoner;
    public BraceletLife Captor { get; } = captor;
    public string PrisonerName { get; } = prisonerName;
    public double PreviousDistance { get; private set; } = distance;
    public double? SequenceStartedAt { get; private set; }
    public bool Committed => SequenceStartedAt.HasValue;
    private readonly bool[] warned = new bool[5];
    private readonly double[] lastWarningAt = Enumerable.Repeat(double.NegativeInfinity, 5).ToArray();

    public int ObserveDistance(double current, double now)
    {
        for (int i = 0; i < warned.Length; i++)
            if (current <= BraceletPolicy.WarningDistance(i + 1) - BraceletPolicy.WarningRearmDistance) warned[i] = false;
        int warning = Committed ? 0 : BraceletPolicy.WarningCrossing(PreviousDistance, current);
        PreviousDistance = current;
        if (warning == 0 || warned[warning - 1] || now - lastWarningAt[warning - 1] < BraceletPolicy.WarningCooldown) return 0;
        // A teleport plays only the highest crossed warning, never five cues.
        for (int i = 0; i < warning; i++) { warned[i] = true; lastWarningAt[i] = now; }
        return warning;
    }

    public bool BeginSequence(double now)
    {
        if (Committed) return false;
        SequenceStartedAt = now;
        return true;
    }

    public BraceletPhase Phase(double now)
    {
        if (!SequenceStartedAt.HasValue) return BraceletPhase.Armed;
        double elapsed = now - SequenceStartedAt.Value;
        return elapsed >= BraceletPolicy.SequenceSeconds ? BraceletPhase.Due
            : elapsed >= BraceletPolicy.FillingSeconds ? BraceletPhase.Detonating : BraceletPhase.Filling;
    }
}

public sealed class BraceletDeathNotice(BraceletBinding binding, double expiresAt)
{
    public BraceletBinding Binding { get; } = binding;
    public double ExpiresAt { get; } = expiresAt;
}

/// <summary>Life-scoped ownership and one-shot detonation, independent of Unity.</summary>
public sealed class BraceletLedger
{
    private readonly Dictionary<BraceletLife, BraceletBinding> active = new();
    private readonly List<BraceletDeathNotice> notices = new();
    public IReadOnlyCollection<BraceletBinding> Active => active.Values;
    public IReadOnlyList<BraceletDeathNotice> Notices => notices;

    public bool Bind(BraceletLife prisoner, BraceletLife captor, string prisonerName, double distance = 0)
    {
        if (prisoner.NetworkId == captor.NetworkId || (TryGet(prisoner, out var existing) && existing.Committed)) return false;
        active[prisoner] = new BraceletBinding(prisoner, captor, prisonerName, distance);
        return true;
    }

    public bool TryGet(BraceletLife prisoner, out BraceletBinding binding)
    {
        if (active.TryGetValue(prisoner, out var found)) { binding = found; return true; }
        binding = null!;
        return false;
    }
    public bool Release(BraceletLife prisoner) => TryGet(prisoner, out var binding) && !binding.Committed && active.Remove(prisoner);
    public void ReleaseCaptor(BraceletLife captor)
    {
        foreach (var key in active.Where(p => p.Value.Captor.Equals(captor) && !p.Value.Committed).Select(p => p.Key).ToArray()) active.Remove(key);
        notices.RemoveAll(n => n.Binding.Captor.Equals(captor));
    }

    public bool Complete(BraceletLife prisoner, double now, out BraceletBinding binding)
    {
        if (!TryGet(prisoner, out binding)) return false;
        active.Remove(prisoner);
        notices.Add(new BraceletDeathNotice(binding, now + BraceletPolicy.DeathNoticeSeconds));
        return true;
    }

    public void Prune(double now) => notices.RemoveAll(n => now >= n.ExpiresAt);
    public void ReleaseAll()
    {
        foreach (var key in active.Where(p => !p.Value.Committed).Select(p => p.Key).ToArray()) active.Remove(key);
    }
    public void Reset() { active.Clear(); notices.Clear(); }
}
