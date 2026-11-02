using Methadrenaline.Server.Core;
using Xunit;

namespace Methadrenaline.Server.Tests;

public sealed class BraceletTests
{
    private static readonly BraceletLife Captor = new(1, 10);
    private static readonly BraceletLife Prisoner = new(2, 20);

    [Theory]
    [InlineData(0, false)]
    [InlineData(7.62, false)]
    [InlineData(15.24, false)]
    [InlineData(22.859999, false)]
    [InlineData(22.86, true)]
    [InlineData(100, true)]
    public void FatalBoundaryIsExactlySeventyFiveFeet(double distance, bool fatal)
        => Assert.Equal(fatal, BraceletPolicy.MustDetonate(distance));

    [Theory]
    [InlineData(0, "#FFDD00")]
    [InlineData(7.62, "#FFDD00")]
    [InlineData(11.43, "#FFB200")]
    [InlineData(15.24, "#FF8800")]
    [InlineData(19.05, "#FF4400")]
    [InlineData(22.86, "#FF0000")]
    [InlineData(100, "#FF0000")]
    public void HudColorInterpolatesBetweenFilmThresholds(double distance, string color)
        => Assert.Equal(color, BraceletPolicy.DistanceColor(distance));

    [Fact]
    public void CompletionIsOneShotAndDeathNoticeLastsExactlyTenSeconds()
    {
        var ledger = new BraceletLedger();
        ledger.Bind(Prisoner, Captor, "Prisoner");
        Assert.True(ledger.Complete(Prisoner, 5, out var binding));
        Assert.Equal(Captor, binding.Captor);
        Assert.False(ledger.Complete(Prisoner, 5, out _));
        Assert.Empty(ledger.Active);
        ledger.Prune(14.999);
        Assert.Single(ledger.Notices);
        ledger.Prune(15);
        Assert.Empty(ledger.Notices);
    }

    [Fact]
    public void ReleaseCannotDetonateAndCreatesNoEliminatedNotice()
    {
        var ledger = new BraceletLedger();
        ledger.Bind(Prisoner, Captor, "Prisoner");
        Assert.True(ledger.Release(Prisoner));
        Assert.False(ledger.Complete(Prisoner, 1, out _));
        Assert.Empty(ledger.Notices);
    }

    [Fact]
    public void CaptorDeathReleasesAllTheirPrisonersOnly()
    {
        var ledger = new BraceletLedger();
        ledger.Bind(Prisoner, Captor, "A");
        ledger.Bind(new(3, 30), Captor, "B");
        ledger.Bind(new(4, 40), new(5, 50), "C");
        ledger.ReleaseCaptor(Captor);
        Assert.Equal("C", Assert.Single(ledger.Active).PrisonerName);
        Assert.Empty(ledger.Notices);
    }

    [Fact]
    public void ANewLifeAndCaptorTransferNeverReuseAnOldBinding()
    {
        var ledger = new BraceletLedger();
        ledger.Bind(Prisoner, Captor, "Old");
        Assert.False(ledger.TryGet(new(Prisoner.NetworkId, Prisoner.LifeId + 1), out _));
        var secondCaptor = new BraceletLife(3, 30);
        ledger.Bind(Prisoner, secondCaptor, "New");
        ledger.ReleaseCaptor(Captor);
        Assert.Equal(secondCaptor, Assert.Single(ledger.Active).Captor);
        ledger.Reset();
        Assert.Empty(ledger.Active);
        Assert.Empty(ledger.Notices);
    }

    [Fact]
    public void HudNamesCannotInjectTagsOrAdditionalLines()
    {
        Assert.Equal("‹/noparse›‹size=999›TestPlayer", BraceletPolicy.SafeName("</noparse><size=999>TestPlayer"));
        Assert.Equal("A B C", BraceletPolicy.SafeName("A\nB\u2028C"));
        Assert.Equal(64, BraceletPolicy.SafeName(new string('a', 100)).Length);
    }

    [Theory]
    [InlineData(9.9, 10, 1)]
    [InlineData(14.9, 15, 2)]
    [InlineData(17.9, 18, 3)]
    [InlineData(19.9, 20, 4)]
    [InlineData(21.9, 22, 5)]
    [InlineData(20.1, 20, 0)]
    [InlineData(20, 19.9, 0)]
    [InlineData(15.1, 15, 0)]
    [InlineData(9, 22.1, 5)]
    [InlineData(21, 14, 0)]
    public void WarningsOnlyCrossBoundariesOutward(double previous, double current, int expected)
        => Assert.Equal(expected, BraceletPolicy.WarningCrossing(previous, current));

    [Fact]
    public void JitterRequiresRetreatAndCooldownButEscalationIsImmediate()
    {
        var binding = new BraceletBinding(Prisoner, Captor, "P", 9);
        Assert.Equal(1, binding.ObserveDistance(10, 1));
        Assert.Equal(0, binding.ObserveDistance(9.9, 2));
        Assert.Equal(0, binding.ObserveDistance(10.1, 3)); // time alone does not rearm
        Assert.Equal(2, binding.ObserveDistance(15, 3.1));
        Assert.Equal(3, binding.ObserveDistance(18, 3.2));
        Assert.Equal(4, binding.ObserveDistance(20, 3.3));
        Assert.Equal(5, binding.ObserveDistance(22, 3.4));
        Assert.Equal(0, binding.ObserveDistance(21.4, 3.5));
        Assert.Equal(0, binding.ObserveDistance(22, 3.6)); // cooldown
        Assert.Equal(0, binding.ObserveDistance(21.4, 5));
        Assert.Equal(5, binding.ObserveDistance(22, 5.1));
    }

