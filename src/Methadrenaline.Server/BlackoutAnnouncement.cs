using System;
using System.Collections.Generic;
using Cassie;
using HarmonyLib;
using LabApi.Features.Wrappers;
using Utils.Networking;

namespace Methadrenaline.Server;

internal sealed class BlackoutAnnouncement : CassieAnnouncement
{
    internal const string Spoken = "DANGER . SCP 0 7 9 HAS DISABLED MAIN POWER . IT IS NOW IN CHARGE . EMERGENCY POWER IS AVAILABLE FOR THIS ANNOUNCEMENT ONLY . TURN ON YOUR LIGHT AND PROCEED TO THE NEAREST EXIT .";
    internal const string Caption = "Danger. SCP-079 has disabled main power.<split>It is now in charge.<split>Emergency power is available for this announcement only.<split>Turn on your light and proceed to the nearest exit.";
    private readonly Action<float> finished;
    private readonly float intermission;

    internal BlackoutAnnouncement(float intermission, Action<float> finished)
        : base(new CassieTtsPayload(Spoken, Caption, true), 100f, 0f)
    {
        this.intermission = intermission;
        this.finished = finished;
    }

    public override float PostAnnouncementCooldown => base.PostAnnouncementCooldown + intermission;

    public override void OnFinishedPlaying() => finished(base.PostAnnouncementCooldown);
}

[HarmonyPatch(typeof(CassieAnnouncementDispatcher), "PlayNewAnnouncement")]
internal static class BlackoutCaptionDelivery
{
    private static bool Prefix(CassieAnnouncement __0) => BlackoutWarnings.Allows(__0);

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var send = AccessTools.Method(typeof(NetworkUtils), nameof(NetworkUtils.SendToAuthenticated))
            .MakeGenericMethod(typeof(CassieTtsPayload));
        return instructions.MethodReplacer(send, AccessTools.Method(typeof(BlackoutCaptionDelivery), nameof(Deliver)));
    }

    private static void Deliver(CassieTtsPayload payload, int channel)
    {
        if (!(CassieAnnouncementDispatcher.CurrentAnnouncement is BlackoutAnnouncement))
        {
            NetworkUtils.SendToAuthenticated(payload, channel);
            return;
        }
        int listeners = 0;
        foreach (var player in Player.ReadyList)
        {
            if (player.IsHost || player.IsDummy) continue;
            var text = Localizer.Text(player.ReferenceHub, BlackoutAnnouncement.Caption);
            player.ReferenceHub.connectionToClient.Send(new CassieTtsPayload(payload.Content, text, payload.PlayBackground), channel);
            listeners++;
        }
        LabApi.Features.Console.Logger.Info($"[METHADRENALINE] Blackout PA: native captions sent to {listeners}.");
    }
}

[HarmonyPatch(typeof(CassieAnnouncementDispatcher), nameof(CassieAnnouncementDispatcher.AddToQueue))]
internal static class BlackoutAnnouncementQueue
{
    private static bool Prefix(CassieAnnouncement __0) => BlackoutWarnings.Allows(__0);
}
