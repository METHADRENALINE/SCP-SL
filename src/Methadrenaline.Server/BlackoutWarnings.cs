using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Cassie;
using HarmonyLib;
using LightContainmentZoneDecontamination;
using PlayerRoles.Spectating;
using Subtitles;
using UnityEngine;
using Utils.Networking;

namespace Methadrenaline.Server;

internal static class BlackoutWarnings
{
    internal static bool Muted => ServerPlugin.Instance?.Blackout.SuppressAnnouncements == true;

    internal static bool Allows(CassieAnnouncement announcement)
        => !Muted || announcement is BlackoutAnnouncement;

    internal static void RefreshClients()
    {
        if (DecontaminationController.Singleton is { } decon && decon)
            decon.SetSyncVarDirtyBit(1UL);
        if (AlphaWarheadController.Singleton is { } warhead && warhead)
            warhead.SetSyncVarDirtyBit(1UL);
    }
}

[HarmonyPatch(typeof(RoomLightController), nameof(RoomLightController.SerializeSyncVars))]
internal static class WarheadEmergencyLighting
{
    private static bool Active => !BlackoutWarnings.Muted &&
        ServerPlugin.Instance?.WarheadPlayback.EmergencyLights == true;

    private static bool Enabled(RoomLightController light)
        => Active || light.LightsEnabled;

    private static Color LightColor(RoomLightController light)
        => Active ? Color.red : light.OverrideColor;

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var enabled = AccessTools.Field(typeof(RoomLightController), nameof(RoomLightController.LightsEnabled));
        var color = AccessTools.Field(typeof(RoomLightController), nameof(RoomLightController.OverrideColor));
        foreach (var instruction in instructions)
        {
            if (instruction.LoadsField(enabled) || instruction.LoadsField(color))
            {
                var method = Equals(instruction.operand, enabled) ? nameof(Enabled) : nameof(LightColor);
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(WarheadEmergencyLighting), method);
            }
            yield return instruction;
        }
    }
}

[HarmonyPatch]
internal static class BlackoutWarningText
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(DecontaminationController), "UpdateTime");
        yield return AccessTools.Method(typeof(DecontaminationController), "FinishDecontamination");
        yield return AccessTools.Method(typeof(AlphaWarheadController), nameof(AlphaWarheadController.StartDetonation));
        yield return AccessTools.Method(typeof(AlphaWarheadController), nameof(AlphaWarheadController.CancelDetonation),
            new[] { typeof(ReferenceHub) });
        yield return AccessTools.Method(typeof(AlphaWarheadController), "Detonate");
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var broadcast = AccessTools.Method(typeof(Broadcast), nameof(Broadcast.RpcAddElement));
        var subtitles = AccessTools.Method(typeof(NetworkUtils), nameof(NetworkUtils.SendToAuthenticated))
            .MakeGenericMethod(typeof(SubtitleMessage));
        var spectatorSubtitles = AccessTools.Method(typeof(SpectatorNetworking), nameof(SpectatorNetworking.SendToSpectatorsOf))
            .MakeGenericMethod(typeof(SubtitleMessage));
        return instructions
            .MethodReplacer(broadcast, AccessTools.Method(typeof(BlackoutWarningText), nameof(SendBroadcast)))
            .MethodReplacer(subtitles, AccessTools.Method(typeof(BlackoutWarningText), nameof(SendSubtitles)))
            .MethodReplacer(spectatorSubtitles, AccessTools.Method(typeof(BlackoutWarningText), nameof(SendSpectatorSubtitles)));
    }

    private static void SendBroadcast(Broadcast broadcast, string message, ushort time, Broadcast.BroadcastFlags flags)
    {
        if (!BlackoutWarnings.Muted) broadcast.RpcAddElement(message, time, flags);
    }

    private static void SendSubtitles(SubtitleMessage message, int channel)
    {
        if (!BlackoutWarnings.Muted) NetworkUtils.SendToAuthenticated(message, channel);
    }

    private static void SendSpectatorSubtitles(SubtitleMessage message, ReferenceHub target, bool includeTarget)
    {
        if (!BlackoutWarnings.Muted) SpectatorNetworking.SendToSpectatorsOf(message, target, includeTarget);
    }
}

[HarmonyPatch]
internal static class BlackoutClientWarningState
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(DecontaminationController), nameof(DecontaminationController.SerializeSyncVars));
        yield return AccessTools.Method(typeof(AlphaWarheadController), nameof(AlphaWarheadController.SerializeSyncVars));
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        bool decon = original.DeclaringType == typeof(DecontaminationController);
        var field = AccessTools.Field(original.DeclaringType, decon ? nameof(DecontaminationController.RoundStartTime) : nameof(AlphaWarheadController.Info));
        var clientValue = AccessTools.Method(typeof(BlackoutClientWarningState), decon ? nameof(ClientDecontaminationStart) : nameof(ClientWarheadInfo));
        foreach (var instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, field))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = clientValue;
            }
            yield return instruction;
        }
    }

    private static double ClientDecontaminationStart(DecontaminationController decon)
        => BlackoutWarnings.Muted ? 0d : decon.RoundStartTime;

    private static AlphaWarheadSyncInfo ClientWarheadInfo(AlphaWarheadController warhead)
    {
        if (BlackoutWarnings.Muted) return default;
        if (warhead.AlreadyDetonated) return warhead.Info;
        var playback = ServerPlugin.Instance?.WarheadPlayback;
        if (playback?.OverridesCountdown(warhead) == true)
            return playback.ShowsFinale ? warhead.Info : default;
        return warhead.Info;
    }
}

[HarmonyPatch(typeof(AlphaWarheadController), nameof(AlphaWarheadController.RpcShake))]
internal static class BlackoutWarheadShake
{
    private static bool Prefix() => !BlackoutWarnings.Muted;
}
