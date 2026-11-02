using SecretLabNAudio.Core.Pools;

namespace ScpProximityChat.Core;

/// <summary>SCP Proximity Chat extension methods for <see cref="Player"/> wrappers.</summary>
public static class ProximityChatState
{

    /// <summary>All conditions that must be met for a player to be able to use Proximity Chat.</summary>
    public static List<Func<Player, bool>> Conditions { get; } = [];

    internal static Dictionary<Player, SpeakerToy> ActiveSpeakers { get; } = [];

    /// <summary>Gets whether the player has Proximity Chat enabled.</summary>
    /// <param name="player">The player to check.</param>
    /// <returns>If the player has Proximity Chat enabled.</returns>
    /// <seealso cref="CanUseProximityChat"/>
    public static bool IsProximityChatEnabled(this Player player) => ActiveSpeakers.ContainsKey(player);

    /// <summary>Gets the current speaker for an active source, or null.</summary>
    public static SpeakerToy? GetProximitySpeaker(this Player player)
        => ActiveSpeakers.TryGetValue(player, out var speaker) ? speaker : null;

    /// <summary>Enables Proximity Chat for the player.</summary>
    /// <param name="player">The player to enable Proximity Chat for.</param>
    /// <returns>True if Proximity Chat has been enabled, false if it's already enabled or if the player has been destroyed.</returns>
    /// <remarks>This method does not check if the player can use Proximity Chat.</remarks>
    /// <seealso cref="CanUseProximityChat"/>
    /// <seealso cref="DisableProximityChat"/>
    /// <seealso cref="ToggleProximityChat"/>
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

    // AdminToyBase serializes local coordinates and only recognizes a parent
    // with its own NetworkIdentity. A camera parent has none: clients would
    // place the speaker near world origin and cull every audio packet.
    internal static void SynchronizePositions()
    {
        foreach (var entry in ActiveSpeakers)
        {
            if (entry.Key.IsDestroyed || entry.Value.IsDestroyed) continue;
            entry.Value.Transform.position = entry.Key.ReferenceHub.PlayerCameraReference.position;
            entry.Value.Base.NetworkPosition = entry.Value.Transform.localPosition;
        }
    }

    /// <summary>Disables Proximity Chat for the player.</summary>
    /// <param name="player">The player to disable Proximity Chat for.</param>
    /// <returns>True if Proximity Chat has been disabled, false if it wasn't enabled.</returns>
    /// <remarks>This method does not check if the player can use Proximity Chat.</remarks>
    /// <seealso cref="CanUseProximityChat"/>
    /// <seealso cref="EnableProximityChat"/>
    /// <seealso cref="ToggleProximityChat"/>
    public static bool DisableProximityChat(this Player player)
    {
        if (!ActiveSpeakers.Remove(player, out var speaker))
            return false;
        SpeakerToyPool.Return(speaker);
        return true;
    }

    /// <summary>Toggles the Proximity Chat state of the player.</summary>
    /// <param name="player">True if Proximity Chat has been enabled, false if it's been disabled or if the player has been destroyed.</param>
    /// <returns>Whether Proximity Chat is now enabled.</returns>
    /// <remarks>This method does not check if the player can use Proximity Chat.</remarks>
    /// <seealso cref="CanUseProximityChat"/>
    /// <seealso cref="EnableProximityChat"/>
    /// <seealso cref="DisableProximityChat"/>
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

    /// <summary>Gets whether the player may use Proximity Chat.</summary>
    /// <param name="player">The player to check.</param>
    /// <returns>Whether the player meets all conditions to use Proximity Chat.</returns>
    public static bool CanUseProximityChat(this Player player)
    {
        foreach (var condition in Conditions)
            if (!condition(player))
                return false;
        return true;
    }

}
