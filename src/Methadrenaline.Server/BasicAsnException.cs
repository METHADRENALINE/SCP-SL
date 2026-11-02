using System.Collections.Generic;
using System.Reflection.Emit;
using CentralAuth;
using HarmonyLib;
using Methadrenaline.Server.Core;

namespace Methadrenaline.Server;

// CheckBans runs after signed-token, challenge, identity and version validation.
// Replace only its reading of the ban reason, without mutating the signed token
// or skipping the remaining local bans, IP matching and mute checks.
[HarmonyPatch(typeof(PlayerAuthenticationManager), "CheckBans")]
internal static class BasicAsnException
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var field = AccessTools.Field(typeof(AuthenticationToken), nameof(AuthenticationToken.GlobalBan));
        var read = AccessTools.Method(typeof(BasicAsnException), nameof(EffectiveBan));
        int replacements = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, field))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = read;
                replacements++;
            }
            yield return instruction;
        }
        if (replacements == 0) throw new System.InvalidOperationException("CheckBans ban field was not found; ASN exception cannot be installed.");
    }

    public static string EffectiveBan(AuthenticationToken token)
        => BasicAsnPolicy.Allows(token.UserId, token.Asn, token.GlobalBan,
            ServerPlugin.Instance?.Config.BasicAsnExemptUserIds ?? System.Array.Empty<string>()) ? "NO" : token.GlobalBan;
}
