namespace SecretAPI.Attributes;

using System;
using SecretAPI.Extensions;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class HarmonyPatchCategory : Attribute
{
    public HarmonyPatchCategory(string category)
    {
        Category = category;
    }

    public string Category { get; }
}
