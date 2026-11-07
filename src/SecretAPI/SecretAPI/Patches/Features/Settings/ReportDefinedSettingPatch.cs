namespace SecretAPI.Patches.Features.Settings;

using System;
using System.Linq;
using HarmonyLib;
using LabApi.Features.Console;
using SecretAPI.Attributes;
using SecretAPI.Features.UserSettings;
using UserSettings.ServerSpecific;

[HarmonyPatchCategory(nameof(CustomSetting))]
[HarmonyPatch(typeof(ServerSpecificSettingsSync), nameof(ServerSpecificSettingsSync.DefinedSettings), MethodType.Setter)]
internal static class ReportDefinedSettingPatch
{
    private static void Prefix(ref ServerSpecificSettingBase[] value)
    {
        value = SettingBatchPolicy.Filter(
            value ?? Array.Empty<ServerSpecificSettingBase>(),
            setting => setting is SSGroupHeader ? null : setting.SettingId,
            CustomSetting.CustomSettings.Select(setting => setting.Id),
            setting => Logger.Error($"Setting {setting.Label} has a duplicate ID ({setting.SettingId}).")).ToArray();
    }
}
