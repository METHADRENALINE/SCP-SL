using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using HarmonyLib;
using Hints;
using LabApi.Features.Wrappers;
using Mirror;
using RemoteAdmin;
using RueI.API.Elements;
using RueI.API.Parsing;
using UserSettings.ServerSpecific;

namespace Methadrenaline.Server;

internal static class Localizer
{
    internal static LocalizationService? Current => ServerPlugin.Instance?.Localization;
    internal static string Text(ReferenceHub? hub, string text) => Current?.Text(hub, text) ?? text;
    internal static ReferenceHub? Hub(NetworkConnection? connection) => connection?.identity != null ? ReferenceHub.GetHub(connection.identity.gameObject) : null;
}

[HarmonyPatch(typeof(HintDisplay), nameof(HintDisplay.Show))]
internal static class LocalizedHintPatch
{
    // RueI stores the source for the target and its spectators. Translate at its
    // per-viewer rendering stage, or here only when it leaves native delivery on.
    [HarmonyPriority(Priority.Last)]
    private static void Prefix(HintDisplay __instance, ref Hint hint, bool __runOriginal)
    {
        if (__runOriginal && Localizer.Current is { } localization) hint = localization.HintFor(ReferenceHub.GetHub(__instance.gameObject), hint);
    }
}

[HarmonyPatch(typeof(Broadcast), nameof(Broadcast.TargetAddElement))]
internal static class LocalizedTargetBroadcastPatch
{
    private static void Prefix(NetworkConnection conn, ref string data) => data = Localizer.Text(Localizer.Hub(conn), data);
}

[HarmonyPatch(typeof(Broadcast), nameof(Broadcast.RpcAddElement))]
internal static class LocalizedGlobalBroadcastPatch
{
    private static bool Prefix(Broadcast __instance, string data, ushort time, Broadcast.BroadcastFlags flags)
    {
        if (Localizer.Current == null || !NetworkServer.active) return true;
        foreach (Player player in Player.List.Where(p => !p.IsHost && !p.IsNpc && p.ReferenceHub.connectionToClient != null))
            __instance.TargetAddElement(player.ReferenceHub.connectionToClient, data, time, flags);
        return false;
    }
}

[HarmonyPatch(typeof(GameConsoleTransmission), nameof(GameConsoleTransmission.SendToClient))]
internal static class LocalizedGameConsolePatch
{
    private static void Prefix(GameConsoleTransmission __instance, ref string text) => text = Localizer.Text(ReferenceHub.GetHub(__instance.gameObject), text);
}

[HarmonyPatch(typeof(PlayerCommandSender), nameof(PlayerCommandSender.RaReply))]
internal static class LocalizedRemoteAdminPatch
{
    private static void Prefix(PlayerCommandSender __instance, ref string text)
    {
        // RA's COMMAND# prefix is a protocol field, not human-readable content.
        int separator = text.IndexOf('#');
        text = separator >= 0 && Regex.IsMatch(text.Substring(0, separator), @"\A[A-Za-z0-9_.:-]+\z") ? text.Substring(0, separator + 1) + Localizer.Text(__instance.ReferenceHub, text.Substring(separator + 1)) : Localizer.Text(__instance.ReferenceHub, text);
    }
}

[HarmonyPatch(typeof(PlayerCommandSender), nameof(PlayerCommandSender.Print))]
internal static class LocalizedRemotePrintPatch
{
    private static void Prefix(PlayerCommandSender __instance, ref string text) => text = Localizer.Text(__instance.ReferenceHub, text);
}

[HarmonyPatch(typeof(ServerSpecificSettingsSync), nameof(ServerSpecificSettingsSync.SendToPlayer), new[] { typeof(ReferenceHub), typeof(ServerSpecificSettingBase[]), typeof(int?) })]
internal static class LocalizedSettingsPatch
{
    private static void Prefix(ReferenceHub hub, ref ServerSpecificSettingBase[] collection)
    {
        if (Localizer.Current is { } localization) collection = localization.SettingsFor(hub, collection);
    }
}

[HarmonyPatch(typeof(ServerSpecificSettingsSync), nameof(ServerSpecificSettingsSync.SendToPlayersConditionally))]
internal static class LocalizedConditionalSettingsPatch
{
    private static bool Prefix(Func<ReferenceHub, bool> filter)
    {
        if (Localizer.Current == null || !NetworkServer.active) return true;
        foreach (Player player in Player.List.Where(p => !p.IsHost && !p.IsNpc && filter(p.ReferenceHub)))
            SecretAPI.Features.UserSettings.CustomSetting.SendSettingsToPlayer(player);
        return false;
    }
}

