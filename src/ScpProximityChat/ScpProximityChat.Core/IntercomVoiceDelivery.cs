using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using Mirror;
using VoiceChat.Networking;

namespace ScpProximityChat.Core;

[HarmonyPatch(typeof(VoiceTransceiver), "ServerReceiveMessage")]
internal static class IntercomVoiceDelivery
{
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> UseIntercomRelay(IEnumerable<CodeInstruction> instructions)
    {
        var send = typeof(NetworkConnection).GetMethods().Single(method =>
            method.Name == nameof(NetworkConnection.Send) && method.IsGenericMethodDefinition &&
            method.GetParameters().Length == 2).MakeGenericMethod(typeof(VoiceMessage));
        return new CodeMatcher(instructions)
            .MatchStartForward(new CodeMatch(OpCodes.Callvirt, send))
            .ThrowIfInvalid("The voice packet delivery call was not found.")
            .SetInstruction(new CodeInstruction(OpCodes.Call,
                AccessTools.Method(typeof(IntercomSpeech), nameof(IntercomSpeech.Send))))
            .InstructionEnumeration();
    }
}
