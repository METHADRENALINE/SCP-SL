namespace SecretAPI.Patches.Features.Settings;

using HarmonyLib;
using SecretAPI.Attributes;
using SecretAPI.Features.UserSettings;
using UserSettings.ServerSpecific;

[HarmonyPatchCategory(nameof(CustomSetting))]
[HarmonyPatch(typeof(ServerSpecificSettingsSync), nameof(ServerSpecificSettingsSync.SendToAll))]
internal static class SendSettingsServerSync
{
    private static bool Prefix()
    {
        CustomSetting.ResyncServer();
        return false;
    }
}
