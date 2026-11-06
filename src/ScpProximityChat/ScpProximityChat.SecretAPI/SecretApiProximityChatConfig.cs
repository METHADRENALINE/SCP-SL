using System.ComponentModel;

namespace ScpProximityChat.SecretAPI;

/// <summary>Configuration for the SecretAPI-based Proximity Chat extension.</summary>
[Serializable]
public sealed class SecretApiProximityChatConfig
{

    /// <summary>Whether to show a simple hint when Proximity Chat is available.</summary>
    public bool ShowAvailableHint { get; set; } = true;

    /// <summary>The hint text to show when Proximity Chat is available. Use {0} for the toggle keybind.</summary>
    [Description("Use \"{0}\" where the keybind should be displayed.")]
    public string AvailableHint { get; set; } = "Press {0} to switch between nearby players and the SCP team.";

    /// <summary>Whether the same settings should apply on all servers under the same account ID.</summary>
    [Description("Whether the same settings should apply on all servers under the same account ID.")]
    public bool Shared { get; set; } = true;

    /// <summary>Whether players should be able to adjust the volume of others' proximity messages.</summary>
    [Description("Whether players should be able to adjust the volume of others' proximity messages.")]
    public bool Personalization { get; set; } = true;

}
