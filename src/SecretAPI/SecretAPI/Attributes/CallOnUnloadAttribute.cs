namespace SecretAPI.Attributes;

using System;
using System.Reflection;
using SecretAPI.Features;

[AttributeUsage(AttributeTargets.Method)]
public class CallOnUnloadAttribute : Attribute, IPriority
{
    public CallOnUnloadAttribute(int priority = 0)
    {
        Priority = priority;
    }

    public int Priority { get; }

    public static void Unload(Assembly? assembly = null)
    {
        assembly ??= Assembly.GetCallingAssembly();
        CallOnLoadAttribute.CallAttributeMethodPriority<CallOnUnloadAttribute>(assembly);
    }

    public override bool Equals(object? obj) => obj == this;

    public override int GetHashCode() => base.GetHashCode();
}
