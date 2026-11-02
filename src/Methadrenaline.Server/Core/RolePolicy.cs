using System;
using System.Collections.Generic;
using System.Linq;

namespace Methadrenaline.Server.Core;

public static class RolePolicy
{
    public static int ScpCount(int participants, int willingPlayers)
        => Math.Min(Math.Max(0, willingPlayers), participants < 2 ? 0 : 1);

    // Shuffle players before assigning their best available role. When two players
    // want the same role, either player has exactly the same chance to choose first.
    public static IReadOnlyDictionary<int, int> Allocate(
        IReadOnlyList<IReadOnlyDictionary<int, int>> preferences,
        IReadOnlyList<int> roles,
        Random random,
        Func<int, IReadOnlyCollection<int>, bool>? allowed = null)
    {
        if (preferences.Count > 2) throw new ArgumentOutOfRangeException(nameof(preferences), "This policy supports at most two SCP players.");
        var order = Enumerable.Range(0, preferences.Count).ToArray();
        for (int i = order.Length - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        var assignments = new Dictionary<int, int>();
        var used = new HashSet<int>();
        var empty = Array.Empty<int>();
        bool PairAllowed(int a, int b) => allowed == null ||
            allowed(a, empty) && allowed(b, new[] { a }) ||
            allowed(b, empty) && allowed(a, new[] { b });
        bool Compatible(int role)
        {
            if (allowed == null) return true;
            if (preferences.Count == 1) return allowed(role, empty);
            // Support SCPs such as 079 must remain selectable as a first preference
            // when a compatible companion can legally spawn before them.
            return used.Count == 0
                ? roles.Any(other => other != role && PairAllowed(role, other))
                : PairAllowed(used.Single(), role);
        }
        foreach (int index in order)
        {
            var available = roles.Distinct().Where(r => !used.Contains(r) && Compatible(r)).ToArray();
            if (available.Length == 0)
                throw new InvalidOperationException("No compatible SCP role remains.");

            int Score(int role) => preferences[index].TryGetValue(role, out int value) ? Math.Max(-5, Math.Min(5, value)) : 0;
            int best = available.Max(Score);
            var choices = available.Where(r => Score(r) == best).ToArray();
            int selected = choices[random.Next(choices.Length)];
            assignments.Add(index, selected);
            used.Add(selected);
        }
        return assignments;
    }
}
