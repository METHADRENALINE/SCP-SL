using System.Collections.Generic;
using CustomPlayerEffects;
using HarmonyLib;
using LabApi.Features.Wrappers;

namespace Methadrenaline.Server;

internal sealed class BlackoutSoundtrack
{
    private readonly Dictionary<Player, SoundtrackMute> applied = new();

    internal void Apply(Player? player)
    {
        if (player == null || player.IsHost || !BlackoutWarnings.Muted) return;
        if (player.GetEffect<SoundtrackMute>() is not { IsEnabled: false } effect) return;
        effect.ServerSetState(1);
        applied[player] = effect;
    }

    internal void Forget(Player player) => applied.Remove(player);

    internal void Reset()
    {
        foreach (var effect in applied.Values)
            if (effect && effect.Intensity == 1 && effect.Duration == 0f) effect.ServerDisable();
        applied.Clear();
    }
}

[HarmonyPatch(typeof(PlayerEffectsController), "OnRoleChanged")]
internal static class BlackoutSoundtrackRoleChange
{
    private static void Postfix(PlayerEffectsController __instance, ReferenceHub __0)
    {
        if (!BlackoutWarnings.Muted || __instance != __0.playerEffectsController) return;
        ServerPlugin.Instance?.Blackout.Soundtrack.Apply(Player.Get(__0));
    }
}

[HarmonyPatch(typeof(AmbientSoundPlayer), "RpcPlaySound")]
internal static class BlackoutAmbientSounds
{
    private static bool Prefix() => !BlackoutWarnings.Muted;
}
