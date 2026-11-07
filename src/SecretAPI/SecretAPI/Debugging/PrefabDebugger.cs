#if DEBUG

namespace SecretAPI.Debugging;

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using LabApi.Events.Handlers;
using LabApi.Loader.Features.Paths;
using Mirror;
using SecretAPI.Attributes;
using SecretAPI.Features;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;

internal static class PrefabDebugger
{
    [CallOnLoad]
    internal static void Load()
    {
        ServerEvents.WaitingForPlayers += OnWaiting;
    }

    private static void OnWaiting()
    {
        List<string> logTexts = new();
        foreach (PropertyInfo properties in typeof(PrefabManager).GetProperties())
        {
            try
            {
                if (properties.GetValue(null) == null)
                    Logger.Error($"[PrefabDebugging] {properties.Name} returned a null value!");
            }
            catch (Exception ex)
            {
                Logger.Error($"[PrefabDebugging] {properties.Name} ran into an exception: {ex}");
            }
        }

        foreach (KeyValuePair<uint, GameObject> pair in NetworkClient.prefabs)
        {
            string txt = $"Prefab Key ({pair.Key}) - Value ({pair.Value.name})";
            logTexts.Add(txt);
        }

        File.WriteAllLines(Path.Combine(SecretApi.ConfigDirectory.FullName, "debug_prefabs.txt"), logTexts);
    }
}

#endif
