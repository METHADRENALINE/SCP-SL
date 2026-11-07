namespace SecretAPI.Extensions;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

public static class ReflectionExtensions
{
    private static readonly Dictionary<(Type, MethodInfo), string> CachedLongFuncNames = new();

    public static T Cast<T>(this object source)
        where T : class => (T)source;

    public static T2 CastTypeSafely<T1, T2>(this T1 source)
        where T1 : class
        where T2 : T1 => (T2)source;

    public static string GetLongFuncName(Type type, string methodName)
    {
        const BindingFlags methodFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        MethodInfo method = type.GetMethod(methodName, methodFlags) ?? throw new InvalidOperationException($"[ReflectionExtensions.GetLongFuncName] {type.FullName}.{methodName} could not be found.");
        return GetLongFuncName(type, method);
    }

    public static string GetLongFuncName(Type type, MethodInfo method)
    {
        if (CachedLongFuncNames.TryGetValue((type, method), out string? longFunc))
            return longFunc;

        longFunc = $"{method.ReturnType.FullName} {type.FullName}::{method.Name}({string.Join(",", method.GetParameters().Select(x => x.ParameterType.FullName))})";
        CachedLongFuncNames.Add((type, method), longFunc);
        return longFunc;
    }

    public static IEnumerable<MethodInfo> GetMethods(this Assembly assembly, BindingFlags flags)
        => assembly.GetTypes().SelectMany(type => type.GetMethods(flags));

    public static MethodInfo? GetNestedMethod(this Type type, string nestedTypeName, string methodName)
    {
        return type.GetNestedTypes(AccessTools.all)
            .Where(t => t.Name.Contains(nestedTypeName))
            .SelectMany(t => t.GetMethods(AccessTools.all))
            .FirstOrDefault(m => m.Name.Contains(methodName));
    }

    public static void CopyPropertiesTo(this object source, object destination)
    {
        Type type = source.GetType();

        if (type != destination.GetType())
            throw new InvalidOperationException($"[ReflectionExtensions.CopyPropertiesTo] Source and destination types are mismatched: {type.FullName} | {destination.GetType().FullName}");

        foreach (PropertyInfo property in type.GetProperties())
            property.SetValue(destination, property.GetValue(source));
    }
}
