using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Hints;
using Mirror;
using RueI.API.Elements;
using Utils.Networking;

namespace Methadrenaline.Server;

[HarmonyPatch]
internal static class SiteMapHintPatch
{
    private static MethodBase TargetMethod() => AccessTools.Method("RueI.API.Parsing.ElementCombiner:Combine");

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        int changed = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(HintEffectReaderWriter) &&
                method.Name == nameof(HintEffectReaderWriter.WriteHintEffect))
            {
                yield return new CodeInstruction(OpCodes.Ldarg_1).MoveLabelsFrom(instruction);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SiteMapHintPatch), nameof(WriteEffect)));
                changed++;
            }
            else yield return instruction;
        }
        if (changed != 1) throw new InvalidOperationException("Unsupported RueI hint-effect serializer.");
    }

    private static void WriteEffect(NetworkWriter writer, HintEffect effect, IEnumerable<Element> elements)
    {
        if (!elements.Any(element => element is SiteMapElement))
        {
            writer.WriteHintEffect(effect);
            return;
        }
        writer.Position -= sizeof(int);
        writer.WriteInt(0);
    }
}
