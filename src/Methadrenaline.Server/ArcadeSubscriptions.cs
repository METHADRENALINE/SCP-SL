using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LabApi.Events.Handlers;
using ArcadeCore = SwiftArcadeMode.Core;

namespace Methadrenaline.Server;

internal static class ArcadeSubscriptions
{
    private static readonly (EventInfo Event, FieldInfo Field)[] Events = FindEvents();

    private static (EventInfo, FieldInfo)[] FindEvents()
    {
        var owners = typeof(PlayerEvents).Assembly.GetTypes().Where(t => t.Namespace == "LabApi.Events.Handlers").ToList();
        foreach (string name in new[] { "LabApi.Features.StaticUnityMethods", "UserSettings.ServerSpecific.ServerSpecificSettingsSync", "Interactables.Interobjects.DoorUtils.DoorEvents", "ProjectMER.Events.Handlers.Schematic", "Shutdown", "MEC.Shutdown" })
            if (AccessTools.TypeByName(name) is Type owner) owners.Add(owner);
        owners.AddRange(typeof(ArcadeCore).Assembly.GetTypes().Where(t => t.IsAbstract && t.IsSealed));
        var result = new List<(EventInfo, FieldInfo)>();
        foreach (var owner in owners.Distinct().Where(t => !t.ContainsGenericParameters))
            foreach (var evt in owner.GetEvents(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var field = owner.GetField(evt.Name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null && typeof(Delegate).IsAssignableFrom(field.FieldType)) result.Add((evt, field));
            }
        return result.ToArray();
    }

    internal static int Count() => Events.Sum(pair => ((Delegate?)pair.Field.GetValue(null))?.GetInvocationList().Count(IsArcade) ?? 0);

    internal static void Clear()
    {
        foreach (var pair in Events)
            foreach (var callback in (((Delegate?)pair.Field.GetValue(null))?.GetInvocationList() ?? Array.Empty<Delegate>()).Where(IsArcade))
                pair.Event.GetRemoveMethod(true)!.Invoke(null, new object[] { callback });
    }

    private static bool IsArcade(Delegate callback) => callback.Method.Module.Assembly == typeof(ArcadeCore).Assembly;
}
