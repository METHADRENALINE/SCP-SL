using System.Text;
using System.Text.Json;
using Methadrenaline.Server.Core;
using Xunit;

namespace Methadrenaline.Server.Tests;

public sealed class ModeVoteTests
{
    [Theory]
    [InlineData(CommunityRoundMode.Plain, true)]
    [InlineData(CommunityRoundMode.Blackout, true)]
    [InlineData(CommunityRoundMode.Blackout, false)]
    public void EligibleModeOpensIndependentBallotAndRoundKeepsBothChoices(CommunityRoundMode mode, bool enabled)
    {
        var policy = Lobby(3);
        string first = policy.PollId!;
        Assert.DoesNotContain(CommunityRoundMode.Perks, policy.Available);
        Assert.Equal("closed", policy.Vote(first, "1", ModeVotePolicy.PerksOn, 101));
        Assert.Equal("accepted", policy.Vote(first, "1", mode, 102));
        policy.Tick(3, true, 130, _ => 0);
        string second = policy.PollId!;
        Assert.NotEqual(first, second);
        Assert.True(policy.Open && policy.VotingPerks);
        Assert.Equal(160, policy.Deadline);
        Assert.Equal("closed", policy.Vote(first, "1", ModeVotePolicy.PerksOn, 131));
        Assert.Equal("closed", policy.Vote(second, "1", mode, 131));
        Assert.Equal("accepted", policy.Vote(second, "1", enabled ? ModeVotePolicy.PerksOn : ModeVotePolicy.PerksOff, 131));
        Assert.Equal("duplicate", policy.Vote(second, "1", ModeVotePolicy.PerksOff, 132));
        policy.Tick(3, true, 160, _ => throw new Exception("Clear majority"));
        Assert.False(policy.Open);
        var round = new PerkRoundPolicy();
        Assert.Equal(mode, round.BeginMode(() => policy.Finish(_ => throw new Exception()), () => policy.Perks == true));
        Assert.Equal(enabled, round.HasPerks);
    }

