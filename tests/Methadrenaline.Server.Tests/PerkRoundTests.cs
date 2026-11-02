using Methadrenaline.Server.Core;
using Xunit;

namespace Methadrenaline.Server.Tests;

public sealed class PerkRoundTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ChoiceIsMadeExactlyOncePerRound(bool choice)
    {
        var policy = new PerkRoundPolicy();
        int flips = 0;
        bool Flip() { flips++; return choice; }
        Assert.Equal(choice, policy.Begin(Flip));
        Assert.Equal(choice, policy.Begin(() => throw new Exception("A duplicate round event must not reroll.")));
        Assert.Equal(choice, policy.HasPerks);
        Assert.Equal(1, flips);
    }

    [Fact]
    public void RestartAllowsAnIndependentNewChoice()
    {
        var policy = new PerkRoundPolicy();
        Assert.True(policy.Begin(() => true));
        policy.Reset();
        Assert.Null(policy.HasPerks);
        Assert.False(policy.Begin(() => false));
        policy.Reset();
        Assert.True(policy.Begin(() => true));
    }

    [Fact]
    public void RussianAnnouncementsMatchTheRequestedTextExactly()
    {
        var policy = new PerkRoundPolicy();
        policy.Begin(() => true);
        Assert.Equal("Перки раскиданы по карте! Удачи выжить :)", policy.Message);
        policy.Reset();
        policy.Begin(() => false);
        Assert.Equal("Можно пробовать выжить, ведь катка без перков!", policy.Message);
    }
}
