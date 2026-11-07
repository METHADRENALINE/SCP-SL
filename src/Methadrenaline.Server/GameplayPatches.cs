using System.Collections.Generic;
using HarmonyLib;
using InventorySystem.Items.Radio;
using LabApi.Features.Wrappers;
using LightContainmentZoneDecontamination;
using PlayerRoles.Spectating;
using PlayerRoles.FirstPersonControl;
using PlayerStatsSystem;
using UnityEngine;
using System;
using System.Reflection;
using NativeRadio = InventorySystem.Items.Radio.RadioItem;
using NativeRadioPickup = InventorySystem.Items.Radio.RadioPickup;

namespace Methadrenaline.Server;

[HarmonyPatch]
internal static class ParavozAutomaticWarheadPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(AlphaWarheadController), "ServerUpdateAutonuke");
        yield return AccessTools.Method(typeof(DeadmanSwitch), "OnUpdate");
        yield return AccessTools.Method(typeof(DeadmanSwitch), nameof(DeadmanSwitch.InitiateProtocol));
    }

    private static bool Prefix() => ServerPlugin.Instance?.IsParavozRound != true;
}

[HarmonyPatch(typeof(DecontaminationController), "set_DecontaminationOverride")]
internal static class ParavozDecontaminationPatch
{
    private static bool Prefix(DecontaminationController.DecontaminationStatus value)
        => value == DecontaminationController.DecontaminationStatus.Disabled || ServerPlugin.Instance?.Paravoz?.Active != true;
}

[HarmonyPatch(typeof(NativeRadio), nameof(NativeRadio.OnAdded))]
internal static class RadioAcquisitionPatch
{
    private static void Postfix(NativeRadio __instance, ref byte ____rangeId)
    {
        if (!Mirror.NetworkServer.active) return;
        __instance.BatteryPercent = 100;
        ____rangeId = (byte)RadioMessages.RadioRangeLevel.UltraRange;
    }
}

[HarmonyPatch(typeof(NativeRadioPickup), "Awake")]
internal static class UltraRangeRadioPickupPatch
{
    private static void Postfix(NativeRadioPickup __instance)
    {
        if (Mirror.NetworkServer.active)
            __instance.NetworkSavedRange = (byte)RadioMessages.RadioRangeLevel.UltraRange;
    }
}

[HarmonyPatch(typeof(Player), "get_CurrentSpectators")]
internal static class ValidSpectatorsPatch
{
    private static void Postfix(Player __instance, List<Player> __result)
        => __result.RemoveAll(p => p.IsDestroyed || p.RoleBase is not SpectatorRole || p.CurrentlySpectating != __instance);
}

[HarmonyPatch(typeof(UltimateHUD.Options), "GetRoleDisplayName")]
internal static class ParavozRoleDisplayPatch
{
    internal const string Color = "#FF4040";
    private static void Postfix(Player __1, ref string __result)
    {
        var mode = ServerPlugin.Instance?.Paravoz;
        if (mode?.IsParavoz(__1) == true) __result = "Paravoz";
        else if (mode?.IsHunter(__1) == true) __result = "Paravoz Hunter";
        __result = RoleHudLocalization.Translate(__1, __result);
    }
}

[HarmonyPatch(typeof(UltimateHUD.Options), nameof(UltimateHUD.Options.GetRoleColor))]
internal static class ParavozRoleColorPatch
{
    private static void Postfix(Player __0, ref string __result)
    {
        if (ServerPlugin.Instance?.Paravoz?.IsParavoz(__0) == true) __result = ParavozRoleDisplayPatch.Color;
    }
}

[HarmonyPatch(typeof(FpcStateProcessor), "get_ServerUseRate")]
internal static class ParavozSilentStaminaPatch
{
    private static readonly AccessTools.FieldRef<FpcStateProcessor, ReferenceHub> Hub =
        AccessTools.FieldRefAccess<FpcStateProcessor, ReferenceHub>("<Hub>k__BackingField");

    private static void Postfix(FpcStateProcessor __instance, ref float __result)
    {
        var mode = ServerPlugin.Instance?.Paravoz;
        var player = Player.Get(Hub(__instance));
        if (Mirror.NetworkServer.active && player != null && mode?.IsParavoz(player) == true && mode.Abilities.Enabled(player)) __result = 0;
    }
}

[HarmonyPatch(typeof(SyncedStatBase), "set_CurValue")]
internal static class NoParavozHealingPatch
{
    private static void Prefix(SyncedStatBase __instance, ref float value)
    {
        var mode = ServerPlugin.Instance?.Paravoz;
        if (__instance is HealthStat && mode?.BlocksHealing(__instance.Hub) == true)
            value = Mathf.Min(value, __instance.CurValue);
    }
}

[HarmonyPatch(typeof(AttackerDamageHandler), "ProcessDamage")]
internal static class ParavozFireFriendlyDamagePatch
{
    private static ref float Multiplier => ref AccessTools.StaticFieldRefAccess<float>(typeof(AttackerDamageHandler), "_ffMultiplier");
    private static void Prefix(AttackerDamageHandler __instance, ReferenceHub ply, out float? __state)
    {
        __state = null;
        var mode = ServerPlugin.Instance?.Paravoz;
        if (__instance is not ExplosionDamageHandler { ExplosionType: ExplosionType.Custom } || mode?.IsParavoz(Player.Get(__instance.Attacker.Hub)) != true || mode.IsParavoz(Player.Get(ply))) return;
        __state = Multiplier;
        Multiplier = 1;
    }
    private static Exception? Finalizer(Exception? __exception, float? __state)
    {
        if (__state.HasValue) Multiplier = __state.Value;
        return __exception;
    }
}
