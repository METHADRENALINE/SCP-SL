using Methadrenaline.Server.Core;
using Xunit;

namespace Methadrenaline.Server.Tests;

public sealed class ChaosRoundPlanTests
{
    [Fact]
    public void EveryRoundHasOneOrTwoRandomlyTimedBurstsAndNeverAThird()
    {
        var counts = new HashSet<int>();
        for (int seed = 0; seed < 1000; seed++)
        {
            var plan = Create(seed);
            counts.Add(plan.PlannedCount);
            Assert.InRange(plan.PlannedCount, 1, 2);
            Assert.InRange(plan.NextAt!.Value, 45, 120);
            double elapsed = 0;
            for (int i = 0; i < plan.PlannedCount; i++)
            {
                elapsed = plan.NextAt!.Value;
                Assert.True(plan.IsDue(elapsed));
                Assert.True(plan.TryRecordStart(elapsed));
                Assert.Equal(i + 1, plan.StartedCount);
                if (plan.NextAt.HasValue) Assert.InRange(plan.NextAt.Value - elapsed, 90, 180);
            }
            Assert.True(plan.Completed);
            Assert.Null(plan.NextAt);
            Assert.False(plan.IsDue(elapsed + 100000));
            Assert.False(plan.TryRecordStart(elapsed + 100000, allowEarly: true));
        }
        Assert.Equal(new[] { 1, 2 }, counts.Order().ToArray());
    }

    [Fact]
    public void ADelayedFirstBurstKeepsTheFullGapBeforeTheSecond()
    {
        var plan = Enumerable.Range(0, 100).Select(Create).First(p => p.PlannedCount == 2);
        Assert.True(plan.TryRecordStart(600));
        Assert.InRange(plan.NextAt!.Value, 690, 780);
        Assert.False(plan.IsDue(601));
    }

    [Fact]
    public void ManualLaunchUsesTheSameRoundBudgetInsteadOfAddingExtraBursts()
    {
        var plan = Create(42);
        Assert.False(plan.TryRecordStart(0));
        Assert.Equal(0, plan.StartedCount);
        for (int i = 0; i < plan.PlannedCount; i++) Assert.True(plan.TryRecordStart(i, allowEarly: true));
        Assert.False(plan.TryRecordStart(10000, allowEarly: true));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1d)]
    public void InvalidClockValuesCannotConsumeTheBudget(double elapsed)
    {
        var plan = Create(42);
        Assert.False(plan.TryRecordStart(elapsed, allowEarly: true));
        Assert.Equal(0, plan.StartedCount);
    }

    private static ChaosRoundPlan Create(int seed) => new(new Random(seed), 45, 120, 90, 180);
}
