using Methadrenaline.Server.Core;
using Xunit;

namespace Methadrenaline.Server.Tests;

public sealed class ParavozPresentationTests
{
    [Fact]
    public void CarrierHearsOwnTrackEvenBesideAnotherCarrier()
        => Assert.Equal((uint)4, ParavozPresentationPolicy.Select(4, 2, new Dictionary<uint, double> { [2] = 0, [4] = 0.5 }));

    [Fact]
    public void ListenerHearsExactlyOneNearestSource()
        => Assert.Equal((uint)2, ParavozPresentationPolicy.Select(7, null, new Dictionary<uint, double> { [3] = 9, [2] = 4, [1] = 16 }));

    [Fact]
    public void EqualDistancesHaveStableSelection()
        => Assert.Equal((uint)1, ParavozPresentationPolicy.Select(7, null, new Dictionary<uint, double> { [2] = 4, [1] = 4 }));

    [Fact]
    public void SmallDistanceChangesDoNotRapidlySwitchTheSource()
        => Assert.Equal((uint)2, ParavozPresentationPolicy.Select(7, 2, new Dictionary<uint, double> { [1] = 4, [2] = 6.25 }));

    [Fact]
    public void ListenerSwitchesWhenAnotherCarrierIsClearlyCloser()
        => Assert.Equal((uint)1, ParavozPresentationPolicy.Select(7, 2, new Dictionary<uint, double> { [1] = 4, [2] = 16 }));

    [Fact]
    public void DeadSourceDisappearsWithoutKeepingAStaleSelection()
        => Assert.Equal((uint)1, ParavozPresentationPolicy.Select(7, 2, new Dictionary<uint, double> { [1] = 4 }));

    [Fact]
    public void OutOfRangeAndInvalidDistancesProduceSilence()
        => Assert.Null(ParavozPresentationPolicy.Select(7, 1, new Dictionary<uint, double> { [1] = 4901, [2] = double.NaN, [3] = double.PositiveInfinity, [4] = -1 }));

    [Theory]
    [InlineData(48000)]
    [InlineData(48123)]
    [InlineData(48240)]
    public void LoopHasNoPartialPacketOrHardBoundary(int length)
    {
        var source = Enumerable.Range(0, length).Select(i => i < length / 2 ? 0.5f : -0.5f).ToArray();
        var copy = (float[])source.Clone();
        var loop = ParavozPresentationPolicy.LoopSamples(source);
        Assert.Equal(0, loop.Length % 480);
        Assert.Equal(loop[0], loop[^1]);
        Assert.All(loop, value => Assert.InRange(value, -0.5f, 0.5f));
        Assert.Equal(copy, source);
    }

    [Fact]
    public void TooShortClipIsRejected()
        => Assert.Throws<ArgumentException>(() => ParavozPresentationPolicy.LoopSamples(new float[480]));
}
