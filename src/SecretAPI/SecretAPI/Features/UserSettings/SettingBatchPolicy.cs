namespace SecretAPI.Features.UserSettings;

using System;
using System.Collections.Generic;

/// <summary>Checks one replacement batch, independent of the previously assigned array.</summary>
internal static class SettingBatchPolicy
{
    internal static List<T> Filter<T>(IEnumerable<T> incoming, Func<T, int?> getId, IEnumerable<int> reserved, Action<T> duplicate)
    {
        var used = new HashSet<int>(reserved);
        var accepted = new List<T>();
        foreach (T setting in incoming)
        {
            int? id = getId(setting);
            if (id.HasValue && !used.Add(id.Value))
            {
                duplicate(setting);
                continue;
            }

            accepted.Add(setting);
        }

        return accepted;
    }
}
