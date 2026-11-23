using Methadrenaline.Server.Core;
using Xunit;

namespace Methadrenaline.Server.Tests;

public sealed class FortunePocketAdviceTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void EachChangePointsToAnotherPassageAndTheLastOneIsAnExit(int changes)
    {
        var portals = new[] { "A", "B", "C", "D", "E", "F" };
        var shown = new HashSet<string>();
        var random = new Random(17);
        string? previous = null;
        Assert.Equal("Ладно, вот тут выход.", FortunePocketAdvice.Line(0, false));
        for (int step = 0; step <= changes; step++)
        {
            bool final = step == changes;
            var next = FortunePocketAdvice.Pick(portals, p => p == "F", shown, previous, final, random);
            Assert.NotNull(next);
            Assert.True(shown.Add(next!));
            Assert.Equal(final, next == "F");
            previous = next;
        }
    }

    [Fact]
    public void SmallOrReshuffledPocketUsesTheLiveExit()
    {
        var portals = new[] { "A", "B", "C" };
        var shown = new HashSet<string> { "A", "B" };
        var random = new Random(3);
        Assert.Equal("B", FortunePocketAdvice.Pick(portals, p => p == "C", shown, "A", false, random));
        Assert.Equal("A", FortunePocketAdvice.Pick(portals, p => p == "A", shown, "B", true, random));
        Assert.Null(FortunePocketAdvice.Pick(portals, _ => false, shown, "A", true, random));
    }
}