    [Theory]
    [InlineData(CommunityRoundMode.Escort)]
    [InlineData(CommunityRoundMode.Paravoz)]
    public void ForbiddenModesSkipSecondBallotAndCannotEnableArcade(CommunityRoundMode mode)
    {
        var policy = Lobby(3);
        policy.Vote(policy.PollId, "1", mode, 101);
        policy.Tick(3, true, 130, _ => 0);
        Assert.False(policy.Open);
        Assert.False(policy.Perks);
        var round = new PerkRoundPolicy();
        round.BeginMode(() => mode, () => true);
        Assert.False(round.HasPerks);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    public void TiedAndEmptyPerkVotesDrawOnce(bool tied, int draw)
    {
        var policy = Lobby();
        policy.Vote(policy.PollId, "1", CommunityRoundMode.Plain, 101);
        policy.Tick(2, true, 130, _ => 0);
        if (tied)
        {
            policy.Vote(policy.PollId, "1", ModeVotePolicy.PerksOff, 131);
            policy.Vote(policy.PollId, "2", ModeVotePolicy.PerksOn, 132);
        }
        int draws = 0;
        policy.Tick(2, true, 160, n => { Assert.Equal(2, n); draws++; return draw; });
        policy.Finish(_ => throw new Exception("Already decided"));
        Assert.Equal(1, draws);
        Assert.Equal(draw == 1, policy.Perks);
    }

    [Fact]
    public void PerkBallotRestartsAfterPopulationDipAndCanBeFinishedEarly()
    {
        var policy = Lobby();
        policy.Vote(policy.PollId, "1", CommunityRoundMode.Blackout, 101);
        policy.Tick(2, true, 130, _ => 0);
        var old = policy.PollId;
        policy.Vote(old, "1", ModeVotePolicy.PerksOff, 131);
        policy.Tick(1, true, 132, _ => 0);
        Assert.False(policy.Open);
        Assert.Equal(CommunityRoundMode.Blackout, policy.Selected);
        policy.Tick(2, true, 150, _ => 0);
        Assert.NotEqual(old, policy.PollId);
        Assert.Equal(180, policy.Deadline);
        Assert.Equal("accepted", policy.Vote(policy.PollId, "1", ModeVotePolicy.PerksOn, 151));
        policy.ForceFinish(_ => throw new Exception());
        policy.Tick(2, true, 152, _ => throw new Exception());
        Assert.False(policy.Open);
        Assert.True(policy.Perks);
    }

    [Fact]
    public void AdminCanReplaceVotingResultThroughoutCountdownAndResetForNextRound()
    {
        var policy = Lobby();
        string poll = policy.PollId!;
        policy.Vote(poll, "123", CommunityRoundMode.Blackout, 101);
        policy.Tick(2, true, 130, _ => 0);
        Assert.Equal(CommunityRoundMode.Blackout, policy.Selected);
        policy.Override(CommunityRoundMode.Paravoz);
        policy.Tick(2, true, 131, _ => throw new Exception("Admin selection must not reroll"));
        Assert.Equal("closed", policy.Vote(poll, "124", CommunityRoundMode.Plain, 131));
        Assert.Equal(CommunityRoundMode.Paravoz, policy.Selected);
        policy.Override(CommunityRoundMode.Plain);
        policy.Tick(1, true, 132, _ => 0);
        Assert.Equal(CommunityRoundMode.Plain, policy.Finish(_ => 0));
        policy.Reset();
        policy.Tick(2, true, 140, _ => 0);
        Assert.True(policy.Open);
        Assert.False(policy.AdminSelected);
        Assert.NotEqual(poll, policy.PollId);
    }

    [Fact]
    public void AdminCanEndOpenPollWithoutWaitingAndLateVotesCannotChangeChoice()
    {
        var policy = Lobby();
        string poll = policy.PollId!;
        policy.Override(CommunityRoundMode.Perks);
        policy.Tick(2, true, 101, _ => 0);
        Assert.False(policy.Open);
        Assert.Equal("closed", policy.Vote(poll, "123", CommunityRoundMode.Plain, 102));
        Assert.Equal(CommunityRoundMode.Plain, policy.Finish(_ => throw new Exception("No reroll")));
        Assert.True(policy.Perks);
    }

    [Fact]
    public void PlayerNamesArePrintableBoundedAndNeverSplitSurrogatePairs()
    {
        Assert.Equal("name", ModeVoteNames.PlayerName("\nna\u200bme\r", "ignored"));
        Assert.Equal("a b", ModeVoteNames.PlayerName("a\u00a0b\u2028\u2029", null));
        Assert.Equal("fallback", ModeVoteNames.PlayerName("\r\n\u200b", " fallback "));
        Assert.Equal("?", ModeVoteNames.PlayerName("\ud800\ue000", "\n"));
        Assert.Equal(new string('a', 99), ModeVoteNames.PlayerName(new string('a', 99) + "\ud83d\ude80", null));
        Assert.Equal(new string('a', 98) + "\ud83d\ude80", ModeVoteNames.PlayerName(new string('a', 98) + "\ud83d\ude80x", null));
    }

    private static ModeVotePolicy Lobby(int players = 2)
    {
        var policy = new ModeVotePolicy();
        policy.Tick(players, true, 100, _ => 0);
        return policy;
    }

    [Fact]
    public void VotingNeedsTwoPlayersAndProvidesThirtySeconds()
    {
        var policy = new ModeVotePolicy();
        policy.Tick(0, true, 90, _ => 0);
        Assert.Null(policy.PollId);
        Assert.False(policy.Open);
        Assert.Empty(policy.Available);
        policy.Tick(1, true, 100, _ => 0);
        Assert.False(policy.Open);
        Assert.Null(policy.PollId);
        Assert.Equal(1, policy.Players);
        Assert.Empty(policy.Available);
        policy.Tick(2, true, 105, _ => 0);
        Assert.True(policy.Open);
        Assert.Equal(135, policy.Deadline);
        Assert.True(Guid.TryParseExact(policy.PollId, "D", out _));
        Assert.Equal(new[] { CommunityRoundMode.Plain, CommunityRoundMode.Paravoz, CommunityRoundMode.Blackout }, policy.Available);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EscortRequiresThreeEvenAfterSelection(bool admin)
    {
        var policy = Lobby(3);
        Assert.Contains(CommunityRoundMode.Escort, policy.Available);
        Assert.Equal("accepted", policy.Vote(policy.PollId, "123", CommunityRoundMode.Escort, 101));
        if (admin) policy.Override(CommunityRoundMode.Escort);
        else policy.Tick(3, true, 130, _ => 0);
        Assert.Equal(CommunityRoundMode.Escort, policy.Selected);
        policy.Tick(2, true, 131, _ => 0);
        Assert.DoesNotContain(CommunityRoundMode.Escort, policy.Available);
        Assert.NotEqual(CommunityRoundMode.Escort, policy.Finish(_ => 0));
    }

    [Fact]
    public void LostEscortVoteCanBeRecastAndRandomOnlyUsesEligibleModes()
    {
        var policy = Lobby(3);
        policy.Vote(policy.PollId, "123", CommunityRoundMode.Escort, 101);
        policy.Tick(2, true, 102, _ => 0);
        Assert.Equal("closed", policy.Vote(policy.PollId, "123", CommunityRoundMode.Escort, 103));
        Assert.Equal("accepted", policy.Vote(policy.PollId, "123", CommunityRoundMode.Blackout, 103));
        Assert.Equal(CommunityRoundMode.Blackout, policy.Finish(_ => 0));
        Assert.Equal(CommunityRoundMode.Escort, Lobby(3).Finish(n => n - 1));
        Assert.Equal(CommunityRoundMode.Blackout, Lobby(2).Finish(n => n - 1));
    }

    [Fact]
    public void FirstVoteIsAuthoritativeEvenWhenUserChangesMode()
    {
        var policy = Lobby();
        Assert.Equal("accepted", policy.Vote(policy.PollId, "123456789012345678", CommunityRoundMode.Blackout, 101));
        Assert.Equal("duplicate", policy.Vote(policy.PollId, "123456789012345678", CommunityRoundMode.Plain, 102));
        policy.Tick(2, true, 130, _ => 0);
        Assert.Equal(CommunityRoundMode.Blackout, policy.Selected);
        Assert.Equal("closed", policy.Vote(policy.PollId, "223456789012345678", CommunityRoundMode.Plain, 130));
    }

    [Fact]
    public void ClosedPollNeverReopensUntilTheWaitingGenerationChanges()
    {
        var policy = Lobby();
        string id = policy.PollId!;
        policy.Tick(2, true, 130, _ => 1);
        for (int time = 131; time < 600; time++) policy.Tick(2, true, time, _ => throw new Exception("No reroll."));
        Assert.Equal(id, policy.PollId);
        Assert.Equal(130, policy.Deadline);
        Assert.False(policy.Open);
        Assert.Equal(CommunityRoundMode.Paravoz, policy.Selected);
        policy.Reset();
        policy.Tick(2, true, 700, _ => 0);
        Assert.NotEqual(id, policy.PollId);
        Assert.Equal(730, policy.Deadline);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void DroppingBelowTwoPlayersCancelsAndClearsAllVotes(int remaining)
    {
        var policy = Lobby();
        string id = policy.PollId!;
        policy.Vote(id, "123", CommunityRoundMode.Blackout, 101);
        policy.Tick(remaining, true, 102, _ => 0);
        Assert.Null(policy.PollId);
        Assert.Null(policy.Selected);
        Assert.False(policy.Open);
        Assert.Equal("closed", policy.Vote(id, "123", CommunityRoundMode.Blackout, 103));
        policy.Tick(2, true, 104, _ => 0);
        Assert.NotEqual(id, policy.PollId);
        Assert.Equal("closed", policy.Vote(id, "123", CommunityRoundMode.Blackout, 105));
        Assert.Equal("accepted", policy.Vote(policy.PollId, "123", CommunityRoundMode.Plain, 105));
        Assert.Equal(134, policy.Deadline);
        policy.Tick(2, true, 134, _ => 0);
        Assert.Equal(CommunityRoundMode.Plain, policy.Selected);
    }

    [Fact]
    public void PlayersArrivingDoNotExtendTheDeadline()
    {
        var policy = Lobby();
        string id = policy.PollId!;
        policy.Tick(3, true, 110, _ => 0);
        Assert.Equal(id, policy.PollId);
        Assert.Equal(130, policy.Deadline);
        Assert.Contains(CommunityRoundMode.Paravoz, policy.Available);
    }

    [Theory]
    [InlineData(0, CommunityRoundMode.Plain)]
    [InlineData(1, CommunityRoundMode.Paravoz)]
    [InlineData(2, CommunityRoundMode.Blackout)]
    public void NoVotesDrawsFromAllAvailableModes(int index, CommunityRoundMode expected)
    {
        var policy = Lobby();
        policy.Tick(2, true, 130, count => { Assert.Equal(3, count); return index; });
        Assert.Equal(expected, policy.Selected);
    }

    [Theory]
    [InlineData(0, CommunityRoundMode.Paravoz)]
    [InlineData(1, CommunityRoundMode.Blackout)]
    public void TieRandomizesOnlyTheWinners(int index, CommunityRoundMode expected)
    {
        var policy = Lobby();
        policy.Vote(policy.PollId, "1", CommunityRoundMode.Blackout, 101);
        policy.Vote(policy.PollId, "2", CommunityRoundMode.Paravoz, 101);
        policy.Tick(2, true, 130, count => { Assert.Equal(2, count); return index; });
        Assert.Equal(expected, policy.Selected);
    }

    [Fact]
    public void LosingSecondPlayerRejectsVotesForEveryMode()
    {
        var policy = Lobby();
        policy.Vote(policy.PollId, "1", CommunityRoundMode.Paravoz, 101);
        policy.Tick(1, true, 102, _ => 0);
        Assert.Equal("closed", policy.Vote(policy.PollId, "2", CommunityRoundMode.Paravoz, 103));
        Assert.Equal("closed", policy.Vote(policy.PollId, "2", CommunityRoundMode.Blackout, 103));
        Assert.Equal("closed", policy.Vote(policy.PollId, "2", CommunityRoundMode.Plain, 103));
        Assert.Empty(policy.Available);
        Assert.Null(policy.Selected);
    }

    [Fact]
    public void LosingSecondPlayerClearsSelectionAndRequiresAnotherFullVote()
    {
        var policy = Lobby();
        policy.Vote(policy.PollId, "1", CommunityRoundMode.Paravoz, 101);
        policy.Tick(2, true, 130, _ => 0);
        Assert.Equal(CommunityRoundMode.Paravoz, policy.Selected);
        policy.Tick(1, true, 131, _ => throw new Exception("No selection with one player."));
        Assert.Null(policy.Selected);
        Assert.Null(policy.PollId);
        Assert.False(policy.Open);
        policy.Tick(2, true, 140, _ => 0);
        Assert.True(policy.Open);
        Assert.Equal(170, policy.Deadline);
        Assert.Equal("accepted", policy.Vote(policy.PollId, "1", CommunityRoundMode.Blackout, 141));
        policy.Tick(2, true, 170, _ => 0);
        policy.Tick(2, true, 200, _ => 0);
        Assert.Equal(CommunityRoundMode.Blackout, policy.Finish(_ => throw new Exception("Round must not reroll.")));
        var perks = new PerkRoundPolicy();
        Assert.Equal(CommunityRoundMode.Blackout, perks.BeginMode(() => policy.Selected!.Value));
        Assert.Equal(CommunityRoundMode.Blackout, perks.BeginMode(() => CommunityRoundMode.Plain));
    }

    [Theory]
    [InlineData(CommunityRoundMode.Plain)]
    [InlineData(CommunityRoundMode.Blackout)]
    public void EligibleWinnerStaysSelectedWithOnePlayer(CommunityRoundMode mode)
    {
        var policy = Lobby();
        string poll = policy.PollId!;
        Assert.Equal("accepted", policy.Vote(poll, "1", mode, 101));
        policy.Tick(2, true, 130, _ => 0);
        policy.Tick(2, true, 160, _ => 0);
        poll = policy.PollId!;
        policy.Tick(1, true, 161, _ => throw new Exception("No reroll."));
        Assert.Equal(mode, policy.Selected);
        Assert.False(policy.Open);
        Assert.Empty(policy.Available);

        policy.Tick(2, true, 170, _ => throw new Exception("No reroll."));
        Assert.Equal(poll, policy.PollId);
        Assert.Equal(mode, policy.Selected);
        Assert.False(policy.Open);
    }

    [Fact]
    public void VotesAtTheDeadlineAreRejectedEvenBeforeTheNextTick()
    {
        var policy = Lobby();
        Assert.Equal("closed", policy.Vote(policy.PollId, "1", CommunityRoundMode.Blackout, 130));
        Assert.Equal("closed", policy.Vote(Guid.NewGuid().ToString("D"), "1", CommunityRoundMode.Blackout, 101));
        policy.Tick(2, false, 110, _ => 0);
        Assert.Equal("closed", policy.Vote(policy.PollId, "1", CommunityRoundMode.Blackout, 111));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("01")]
    [InlineData(" 1")]
    [InlineData("+1")]
    [InlineData("18446744073709551616")]
    [InlineData("123abc")]
    public void InvalidDiscordIdsAreRejected(string user) => Assert.False(ModeVotePolicy.ValidUser(user));

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{not json}")]
    public void InvalidJsonIsRejected(string json) => Assert.ThrowsAny<Exception>(() => ModeVoteJson.Read(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void JsonContractUsesDashedIdsSnakeCaseAndExplicitEmptyValues()
    {
        var request = new ModeVoteRequest { BootId = Guid.NewGuid().ToString("D"), PollId = Guid.NewGuid().ToString("D"), UserId = "123", Mode = "perks" };
        var parsed = ModeVoteJson.Read(ModeVoteJson.Write(request));
        Assert.Equal(request.BootId, parsed.BootId);
        Assert.Equal(request.PollId, parsed.PollId);
        using var json = JsonDocument.Parse(ModeVoteJson.Write(new ModeVoteStatus { BootId = request.BootId }));
        Assert.Equal(1, json.RootElement.GetProperty("schema_version").GetInt32());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("mode").ValueKind);
        var poll = json.RootElement.GetProperty("poll");
        Assert.Equal(JsonValueKind.Null, poll.GetProperty("id").ValueKind);
        Assert.Equal(JsonValueKind.Null, poll.GetProperty("deadline").ValueKind);
        Assert.False(poll.GetProperty("open").GetBoolean());
        Assert.Empty(json.RootElement.GetProperty("online_names").EnumerateArray());
    }

    [Fact]
    public void JsonReaderRejectsUnknownDuplicateMissingAndNonStringFieldsAndTrailingData()
    {
        var fields = new Dictionary<string, object?>
        {
            ["boot_id"] = Guid.NewGuid().ToString("D"),
            ["poll_id"] = Guid.NewGuid().ToString("D"),
            ["discord_user_id"] = "123",
            ["mode"] = "perks"
        };
        string valid = JsonSerializer.Serialize(fields);
        Assert.ThrowsAny<JsonException>(() => ModeVoteJson.Read(Encoding.UTF8.GetBytes(valid + " {}")));
        Assert.ThrowsAny<JsonException>(() => ModeVoteJson.Read(Encoding.UTF8.GetBytes(valid.Substring(0, valid.Length - 1) + ",\"mode\":\"plain\"}")));
        fields["extra"] = "forbidden";
        Assert.ThrowsAny<JsonException>(() => ModeVoteJson.Read(JsonSerializer.SerializeToUtf8Bytes(fields)));
        fields.Remove("extra");
        fields["discord_user_id"] = 123;
        Assert.ThrowsAny<JsonException>(() => ModeVoteJson.Read(JsonSerializer.SerializeToUtf8Bytes(fields)));
        fields["discord_user_id"] = null;
        Assert.ThrowsAny<JsonException>(() => ModeVoteJson.Read(JsonSerializer.SerializeToUtf8Bytes(fields)));
        fields.Remove("discord_user_id");
        Assert.ThrowsAny<JsonException>(() => ModeVoteJson.Read(JsonSerializer.SerializeToUtf8Bytes(fields)));
        Assert.ThrowsAny<JsonException>(() => ModeVoteJson.Read(Encoding.UTF8.GetBytes("{\"extra\":{\"a\":{\"b\":{\"c\":{}}}}}")));
    }

    [Fact]
    public async Task CanceledAndExpiredQueueEntriesNeverApplyLater()
    {
        using var bridge = new ModeVoteBridge();
        bridge.Publish(new ModeVoteStatus());
        var canceled = bridge.Enqueue(new ModeVoteRequest())!;
        canceled.Cancel();
        bridge.Drain(_ => throw new Exception("Canceled request applied."));
        Assert.Null(await canceled.Completion);
        var expired = bridge.Enqueue(new ModeVoteRequest())!;
        await Task.Delay(2100);
        bridge.Drain(_ => throw new Exception("Expired request applied."));
        Assert.Null(await expired.Completion);
    }

    [Fact]
    public async Task QueueIsBoundedAndManyConcurrentVotesStillDeduplicateOnGameThread()
    {
        using var bridge = new ModeVoteBridge();
        var policy = Lobby();
        bridge.Publish(new ModeVoteStatus());
        var votes = Enumerable.Range(0, 64).AsParallel().Select(_ => bridge.Enqueue(new ModeVoteRequest { PollId = policy.PollId!, UserId = "123", Mode = "blackout" })!).ToArray();
        Assert.Null(bridge.Enqueue(new ModeVoteRequest()));
        bridge.Drain(v => policy.Vote(v.PollId, v.UserId, CommunityRoundMode.Blackout, 101));
        var results = await Task.WhenAll(votes.Select(v => v.Completion));
        Assert.Equal(1, results.Count(r => r == "accepted"));
        Assert.Equal(63, results.Count(r => r == "duplicate"));
    }

    [Fact]
    public async Task SnapshotIsImmutableAndHttpReadsCannotRefreshHeartbeat()
    {
        using var bridge = new ModeVoteBridge();
        var status = new ModeVoteStatus { HeartbeatSequence = 7, OnlineNames = new[] { "before" } };
        bridge.Publish(status);
        status.OnlineNames[0] = "after";
        var snapshot = bridge.Snapshot!;
        var mutated = snapshot.Json;
        mutated[0] = 0;
        using var json = JsonDocument.Parse(snapshot.Json);
        Assert.Equal("before", json.RootElement.GetProperty("online_names")[0].GetString());
        await Task.Delay(5100);
        Assert.False(bridge.Snapshot!.Fresh);
        Assert.Null(bridge.Enqueue(new ModeVoteRequest()));
        Assert.Equal(7, json.RootElement.GetProperty("heartbeat_sequence").GetInt64());
    }

    [Theory]
    [InlineData(ServerLanguage.English, "Voting for a mode in Discord\n30 s", "Vanilla (almost)", "Perks", "Paravoz")]
    [InlineData(ServerLanguage.Ukrainian, "Голосування за режим у Discord\n30 с", "Ваніль (майже)", "Перки", "Паровоз")]
    [InlineData(ServerLanguage.Russian, "Голосование за режим в Discord\n30 с", "Ваниль (почти)", "Перки", "Паравоз")]
    public void LobbyHudAndAllModeNamesUseTheExistingThreeLanguageCatalog(ServerLanguage language, string expected, string plain, string perks, string paravoz)
    {
        using var stream = typeof(ModeVoteTests).Assembly.GetManifestResourceStream("catalog.xml")!;
        var catalog = LocalizationCatalog.Read(stream);
        Assert.Equal(expected, string.Format(catalog.Translate(ModeVotePolicy.VotingLabel, language), 30));
        Assert.Equal(plain, catalog.Translate(ModeVotePolicy.Label(CommunityRoundMode.Plain), language));
        Assert.Equal(perks, catalog.Translate(ModeVotePolicy.Label(CommunityRoundMode.Perks), language));
        Assert.Equal(paravoz, catalog.Translate(ModeVotePolicy.Label(CommunityRoundMode.Paravoz), language));
    }
}
