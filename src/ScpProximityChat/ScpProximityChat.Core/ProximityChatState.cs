using SecretLabNAudio.Core.Pools;

namespace ScpProximityChat.Core;

public static class ProximityChatState
{

    public static List<Func<Player, bool>> Conditions { get; } = [];

    internal static Dictionary<Player, SpeakerToy> ActiveSpeakers { get; } = [];

    public static bool IsProximityChatEnabled(this Player player) => ActiveSpeakers.ContainsKey(player);

    public static SpeakerToy? GetProximitySpeaker(this Player player)
        => ActiveSpeakers.TryGetValue(player, out var speaker) ? speaker : null;

    public static bool EnableProximityChat(this Player player)
    {
        if (player.IsProximityChatEnabled() || player.IsDestroyed)
            return false;
        var config = ProximityChatPlugin.Cfg;
        var toy = SpeakerToyPool.Rent(SpeakerToyPool.NextAvailableId, config.AudioSettings,
            player.ReferenceHub.transform, spawn: false);
        toy.IsStatic = false;
        toy.Transform.position = player.ReferenceHub.PlayerCameraReference.position;
        toy.Base.NetworkPosition = toy.Transform.localPosition;
        toy.Spawn();
        toy.Base.NetworkPosition = toy.Transform.localPosition;
        ProximityChatEvents.Personalize(player, toy);
        ActiveSpeakers.Add(player, toy);
        return true;
    }

    internal static void SynchronizePositions()
    {
        foreach (var entry in ActiveSpeakers)
        {
            if (entry.Key.IsDestroyed || entry.Value.IsDestroyed) continue;
            entry.Value.Transform.position = entry.Key.ReferenceHub.PlayerCameraReference.position;
            entry.Value.Base.NetworkPosition = entry.Value.Transform.localPosition;
        }
    }

    public static bool DisableProximityChat(this Player player)
    {
        if (!ActiveSpeakers.Remove(player, out var speaker))
            return false;
        SpeakerToyPool.Return(speaker);
        return true;
    }

    public static bool ToggleProximityChat(this Player player)
    {
        if (player.IsDestroyed)
            return false;
        if (player.DisableProximityChat())
        {
            ProximityChatEvents.OnToggled(player, false);
            return false;
        }

        player.EnableProximityChat();
        ProximityChatEvents.OnToggled(player, true);
        return true;
    }

    public static bool CanUseProximityChat(this Player player)
    {
        foreach (var condition in Conditions)
            if (!condition(player))
                return false;
        return true;
    }

}
