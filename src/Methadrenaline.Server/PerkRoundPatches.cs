using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CommandSystem;
using HarmonyLib;
using LabApi.Events.Handlers;
using MEC;
using SwiftArcadeMode.Features;
using SwiftArcadeMode.Features.Humans.Perks;
using SwiftArcadeMode.Features.Scoring;
using SwiftArcadeMode.Utils.Effects;
using ArcadeCore = SwiftArcadeMode.Core;

namespace Methadrenaline.Server;

internal static class ArcadeGate
{
    internal static PerkRoundController? Controller => ServerPlugin.Instance?.PerkRounds;
    internal static bool Allowed => Controller == null || Controller.Active || Controller.Cleaning;
}

[HarmonyPatch(typeof(ServerEvents), nameof(ServerEvents.OnRoundStarted))]
internal static class ChoosePerkRoundPatch
{
    [HarmonyPriority(Priority.First)]
    private static void Prefix() => ArcadeGate.Controller?.BeginRound();
    private static void Postfix() => ArcadeGate.Controller?.Announce();
}

[HarmonyPatch]
internal static class ResetPerkRoundPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(ServerEvents), nameof(ServerEvents.OnWaitingForPlayers));
        yield return AccessTools.Method(typeof(ServerEvents), nameof(ServerEvents.OnRoundRestarted));
    }
    [HarmonyPriority(Priority.First)]
    private static void Prefix() => ArcadeGate.Controller?.Reset();
}

[HarmonyPatch(typeof(ServerEvents), nameof(ServerEvents.OnRoundEnded))]
internal static class EndPerkRoundPatch
{
    private static void Postfix() => ArcadeGate.Controller?.Suspend();
}

[HarmonyPatch]
internal static class BlockArcadeCommandsPatch
{
    private static IEnumerable<MethodBase> TargetMethods() => typeof(ArcadeCore).Assembly.GetTypes().Where(t => !t.IsAbstract && typeof(ICommand).IsAssignableFrom(t)).Select(t => AccessTools.Method(t, "Execute")).Where(m => m != null).Distinct();
    private static bool Prefix(ref bool __result, ref string response)
    {
        if (ArcadeGate.Allowed) return true;
        response = PerkRoundController.DisabledMessage;
        __result = false;
        return false;
    }
}

[HarmonyPatch]
internal static class BlockArcadePerkGrantsPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(PerkInventory), nameof(PerkInventory.AddPerk));
        foreach (var method in typeof(PerkManager).GetMethods().Where(m => m.Name == nameof(PerkManager.GivePerk))) yield return method;
    }
    private static bool Prefix(ref bool __result)
    {
        if (ArcadeGate.Allowed) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch]
internal static class BlockArcadeEntryPointsPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(ArcadeCore), nameof(ArcadeCore.FixedUpdate));
        yield return AccessTools.Method(typeof(PerkSpawner), nameof(PerkSpawner.SpawnPerk));
        yield return AccessTools.Method(typeof(CustomEffectManager), nameof(CustomEffectManager.AddCustomEffect));
        yield return AccessTools.Method(typeof(ScoringManager), nameof(ScoringManager.AddScore));
    }
    private static bool Prefix() => ArcadeGate.Allowed;
}

[HarmonyPatch(typeof(Timing), "RunCoroutineInternal")]
internal static class GuardArcadeTimersPatch
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, FieldInfo[]> ActionFields = new();
    private static void Prefix(ref IEnumerator<float> coroutine)
    {
        var controller = ArcadeGate.Controller;
        if (controller == null || controller.Cleaning || coroutine == null) return;
        int epoch = controller.Epoch;
        Type type = coroutine.GetType();
        foreach (var field in ActionFields.GetOrAdd(type, t => t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Where(f => f.FieldType == typeof(Action)).ToArray()))
        {
            if (field.GetValue(coroutine) is not Action action) continue;
            var handlers = action.GetInvocationList().Cast<Action>().ToArray();
            if (!handlers.Any(h => h.Method.Module.Assembly == typeof(ArcadeCore).Assembly)) continue;
            Action? guarded = null;
            foreach (Action handler in handlers)
            {
                Action original = handler;
                guarded += original.Method.Module.Assembly == typeof(ArcadeCore).Assembly ? new Action(() => { if (controller.IsCurrent(epoch)) original(); }) : original;
            }
            field.SetValue(coroutine, guarded);
        }
        if (type.Assembly == typeof(ArcadeCore).Assembly) coroutine = Guard(coroutine, controller, epoch);
    }
    private static IEnumerator<float> Guard(IEnumerator<float> routine, PerkRoundController controller, int epoch)
    {
        try
        {
            while (controller.IsCurrent(epoch) && routine.MoveNext()) yield return routine.Current;
        }
        finally { routine.Dispose(); }
    }
}
