namespace SecretAPI.Features;

using System;
using System.Collections.Generic;
using System.Reflection;

public interface IRegister
{
    private static Dictionary<Assembly, List<IRegister>> registerables = new();

    public void TryRegister();

    public void TryUnregister()
    {
    }

    public static void RegisterAll(Assembly? assembly = null)
    {
        assembly ??= Assembly.GetCallingAssembly();

        registerables.TryAdd(assembly, new());

        foreach (Type type in assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface)
                continue;

            if (!typeof(IRegister).IsAssignableFrom(type))
                continue;

            object obj = Activator.CreateInstance(type);
            if (obj is not IRegister register)
                continue;

            registerables[assembly].Add(register);
            register.TryRegister();
        }
    }

    public static void UnRegisterAll(Assembly? assembly = null)
    {
        assembly ??= Assembly.GetCallingAssembly();

        foreach (IRegister register in registerables[assembly])
            register.TryUnregister();

        registerables.Remove(assembly);
    }
}
