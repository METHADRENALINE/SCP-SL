using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Mirror;

namespace Methadrenaline.Server;

[HarmonyPatch]
internal static class SiteMapChunkingPatch
{
    private static MethodBase TargetMethod() => AccessTools.Method("RueI.API.Parsing.ElementCombiner:Combine");

    // RueI 3.1.2 includes the separately written anchor prefix in the loop's
    // source length. That reads past the written body into pooled-buffer bytes.
    // Use the body's actual length for both the final slice and loop condition.
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var position = AccessTools.Field(typeof(NetworkWriter), nameof(NetworkWriter.Position));
        CodeInstruction? body = null;
        int sum = -1, offset = -1;
        for (int i = 1; i + 4 < code.Count; i++)
            if (code[i].opcode == OpCodes.Ldfld && Equals(code[i].operand, position) && code[i + 2].opcode == OpCodes.Ldfld && Equals(code[i + 2].operand, position) && code[i + 3].opcode == OpCodes.Add &&
                code[i + 4].opcode.Name.StartsWith("stloc", StringComparison.Ordinal))
            { body = code[i - 1]; sum = Local(code[i + 4]); break; }
        for (int i = 0; i + 4 < code.Count; i++)
            if (Loads(code[i], sum) && code[i + 2].opcode == OpCodes.Sub &&
                code[i + 3].LoadsConstant(65533) && code[i + 4].operand is MethodInfo method && method.Name == "Min")
            { offset = Local(code[i + 1]); break; }
        if (body == null || sum < 0 || offset < 0) throw new InvalidOperationException("Unsupported RueI large-hint serializer.");
        int changed = 0;
        var result = new List<CodeInstruction>();
        for (int i = 0; i < code.Count; i++)
        {
            if (i + 2 < code.Count && Loads(code[i], sum) && Loads(code[i + 1], offset) &&
                (code[i + 2].opcode == OpCodes.Sub || code[i + 2].opcode == OpCodes.Bgt || code[i + 2].opcode == OpCodes.Bgt_S))
            {
                var load = new CodeInstruction(body.opcode, body.operand);
                load.labels.AddRange(code[i].labels);
                load.blocks.AddRange(code[i].blocks);
                result.Add(load);
                result.Add(new CodeInstruction(OpCodes.Ldfld, position));
                changed++;
            }
            else result.Add(code[i]);
        }
        if (changed != 2) throw new InvalidOperationException("RueI chunk-boundary patch did not match both length checks.");
        return result;
    }

    private static bool Loads(CodeInstruction instruction, int local) =>
        instruction.opcode.Name.StartsWith("ldloc", StringComparison.Ordinal) && Local(instruction) == local;

    private static int Local(CodeInstruction instruction)
    {
        if (instruction.operand is LocalBuilder builder) return builder.LocalIndex;
        if (instruction.operand is LocalVariableInfo variable) return variable.LocalIndex;
        string name = instruction.opcode.Name;
        if (name.Length == 7 && char.IsDigit(name[6])) return name[6] - '0';
        return instruction.operand is byte index ? index : instruction.operand is int number ? number : -1;
    }
}
