using Methadrenaline.Server;
using Methadrenaline.Server.Core;
using Xunit;

namespace Methadrenaline.Server.Tests;

public sealed class AnomalyTests
{
    [Theory]
    [InlineData("levitation", "gravity", false)]
    [InlineData("gravity", "levitation", false)]
    [InlineData("levitation", "levitation", false)]
    [InlineData("blackout", "blackout", false)]
    [InlineData("levitation", "blackout", true)]
    [InlineData("doors", "levitation", true)]
    [InlineData("signals", "doors", true)]
    public void EffectsOverlapButGravityOwnersAndDuplicateEffectsDoNot(string incoming, string running, bool allowed)
        => Assert.Equal(allowed, AnomalyPolicy.CanStart(incoming, new[] { running }, 3));

    [Fact]
    public void FourthEffectCannotExceedTheConcurrencyLimit()
    {
        Assert.False(AnomalyPolicy.CanStart("signals", new[] { "levitation", "blackout", "doors" }, 3));
        Assert.True(AnomalyPolicy.CanStart("signals", new[] { "blackout", "doors" }, 3));
        Assert.False(AnomalyPolicy.CanStart("unknown", Array.Empty<string>(), 3));
    }

    [Fact]
    public void MaximumChaosSupportsFourDistinctEffectsWithOneGravityOwner()
    {
        Assert.True(AnomalyPolicy.CanStart("signals", new[] { "levitation", "blackout", "doors" }, 4));
        Assert.False(AnomalyPolicy.CanStart("gravity", new[] { "levitation", "blackout", "doors" }, 4));
        Assert.False(AnomalyPolicy.CanStart("doors", new[] { "levitation", "blackout", "doors" }, 4));
    }

    [Fact]
    public void LevitationAlwaysLastsTwentySecondsAndRetainsNormalFallDamage()
    {
        Assert.Equal(20, AnomalyPolicy.Duration("levitation", 5));
        Assert.Equal(20, AnomalyPolicy.Duration("levitation", 40));
        Assert.False(AnomalyPolicy.ProtectsFromFall("levitation"));
        Assert.True(AnomalyPolicy.ProtectsFromFall("gravity"));
        Assert.True(AnomalyPolicy.ProtectsFromFall("levitation", fromChaos: true));
        Assert.Equal(20, AnomalyPolicy.Duration("levitation", 22));
        Assert.Equal(22, AnomalyPolicy.Duration("blackout", 22));
    }

    [Fact]
    public void LevitationAcceleratesUpwardsWhileLunarGravityRemainsDownward()
    {
        Assert.Equal(12, AnomalyPolicy.GravityY("levitation", -19.6f, 0.5f, 12));
        Assert.Equal(12, AnomalyPolicy.GravityY("levitation", 0, 0.5f, 12));
        Assert.Equal(-9.8f, AnomalyPolicy.GravityY("gravity", -19.6f, 0.5f, 12));
    }

    [Fact]
    public void ChaosLandingProtectionSurvivesLongFallsAndExpiresAfterTheFirstLanding()
    {
        var protection = new LandingProtection(7);
        Assert.True(protection.Update(7, true, false, 0));
        Assert.True(protection.Update(7, true, false, 45));
        Assert.True(protection.Update(7, true, true, 46));
        Assert.True(protection.Update(7, true, true, 46.49));
        Assert.False(protection.Update(7, true, false, 46.5));
    }

    [Fact]
    public void LandingProtectionCannotLeakToANewLife()
    {
        var protection = new LandingProtection(7);
        Assert.False(protection.Update(8, true, false, 1));
        Assert.False(protection.Update(7, false, false, 1));
    }

    [Fact]
    public void FinishingOneEffectDoesNotUndoAnotherActiveEffect()
    {
        bool dark = false, doorsOpen = false;
        var failures = new List<Exception>();
        var blackout = new RestorationScope(failures.Add);
        var doors = new RestorationScope(failures.Add);
        blackout.Add(() => dark = false);
        doors.Add(() => doorsOpen = false);
        dark = doorsOpen = true;
        blackout.Dispose();
        Assert.False(dark);
        Assert.True(doorsOpen);
        doors.Dispose();
        Assert.False(doorsOpen);
        Assert.Empty(failures);
    }

    [Fact]
    public void ChaosBundleUsesFourCompatibleEffectsAndAlsoWorksForASoloPlayer()
    {
        var config = new Config();
        config.Validate();
        Assert.Equal(1, config.MinimumEventPlayers);
        var running = new List<string>();
        foreach (string name in AnomalyPolicy.ChaosEffects)
        {
            Assert.True(AnomalyPolicy.CanStart(name, running, 4));
            running.Add(name);
        }
        Assert.Equal(new[] { "levitation", "blackout", "doors", "signals" }, running);
        Assert.True(config.MinimumChaosGap > config.EventDuration);
        Assert.True(config.FirstChaosMaximumDelay >= config.FirstChaosMinimumDelay);
        Assert.True(config.MaximumAffectedDoors > 4);
        Assert.True(config.MaximumAffectedRooms > 3);
    }

    [Fact]
    public void InvalidChaosConfigCannotCreateTightLoopsOrUnboundedMutations()
    {
        var config = new Config
        {
            MinimumChaosGap = 0, MaximumChaosGap = float.NaN,
            FirstChaosMinimumDelay = float.PositiveInfinity, FirstChaosMaximumDelay = float.NaN,
            DoorPulseInterval = 0, SignalPulseInterval = 0, LevitationAcceleration = float.NaN,
            MaximumAffectedDoors = 999, MaximumAffectedRooms = 999, MinimumEventPlayers = 0
        };
        config.Validate();
        Assert.True(config.MaximumChaosGap >= config.MinimumChaosGap);
        Assert.True(config.MinimumChaosGap > config.EventDuration);
        Assert.True(config.FirstChaosMaximumDelay >= config.FirstChaosMinimumDelay);
        Assert.InRange(config.DoorPulseInterval, 1, 10);
        Assert.InRange(config.SignalPulseInterval, 3, 15);
        Assert.InRange(config.LevitationAcceleration, 3, 25);
        Assert.InRange(config.MaximumAffectedDoors, 1, 32);
        Assert.InRange(config.MaximumAffectedRooms, 1, 16);
        Assert.Equal(1, config.MinimumEventPlayers);
    }
}
