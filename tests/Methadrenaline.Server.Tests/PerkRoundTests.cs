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

    [Theory]
    [InlineData(ServerLanguage.English, "Vanilla (almost)", "Блэкаут", " + perks")]
    [InlineData(ServerLanguage.Ukrainian, "Ваніль (майже)", "Блэкаут", " + перки")]
    [InlineData(ServerLanguage.Russian, "Ваниль (почти)", "Блэкаут", " + перки")]
    public void LobbyAndRoundStartShowTheSelectedModifier(ServerLanguage language, string vanilla, string blackout, string suffix)
    {
        using var stream = typeof(PerkRoundTests).Assembly.GetManifestResourceStream("catalog.xml")!;
        var catalog = LocalizationCatalog.Read(stream);
        foreach (var (mode, name) in new[] { (CommunityRoundMode.Plain, vanilla), (CommunityRoundMode.Blackout, blackout) })
        {
            Assert.Equal(name, catalog.Translate(ModeVotePolicy.Label(mode), language));
            var round = new PerkRoundPolicy();
            round.BeginMode(() => mode, () => true);
            Assert.Equal(name + suffix, catalog.Translate(ModeVotePolicy.Label(mode, true), language));
            Assert.Equal(name + suffix, catalog.Translate(round.Message, language).Split('\n')[0]);
        }
        foreach (var mode in new[] { CommunityRoundMode.Paravoz, CommunityRoundMode.Escort })
            Assert.Equal(ModeVotePolicy.Label(mode), ModeVotePolicy.Label(mode, true));
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
        Assert.Equal("Vanilla (almost) + perks\nПерки раскиданы по карте! Удачи выжить :)", policy.Message);
        policy.Reset();
        policy.Begin(() => false);
        Assert.Equal("Можно пробовать выжить, ведь катка без перков!", policy.Message);
    }
}
