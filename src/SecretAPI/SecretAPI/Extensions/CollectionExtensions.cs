namespace SecretAPI.Extensions;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Random = UnityEngine.Random;

public static class CollectionExtensions
{
    extension<T>(IEnumerable<T> collection)
    {
        public T GetRandomValue()
        {
            TryGetRandomValue(collection, out T? value);
            return value!;
        }

        public bool TryGetRandomValue([NotNullWhen(true)] out T? value)
        {
            IList<T> list = collection as IList<T> ?? collection.ToList();
            if (list.Count == 0)
            {
                value = default;
                return false;
            }

            value = list[Random.Range(0, list.Count)];
            return value != null;
        }
    }
}
