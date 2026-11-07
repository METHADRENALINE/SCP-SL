namespace SecretAPI.Extensions;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LabApi.Features.Console;
using SecretAPI.Attributes;

public static class HarmonyExtensions
{
    extension(Harmony harmony)
    {
        public void PatchCategory(string category, Assembly? assembly = null)
        {
            assembly ??= Assembly.GetCallingAssembly();

            assembly.GetTypes().Where(type =>
                {
                    IEnumerable<HarmonyPatchCategory> categories = type.GetCustomAttributes<HarmonyPatchCategory>();
                    return categories.Any(c => c.Category == category);
                })
                .Do(type => SafePatch(harmony, type));
        }

        public void PatchAllNoCategory(Assembly? assembly = null)
        {
            assembly ??= Assembly.GetCallingAssembly();

            assembly.GetTypes().Where(type =>
                {
                    IEnumerable<HarmonyPatchCategory> categories = type.GetCustomAttributes<HarmonyPatchCategory>();
                    return !categories.Any();
                })
                .Do(type => SafePatch(harmony, type));
        }

        public void SafePatch(Type type)
        {
            try
            {
                harmony.CreateClassProcessor(type).Patch();
            }
            catch (Exception ex)
            {
                Logger.Error($"[HarmonyExtensions] failed to safely patch {harmony.Id} ({type.FullName}): {ex}");
            }
        }
    }
}
