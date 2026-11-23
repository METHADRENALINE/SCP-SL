using Methadrenaline.Server.Core;
using Xunit;

namespace Methadrenaline.Server.Tests;

public sealed class FortuneCoinPolicyTests
{
    [Theory]
    [InlineData(0f, true)]
    [InlineData(399.99f, true)]
    [InlineData(400f, true)]
    [InlineData(400.01f, false)]
    [InlineData(1600f, false)]
    public void AudioStopsAtTwentyMetres(float squaredDistance, bool audible) =>
        Assert.Equal(audible, FortuneCoinPolicy.InEarshot(squaredDistance));

    [Fact]
    public void PreviouslyRecommendedLootSurvivesAMinorDetourButYieldsToDangerOrAMuchShorterRoute()
    {
        float same = FortuneCoinPolicy.LootCost(75, 0, true);
        float alternative = FortuneCoinPolicy.LootCost(65, 0, false);
        Assert.True(same < alternative);
        Assert.True(FortuneCoinPolicy.LootCost(8, 0, false) < same);
        Assert.True(FortuneCoinPolicy.LootCost(75, 140, true) > alternative);
    }
}
