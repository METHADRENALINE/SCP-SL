namespace SecretAPI.Features;

using System.Collections.Generic;

public static class Registry<T>
{
    public static List<T> Registered { get; } = [];

    public static int CurrentId { get; set; }
}
