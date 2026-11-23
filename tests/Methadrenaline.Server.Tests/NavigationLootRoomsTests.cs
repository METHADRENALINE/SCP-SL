using Methadrenaline.Server.Core;
using Xunit;

namespace Methadrenaline.Server.Tests;

public class NavigationLootRoomsTests
{
    [Theory]
    [InlineData(25, true, 5, 5)]
    [InlineData(10, true, 5, 5)]
    [InlineData(9, true, 5, 4)]
    [InlineData(6, true, 3, 3)]
    [InlineData(25, false, 5, 0)]
    [InlineData(3, false, 3, 0)]
    public void EachRoomGetsAtMostOnePickup(int count, bool mapsAllowed, int coinCount, int mapCount)
    {
        var rooms = Enumerable.Range(0, count).ToArray();
        for (int seed = 0; seed < 100; seed++)
        {
            var random = new Random(seed);
            var shuffled = rooms.OrderBy(_ => random.Next()).ToArray();
            var loot = NavigationLootRooms.Allocate(shuffled, room => room % 2 == 0, mapsAllowed);
            Assert.Equal(coinCount, loot.Coins.Length);
            Assert.Equal(mapCount, loot.Maps.Length);
            Assert.Equal(coinCount + mapCount, loot.Coins.Concat(loot.Maps).Distinct().Count());
        }
    }
}