    [Theory]
    [InlineData(ServerLanguage.English, "18.42")]
    [InlineData(ServerLanguage.Ukrainian, "18,42")]
    [InlineData(ServerLanguage.Russian, "18,42")]
    public void DistanceUsesTheViewersDecimalSeparator(ServerLanguage language, string expected)
        => Assert.Equal(expected, BraceletPolicy.FormatDistance(18.42, language));

    [Theory]
    [InlineData(100, BraceletPhase.Filling)]
    [InlineData(102.999, BraceletPhase.Filling)]
    [InlineData(103, BraceletPhase.Detonating)]
    [InlineData(106.999, BraceletPhase.Detonating)]
    [InlineData(107, BraceletPhase.Due)]
    [InlineData(200, BraceletPhase.Due)]
    public void FillingLastsThreeSecondsAndDetonationEndsAtSeven(double now, BraceletPhase expected)
    {
        var binding = new BraceletBinding(Prisoner, Captor, "P");
        Assert.Equal(BraceletPhase.Armed, binding.Phase(100));
        Assert.True(binding.BeginSequence(100));
        Assert.False(binding.BeginSequence(106));
        Assert.Equal(expected, binding.Phase(now));
    }

    [Fact]
    public void ACommittedBraceletCannotBeReleasedReboundOrCanceledByRoundEnd()
    {
        var ledger = new BraceletLedger();
        ledger.Bind(Prisoner, Captor, "P");
        ledger.Bind(new(3, 30), Captor, "Another prisoner");
        Assert.True(ledger.TryGet(Prisoner, out var binding));
        binding.BeginSequence(5);
        Assert.False(ledger.Release(Prisoner));
        Assert.False(ledger.Bind(Prisoner, new(4, 40), "New captor"));
        ledger.ReleaseCaptor(Captor);
        ledger.ReleaseAll();
        Assert.Same(binding, Assert.Single(ledger.Active));
        Assert.Equal(0, binding.ObserveDistance(1, 11));
        Assert.Equal(BraceletPhase.Due, binding.Phase(12));
        Assert.True(ledger.Complete(Prisoner, 12, out _));
        Assert.False(ledger.Complete(Prisoner, 13, out _));
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(2, 3)]
    [InlineData(3, 4)]
    [InlineData(4, 6)]
    [InlineData(5, 8)]
    public void CuesHaveSeparateShortBeepsAndComfortableUnclippedPcm(int level, int expectedBeeps)
    {
        float[] samples = BraceletSignal.CreateWarning(level);
        Assert.InRange(samples.Length, 24000, 48000);
        Assert.All(samples, sample => Assert.InRange(sample, -0.181f, 0.181f));
        int beeps = 0;
        bool previouslyAudible = false;
        for (int offset = 0; offset < samples.Length; offset += 480)
        {
            bool audible = samples.Skip(offset).Take(480).Any(s => Math.Abs(s) > 0.001);
            if (audible && !previouslyAudible) beeps++;
            previouslyAudible = audible;
        }
        Assert.Equal(expectedBeeps, beeps);
        Assert.Equal(0, samples[0]);
        Assert.Equal(0, samples[samples.Length - 1]);
    }

    [Fact]
    public void ConfirmationIsOneContinuousGentleSecondWithSoftEdges()
    {
        var samples = BraceletSignal.CreateConfirmation();
        Assert.InRange(samples.Length, 48000, 49000);
        Assert.All(samples, x => Assert.InRange(x, -0.141f, 0.141f));
        for (int start = 480; start < 47520; start += 480)
            Assert.Contains(samples.Skip(start).Take(480), x => Math.Abs(x) > 0.005);
        Assert.Equal(0, samples[0]);
        Assert.Equal(0, samples[samples.Length - 1]);
    }

    [Fact]
    public void SequenceAlarmIsSevenSecondsLongAndHasNoClippingOrAbruptEnds()
    {
        float[] samples = BraceletSignal.CreateSequence();
        Assert.Equal(7 * BraceletSignal.SampleRate, samples.Length);
        Assert.All(samples, sample => Assert.InRange(sample, -0.241f, 0.241f));
        Assert.Equal(0, samples[0]);
        Assert.InRange(Math.Abs(samples[samples.Length - 1]), 0, 0.002f);
    }

    [Theory]
    [InlineData(0, 3, 36)]
    [InlineData(3, 4, 112)]
    public void BothStagesKeepBeepingAndDetonationHasMoreThanTwiceThePulseRate(int startSeconds, int durationSeconds, int expectedBeeps)
    {
        float[] samples = BraceletSignal.CreateSequence();
        int start = startSeconds * BraceletSignal.SampleRate;
        int end = start + durationSeconds * BraceletSignal.SampleRate;
        int beeps = 0;
        bool previouslyAudible = false;
        for (int offset = start; offset < end; offset += 48)
        {
            bool audible = false;
            for (int i = offset; i < offset + 48; i++) if (Math.Abs(samples[i]) > 0.003) audible = true;
            if (audible && !previouslyAudible) beeps++;
            previouslyAudible = audible;
        }
        Assert.Equal(expectedBeeps, beeps);
    }
}
