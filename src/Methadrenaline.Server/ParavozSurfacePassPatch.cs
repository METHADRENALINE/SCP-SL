using HarmonyLib;
using InventorySystem;
using InventorySystem.Items;
using InventorySystem.Searching;
using LabApi.Features.Wrappers;
using System.Linq;

namespace Methadrenaline.Server;

[HarmonyPatch(typeof(InventoryExtensions), nameof(InventoryExtensions.ServerAddItem))]
internal static class NoParavozSurfacePassGrantPatch
{
    private static bool Prefix(Inventory __0, ItemType type, ref ItemBase __result)
    {
        var mode = ServerPlugin.Instance?.Paravoz;
        if (mode?.Active != true) return true;
        bool denied = type == ItemType.SurfaceAccessPass;
        if (mode.IsParavoz(Player.Get(__0.gameObject)))
            denied |= type != ItemType.Radio || __0.UserInventory.Items.Values.Any(i => i.ItemTypeId == ItemType.Radio);
        if (!denied) return true;
        __result = null!;
        return false;
    }
}

[HarmonyPatch(typeof(PickupSearchCompletor), "ValidateAny")]
internal static class ParavozRadioOnlyPickupPatch
{
    private static bool Prefix(PickupSearchCompletor __instance, ref bool __result)
    {
        if (ServerPlugin.Instance?.Paravoz?.IsParavoz(Player.Get(__instance.Hub)) != true) return true;
        if (__instance.TargetPickup != null && __instance.TargetPickup.Info.ItemId == ItemType.Radio &&
            !__instance.Hub.inventory.UserInventory.Items.Values.Any(i => i.ItemTypeId == ItemType.Radio)) return true;
        __result = false;
        return false;
    }
}
