namespace SecretAPI.Patches.Features.Settings;

using HarmonyLib;
using LabApi.Features.Wrappers;
using SecretAPI.Attributes;
using SecretAPI.Features.UserSettings;
using UserSettings.ServerSpecific;

[HarmonyPatchCategory(nameof(CustomSetting))]
[HarmonyPatch(typeof(ServerSpecificSettingsSync), nameof(ServerSpecificSettingsSync.SendToPlayer), typeof(ReferenceHub))]
internal static class SendSettingsPlayerSync
{
    private static bool Prefix(ReferenceHub hub)
    {
        CustomSetting.SendSettingsToPlayer(Player.Get(hub));
        return false;
    }
}