[HarmonyPatch(typeof(ServerSpecificSettingBase), nameof(ServerSpecificSettingBase.SendUpdate), new[] { typeof(string), typeof(string), typeof(bool), typeof(Func<ReferenceHub, bool>) })]
internal static class LocalizedSettingUpdatePatch
{
    private static readonly MethodInfo Send = AccessTools.Method(typeof(ServerSpecificSettingBase), "SendUpdate", new[] { typeof(Func<ReferenceHub, bool>), typeof(Action<NetworkWriter>), typeof(bool), typeof(string), typeof(string) });
    private static bool Prefix(ServerSpecificSettingBase __instance, string newLabel, string newHintDescription, bool applyOverride, Func<ReferenceHub, bool>? receiveFilter)
    {
        if (Localizer.Current == null || !NetworkServer.active) return true;
        if (applyOverride)
        {
            AccessTools.Property(typeof(ServerSpecificSettingBase), "Label").SetValue(__instance, newLabel);
            AccessTools.Property(typeof(ServerSpecificSettingBase), "HintDescription").SetValue(__instance, newHintDescription);
        }
        foreach (Player player in Player.List.Where(p => !p.IsHost && !p.IsNpc && (receiveFilter == null || receiveFilter(p.ReferenceHub))))
        {
            ReferenceHub hub = player.ReferenceHub;
            Func<ReferenceHub, bool> onlyThisPlayer = target => target == hub;
            Send.Invoke(__instance, new object[] { onlyThisPlayer, null!, true, Localizer.Text(hub, newLabel), Localizer.Text(hub, newHintDescription) });
        }
        return false;
    }
}

[HarmonyPatch(typeof(ServerSpecificSettingsSync), "set_DefinedSettings")]
internal static class PreserveSettingsDuringMapEditingPatch
{
    [HarmonyPriority(Priority.First)]
    private static void Prefix(ref ServerSpecificSettingBase[] value)
    {
        if (Localizer.Current == null || value == null || value.Length != 2 || value[0] is not SSGroupHeader || value[0].Label != "MapEditorReborn" || value[1] is not SSDropdownSetting || value[1].SettingId != 0 || value[1].Label != "Schematic Name") return;
        // ProjectMER 2026.7.6.1 replaces the entire registry with its two entries.
        // Merge this particular registration without changing normal replacement
        // and removal semantics used by other plugins.
        value = (ServerSpecificSettingsSync.DefinedSettings ?? Array.Empty<ServerSpecificSettingBase>()).Where(s => !(s is SSGroupHeader && s.Label == "MapEditorReborn") && !(s is SSDropdownSetting && s.SettingId == 0 && s.Label == "Schematic Name")).Concat(value).ToArray();
    }
}

/// <summary>Translate before RueI parses layout and measures text. Its elements
/// can be shared by players and spectators; their source and caches stay intact.</summary>
[HarmonyPatch]
internal static class LocalizedRueElementPatch
{
    private sealed class CachedSource { internal float Until = float.NegativeInfinity; internal string Text = ""; }
    private static readonly ConditionalWeakTable<CachedElement, CachedSource> Sources = new();
    private static readonly Func<string, Element, ParsedData> Parse = (Func<string, Element, ParsedData>)Delegate.CreateDelegate(typeof(Func<string, Element, ParsedData>), AccessTools.Method(AccessTools.TypeByName("RueI.API.Parsing.Parser"), "Parse", new[] { typeof(string), typeof(Element) }));
    private static readonly FieldInfo Content = AccessTools.Field(typeof(BasicElement), "content");
    private static readonly PropertyInfo Getter = AccessTools.Property(typeof(DynamicElement), "ContentGetter");
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(BasicElement), "GetParsedData");
        yield return AccessTools.Method(typeof(DynamicElement), "GetParsedData");
        yield return AccessTools.Method(typeof(CachedElement), "GetParsedData");
    }
    private static bool Prefix(Element __instance, ReferenceHub hub, ref ParsedData __result)
    {
        if (Localizer.Current == null) return true;
        string source;
        if (__instance is BasicElement) source = (string)Content.GetValue(__instance)!;
        else if (__instance is CachedElement cached)
        {
            var stored = Sources.GetValue(cached, _ => new CachedSource());
            if (stored.Until < UnityEngine.Time.timeSinceLevelLoad)
            {
                stored.Text = ((Func<ReferenceHub, string>)Getter.GetValue(__instance)!)(hub);
                stored.Until = UnityEngine.Time.timeSinceLevelLoad + (float)cached.CacheTime.TotalSeconds;
            }
            source = stored.Text;
        }
        else source = ((Func<ReferenceHub, string>)Getter.GetValue(__instance)!)(hub);
        __result = Parse(Localizer.Text(hub, source), __instance);
        return false;
    }
}
