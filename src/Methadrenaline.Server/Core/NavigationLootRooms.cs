using System;
using System.Collections.Generic;
using System.Linq;

namespace Methadrenaline.Server.Core;

internal static class NavigationLootRooms
{
    internal const int PerZone = 5;

    internal static (T[] Coins, T[] Maps) Allocate<T>(T[] shuffledRooms, Func<T, bool> walkway, bool allowMaps)
    {
        int coinCount = Math.Min(PerZone, allowMaps ? (shuffledRooms.Length + 1) / 2 : shuffledRooms.Length);
        var coins = shuffledRooms.Where(walkway).Take(3).Concat(shuffledRooms).Distinct().Take(coinCount).ToArray();
        var maps = allowMaps ? shuffledRooms.Except(coins).Take(PerZone).ToArray() : Array.Empty<T>();
        return (coins, maps);
    }
}
