using System;
using System.Linq;
using HarmonyLib;
using LabApi.Features.Wrappers;
using SwiftArcadeMode.Features;
using SwiftArcadeMode.Features.Game.Modes;
using SwiftArcadeMode.Features.Humans.Perks;
using SwiftArcadeMode.Features.Humans.Perks.Content.Caster;

namespace Methadrenaline.Server;

internal static class ArcadeContentPolicy
{
    internal static bool IsExcluded(Type? perk) => perk != null && typeof(CasterBase).IsAssignableFrom(perk);

    internal static void Apply()
    {
        FilterPerks();
        FilterModes();
    }

    internal static void FilterPerks()
    {
        foreach (string id in PerkManager.RegisteredPerks.Where(p => IsExcluded(p.Value.Perk)).Select(p => p.Key).ToArray())
            PerkManager.RegisteredPerks.Remove(id);
    }

    internal static void FilterModes() => GameModeManager.Registry.RemoveAll(t => typeof(CasterBattle).IsAssignableFrom(t));
}

[HarmonyPatch(typeof(PerkManager), nameof(PerkManager.RegisterPerks))]
internal static class FilterArcadeClassesPatch
{
    private static void Postfix() => ArcadeContentPolicy.FilterPerks();
}

[HarmonyPatch(typeof(GameModeManager), nameof(GameModeManager.RegisterModes))]
internal static class FilterCasterBattlePatch
{
    private static void Postfix() => ArcadeContentPolicy.FilterModes();
}

[HarmonyPatch(typeof(GameModeManager), "set_Current")]
internal static class BlockCasterBattlePatch
{
    private static bool Prefix(GameModeBase value) => value is not CasterBattle;
}

[HarmonyPatch(typeof(PerkInventory), nameof(PerkInventory.AddPerk))]
internal static class BlockArcadeClassGrantsPatch
{
    private static bool Prefix(PerkAttribute type, ref bool __result)
    {
        if (!ArcadeContentPolicy.IsExcluded(type?.Perk)) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(PerkSpawner), nameof(PerkSpawner.SpawnPerk))]
internal static class BlockArcadeClassPickupsPatch
{
    private static bool Prefix(PerkAttribute ty, ref Pickup __result)
    {
        if (!ArcadeContentPolicy.IsExcluded(ty?.Perk)) return true;
        __result = null!;
        return false;
    }
}
