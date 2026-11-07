using SecretLabNAudio.Core.Extensions;

namespace ScpProximityChat.Core;

public static class ProximityChatEvents
{

    public static event ProximityChatAvailable? Available;

    public static event ProximityChatToggled? Toggled;

    public static event ProximityChatMessageSending? Sending;

    public static event ProximityChatMessageReceiving? Receiving;

    public static event ProximityChatPersonalizing? Personalizing;

    internal static void OnAvailable(Player player) => Available?.Invoke(player);

    internal static void OnToggled(Player player, bool enabled) => Toggled?.Invoke(player, enabled);

    internal static void OnSending(Player player, ref bool allow) => Sending?.Invoke(player, ref allow);

    internal static void OnReceiving(Player sender, Player target, ref bool allow) => Receiving?.Invoke(sender, target, ref allow);

    internal static void Personalize(Player player, SpeakerToy toy)
    {
        if (Personalizing != null)
            toy.AddPersonalization(personalization => Personalizing.Invoke(player, personalization));
    }

}
