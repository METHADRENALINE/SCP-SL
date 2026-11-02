using Methadrenaline.Server;
using Methadrenaline.Server.Core;
using Xunit;

namespace Methadrenaline.Server.Tests;

public sealed class GameplayTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData(0d, false)]
    [InlineData(-1d, false)]
    [InlineData(double.NaN, false)]
    [InlineData(double.PositiveInfinity, false)]
    [InlineData(3d, true)]
    [InlineData(12d, true)]
    [InlineData(12.1, false)]
    public void SignalDurationSupportsHeadlessServersWithoutAcceptingInvalidKnownDurations(double? duration, bool expected)
        => Assert.Equal(expected, AnnouncementPolicy.IsSafe(duration));

    [Theory]
    [InlineData(1, 1, 0)]
    [InlineData(2, 2, 1)]
    [InlineData(3, 3, 1)]
    [InlineData(4, 4, 1)]
    [InlineData(5, 5, 1)]
    [InlineData(6, 6, 1)]
    [InlineData(5, 1, 1)]
    [InlineData(6, 0, 0)]
    public void PlayerThresholdAndOptOutCap(int players, int willing, int expected)
        => Assert.Equal(expected, RolePolicy.ScpCount(players, willing));

    [Fact]
    public void DifferentFirstChoicesAreRespected()
    {
        var result = RolePolicy.Allocate(Ballots((5, 0, -5), (0, 5, -5)), new[] { 1, 2, 3 }, new Random(42));
        Assert.Equal(1, result[0]);
        Assert.Equal(2, result[1]);
    }

    [Fact]
    public void SharedFirstChoiceUsesFairCoinAndLoserGetsSecondChoice()
    {
        int firstWins = 0;
        var random = new Random(1129);
        for (int i = 0; i < 10000; i++)
        {
            var result = RolePolicy.Allocate(Ballots((5, 3, -5), (5, -5, 3)), new[] { 1, 2, 3 }, random);
            Assert.Equal(2, result.Values.Distinct().Count());
            if (result[0] == 1) { firstWins++; Assert.Equal(3, result[1]); }
            else { Assert.Equal(1, result[1]); Assert.Equal(2, result[0]); }
        }
        Assert.InRange(firstWins, 4750, 5250);
    }

    [Fact]
    public void UnavailablePreferencesFallThroughWithoutDuplicateRoles()
    {
        var result = RolePolicy.Allocate(Ballots((5, 4, 3), (5, 4, 3)), new[] { 1, 2, 3 }, new Random(15), (role, _) => role != 2);
        Assert.Equal(new[] { 1, 3 }, result.Values.Order().ToArray());
    }

    [Fact]
    public void NativePairConstraintsAreAppliedBeforeAssignment()
    {
        var result = RolePolicy.Allocate(Ballots((5, 4, 3), (5, 4, 3)), new[] { 1, 2, 3 }, new Random(15),
            (role, used) => !(role == 2 && used.Contains(1) || role == 1 && used.Contains(2)));
        Assert.False(result.Values.Contains(1) && result.Values.Contains(2));
    }

    [Fact]
    public void EmptyOrInsufficientPoolFailsBeforeAnyRolesAreCommitted()
        => Assert.Throws<InvalidOperationException>(() => RolePolicy.Allocate(Ballots((5, 0, 0), (5, 0, 0)), new[] { 1 }, new Random(1)));

    [Fact]
    public void SupportScpCanWinFirstPreferenceWhenCompanionSpawnsFirst()
    {
        var random = new Random(40);
        int wins = 0;
        for (int i = 0; i < 1000; i++)
        {
            var result = RolePolicy.Allocate(Ballots((4, 5, -5), (4, 5, -5)), new[] { 1, 2, 3 }, random,
                (role, previous) => role != 2 || previous.Contains(1));
            Assert.Equal(new[] { 1, 2 }, result.Values.Order().ToArray());
            if (result[0] == 2) wins++;
        }
        Assert.InRange(wins, 440, 560);
    }

    [Fact]
    public void LoneScpCannotBeSupportRoleRequiringCompanion()
    {
        var result = RolePolicy.Allocate(Ballots((4, 5, -5)), new[] { 1, 2, 3 }, new Random(2),
            (role, previous) => role != 2 || previous.Contains(1));
        Assert.Equal(1, result[0]);
    }

    [Fact]
    public void CleanupRestoresInReverseOrderAndContinuesAfterFailure()
    {
        var calls = new List<int>();
        var errors = new List<Exception>();
        var scope = new RestorationScope(errors.Add);
        scope.Add(() => calls.Add(1));
        scope.Add(() => throw new InvalidOperationException("test failure"));
        scope.Add(() => calls.Add(3));
        scope.Dispose();
        scope.Dispose();
        Assert.Equal(new[] { 3, 1 }, calls);
        Assert.Single(errors);
        Assert.Equal(0, scope.Count);
        Assert.Throws<ObjectDisposedException>(() => scope.Add(() => { }));
    }

    [Fact]
    public void InvalidConfigCannotEnableUnboundedOrZeroGravityEvents()
    {
        var config = new Config { GravityMultiplier = -50, EventDuration = float.PositiveInfinity, MinimumChaosGap = -1, MaximumChaosGap = float.NaN };
        config.Validate();
        Assert.InRange(config.GravityMultiplier, 0.35f, 0.8f);
        Assert.InRange(config.EventDuration, 5, 40);
        Assert.True(config.MaximumChaosGap >= config.MinimumChaosGap);
        config.MinimumChaosGap = 600;
        config.MaximumChaosGap = float.NaN;
        config.Validate();
        Assert.True(config.MaximumChaosGap >= config.MinimumChaosGap);
    }

    private static IReadOnlyDictionary<int, int>[] Ballots(params (int A, int B, int C)[] entries)
        => entries.Select(e => (IReadOnlyDictionary<int, int>)new Dictionary<int, int> { [1] = e.A, [2] = e.B, [3] = e.C }).ToArray();
}
