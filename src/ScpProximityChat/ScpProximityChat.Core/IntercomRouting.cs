using System.Reflection.Emit;
using HarmonyLib;
using PlayerRoles;
using PlayerRoles.Voice;
using VoiceChat;

namespace ScpProximityChat.Core;

[HarmonyPatch(typeof(Intercom))]
internal static class IntercomRouting
{
    [HarmonyTranspiler]
    [HarmonyPatch("CheckPlayer")]
    private static IEnumerable<CodeInstruction> AllowLocalScp(IEnumerable<CodeInstruction> instructions)
    {
        var humanCheck = AccessTools.Method(typeof(PlayerRolesUtils), nameof(PlayerRolesUtils.IsHuman), [typeof(ReferenceHub)]);
        return new CodeMatcher(instructions)
            .MatchStartForward(new CodeMatch(OpCodes.Call, humanCheck))
            .ThrowIfInvalid("The intercom role check was not found.")
            .SetOperandAndAdvance(AccessTools.Method(typeof(IntercomRouting), nameof(CanUseIntercom)))
            .InstructionEnumeration();
    }

    private static bool CanUseIntercom(ReferenceHub hub) =>
        hub.IsHuman() || Player.Get(hub) is { IsSCP: true } player && IsSpeakingLocally(player);

    private static bool IsSpeakingLocally(Player player) =>
        player.IsProximityChatEnabled() && player.VoiceModule?.CurrentChannel == VoiceChatChannel.Proximity;

    [HarmonyPostfix]
    [HarmonyPatch(nameof(Intercom.CheckPerms))]
    private static void StopWhenSwitchingToTeamChat(ReferenceHub hub, ref bool __result)
    {
        if (__result && Player.Get(hub) is { IsSCP: true } player)
            __result = IsSpeakingLocally(player);
    }
}
