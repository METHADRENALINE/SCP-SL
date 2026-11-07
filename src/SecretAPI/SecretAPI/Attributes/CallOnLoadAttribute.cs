namespace SecretAPI.Attributes;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SecretAPI.Extensions;
using SecretAPI.Features;

[AttributeUsage(AttributeTargets.Method)]
public class CallOnLoadAttribute : Attribute, IPriority
{
    public CallOnLoadAttribute(int priority = 0)
    {
        Priority = priority;
    }

    public int Priority { get; }

    public static void Load(Assembly? assembly = null)
    {
        assembly ??= Assembly.GetCallingAssembly();
        CallAttributeMethodPriority<CallOnLoadAttribute>(assembly);
    }

    public override bool Equals(object? obj) => obj == this;

    public override int GetHashCode() => base.GetHashCode();

    internal static void CallAttributeMethodPriority<TAttribute>(Assembly assembly)
        where TAttribute : Attribute, IPriority
    {
        Dictionary<TAttribute, MethodInfo> methods = new();
        foreach (MethodInfo method in assembly.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            TAttribute? attribute = method.GetCustomAttribute<TAttribute>();
            if (attribute == null)
                continue;

            methods.Add(attribute, method);
        }

        foreach (KeyValuePair<TAttribute, MethodInfo> method in methods.OrderBy(static v => v.Key.Priority))
            method.Value.Invoke(null, null);
    }
}
