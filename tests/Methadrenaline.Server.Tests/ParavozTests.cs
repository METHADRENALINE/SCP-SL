using Methadrenaline.Server.Core;
using Xunit;

namespace Methadrenaline.Server.Tests;

public sealed class ParavozTests
{
    [Theory]
    [InlineData(-1, 60)]
    [InlineData(0, 60)]
    [InlineData(0.1f, 60)]
    [InlineData(1, 59)]
    [InlineData(1.1f, 59)]
    [InlineData(59.1f, 1)]
    [InlineData(60, 0)]
    [InlineData(61, 0)]
    public void WaitingClockRoundsUpAndStopsAtRelease(float elapsed, int seconds)
        => Assert.Equal(seconds, ParavozPolicy.WaitingSeconds(elapsed));

    [Theory]
    [InlineData(CommunityRoundMode.Plain)]
    [InlineData(CommunityRoundMode.Perks)]
    [InlineData(CommunityRoundMode.Paravoz)]
    public void ThreeModeChoiceIsMadeOnlyOnce(CommunityRoundMode mode)
    {
        var policy = new PerkRoundPolicy();
        Assert.Equal(mode, policy.BeginMode(() => mode));
        Assert.Equal(mode, policy.BeginMode(() => throw new Exception("Already selected")));
        Assert.Equal(mode == CommunityRoundMode.Perks, policy.HasPerks);
        if (mode == CommunityRoundMode.Paravoz) Assert.Equal("Паравоз!", policy.Message);
    }
    private static ParavozPolicy Game()
    {
        var policy = new ParavozPolicy();
        policy.Begin(new uint[] { 1, 2, 3 }, 1);
        return policy;
    }
    [Fact]
    public void FireKillConvertsRunnersAndHuntersButNotOtherCarriers()
    {
        var game = Game();
        Assert.True(game.Escape(2));
        Assert.True(game.Infect(2, 1));
        Assert.True(game.Infect(3, 2));
        Assert.False(game.Infect(1, 2));
        Assert.Equal(ParavozWinner.Virus, game.Winner);
    }
    [Fact]
    public void EscapeOfEveryRemainingRunnerWinsEvenAfterAnInfection()
    {
        var game = Game();
        game.Infect(2, 1);
        Assert.Equal(ParavozWinner.None, game.Winner);
        game.Escape(3);
        Assert.Equal(ParavozWinner.Humans, game.Winner);
    }
    [Fact]
    public void HuntersCanEndTheOutbreakBeforeEveryoneEscapes()
    {
        var game = Game();
        game.Escape(2);
        game.Eliminate(1);
        Assert.Equal(ParavozWinner.Humans, game.Winner);
    }
    [Fact]
    public void LiveCarrierHunterAndRunnerMustKeepTheRoundOpen()
    {
        var game = Game();
        Assert.True(game.Escape(2));
        Assert.Equal(ParavozWinner.None, game.Winner);
        Assert.True(game.Is(1, ParavozMember.Paravoz));
        Assert.True(game.Is(2, ParavozMember.Hunter));
        Assert.True(game.Is(3, ParavozMember.Runner));
        // Repeated escape notifications cannot consume another runner.
        Assert.False(game.Escape(2));
        Assert.Equal(ParavozWinner.None, game.Winner);
    }
    [Fact]
    public void RecordedSixPlayerRoundWinsOnlyAfterFourthEscape()
    {
        var game = new ParavozPolicy();
        game.Begin(new uint[] { 1, 2, 3, 4, 5, 6 }, 1);
        game.Eliminate(2); // The recorded Tesla death.
        foreach (uint id in new uint[] { 3, 4, 5 })
        {
            Assert.True(game.Escape(id));
            Assert.Equal(ParavozWinner.None, game.Winner);
        }
        Assert.True(game.Is(6, ParavozMember.Runner));
        Assert.True(game.Escape(6));
        Assert.True(game.Is(1, ParavozMember.Paravoz));
        Assert.Equal(4, game.Members.Values.Count(x => x == ParavozMember.Hunter));
        Assert.Equal(ParavozWinner.Humans, game.Winner);
    }
    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(2, 1, 1)]
    [InlineData(1, 4, 1)]
    [InlineData(3, 2, 4)]
    public void AnyRemainingRunnerAndCarrierPreventsEscapeVictory(int carriers, int hunters, int runners)
    {
        var game = new ParavozPolicy();
        uint total = (uint)(carriers + hunters + runners);
        game.Begin(Enumerable.Range(1, (int)total).Select(x => (uint)x), 1);
        for (uint id = 2; id <= carriers; id++) Assert.True(game.Infect(id, 1));
        for (uint id = (uint)carriers + 1; id <= carriers + hunters; id++) Assert.True(game.Escape(id));
        Assert.Equal(ParavozWinner.None, game.Winner);
    }
    [Fact]
    public void EliminationIsNotEscapeOrInfection()
    {
        var game = Game();
        game.Eliminate(2);
        Assert.False(game.Escape(2));
        Assert.False(game.Infect(2, 1));
        Assert.Equal(ParavozWinner.None, game.Winner);
        game.Eliminate(3);
        Assert.Equal(ParavozWinner.Virus, game.Winner);
    }
    [Theory]
    [InlineData(0, 0)]
    [InlineData(349.9f, 0)]
    [InlineData(350, 10)]
    [InlineData(350.1f, 10)]
    [InlineData(351, 9)]
    [InlineData(359, 1)]
    [InlineData(359.9f, 1)]
    [InlineData(360, 0)]
    [InlineData(361, 0)]
    public void GatheringCountdownCoversOnlyLastTenSecondsBeforeTeleport(float elapsed, int seconds)
        => Assert.Equal(seconds, ParavozPolicy.GatherCountdown(elapsed));
}
