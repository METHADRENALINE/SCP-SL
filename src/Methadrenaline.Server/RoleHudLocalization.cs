using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LabApi.Features.Wrappers;
using PlayerRoles;

namespace Methadrenaline.Server;

internal static class RoleHudLocalization
{
    [ThreadStatic] internal static ReferenceHub? Viewer;

    internal static string Translate(Player subject, string label)
    {
        if (subject.Role == RoleTypeId.None) return "";
        var localization = Localizer.Current;
        if (localization == null) return label;
        if (label == subject.Role.ToString()) label = subject.Role switch
        {
            RoleTypeId.CustomRole => "Custom role",
            RoleTypeId.Destroyed => "Destroyed role",
            RoleTypeId.AlphaFlamingo => "Alpha Flamingo",
            RoleTypeId.ZombieFlamingo => "Zombie Flamingo",
            RoleTypeId.NtfFlamingo => "MTF Flamingo",
            RoleTypeId.ChaosFlamingo => "CI Flamingo",
            _ => label
        };
        return localization.Catalog.Translate(label, localization.Language(Viewer ?? subject.ReferenceHub));
    }

    internal static void Refresh(ReferenceHub hub)
    {
        var player = Player.Get(hub);
        if (player == null || player.IsDestroyed) return;
        UltimateHUD.Hints.RefreshPlayerInfo(player);
        UltimateHUD.Hints.RefreshSpectatorPlayerInfo(player);
    }
}

[HarmonyPatch]
internal static class RoleHudViewerPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(UltimateHUD.Hints), nameof(UltimateHUD.Hints.RefreshPlayerInfo));
        yield return AccessTools.Method(typeof(UltimateHUD.Hints), nameof(UltimateHUD.Hints.RefreshSpectatorPlayerInfo));
    }
    private static void Prefix(Player __0, out ReferenceHub? __state)
    {
        __state = RoleHudLocalization.Viewer;
        RoleHudLocalization.Viewer = __0?.ReferenceHub;
    }
    private static Exception? Finalizer(Exception? __exception, ReferenceHub? __state)
    {
        RoleHudLocalization.Viewer = __state;
        return __exception;
    }
}
