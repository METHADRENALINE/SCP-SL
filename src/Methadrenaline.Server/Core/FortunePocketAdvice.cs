using System;
using System.Collections.Generic;
using System.Linq;

namespace Methadrenaline.Server.Core;

internal static class FortunePocketAdvice
{
    internal static string Line(int step, bool final) => final ? "Всё, теперь точно сюда." : step switch
    {
        0 => "Ладно, вот тут выход.",
        1 => "Стой, я передумала. Вот сюда.",
        2 => "Погоди, кажется, всё-таки сюда.",
        _ => "А нет, вон тот проход."
    };

    internal static T? Pick<T>(IEnumerable<T> portals, Func<T, bool> isExit, ISet<T> shown,
        T? previous, bool final, Random random) where T : class
    {
        var candidates = portals.Where(portal => isExit(portal) == final).ToArray();
        if (candidates.Length == 0) return null;
        var fresh = candidates.Where(portal => !shown.Contains(portal)).ToArray();
        if (fresh.Length > 0) return fresh[random.Next(fresh.Length)];
        var others = candidates.Where(portal => !EqualityComparer<T>.Default.Equals(portal, previous)).ToArray();
        return others.Length > 0 ? others[random.Next(others.Length)] : candidates[0];
    }
}
