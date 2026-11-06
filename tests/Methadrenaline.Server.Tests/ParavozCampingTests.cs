using Methadrenaline.Server.Core;
using Xunit;

namespace Methadrenaline.Server.Tests;

public class ParavozCampingTests
{
    [Theory]
    [InlineData(1, 25, 35)]
    [InlineData(2, 25, 35)]
    [InlineData(3, 40, 50)]
    [InlineData(4, 55, 65)]
    public void AlternatingRoomsKeepTheCampingClock(int rooms, int warningAt, int damageAt)
    {
        var tracker = new ParavozCamping<int>();
        for (int second = 0; second < warningAt; second++)
            Assert.Null(tracker.Observe(second % rooms, second));
        for (int second = warningAt; second <= damageAt + 2; second++)
            Assert.Equal((rooms, (float)damageAt), tracker.Observe(second % rooms, second));
    }

    [Fact]
    public void ARouteThroughFiveRoomsIsNotCamping()
    {
        var tracker = new ParavozCamping<int>();
        for (int second = 0; second < 360; second++)
            Assert.Null(tracker.Observe(second % 5, second));
    }

    [Fact]
    public void NewRoomsExtendTheDeadlineAndAFullRouteClearsIt()
    {
        var tracker = new ParavozCamping<int>();
        tracker.Observe(0, 0);
        Assert.Equal((1, 35f), tracker.Observe(0, 25));
        Assert.Equal((2, 35f), tracker.Observe(1, 26));
        Assert.Null(tracker.Observe(2, 27));
        Assert.Equal((3, 50f), tracker.Observe(2, 40));
        Assert.Null(tracker.Observe(3, 41));
        for (int second = 42; second < 55; second++)
            Assert.Null(tracker.Observe(second % 4, second));
        Assert.Equal((4, 65f), tracker.Observe(3, 55));
        Assert.Null(tracker.Observe(4, 56));
    }

    [Fact]
    public void ReturningToAFamiliarRoomDoesNotResetTheClock()
    {
        var tracker = new ParavozCamping<int>();
        tracker.Observe(0, 0);
        tracker.Observe(1, 17);
        Assert.Equal((2, 35f), tracker.Observe(0, 27));
        Assert.Equal((2, 35f), tracker.Observe(1, 35));
    }
}
