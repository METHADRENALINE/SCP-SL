using System.ComponentModel;

namespace ScpProximityChat.SecretAPI;

[Serializable]
public sealed class SecretApiProximityChatConfig
{

    public bool ShowAvailableHint { get; set; } = true;

    [Description("Use \"{0}\" where the keybind should be displayed.")]
    public string AvailableHint { get; set; } = "Press {0} to switch between nearby players and the SCP team.";

    [Description("Whether the same settings should apply on all servers under the same account ID.")]
    public bool Shared { get; set; } = true;

    [Description("Whether players should be able to adjust the volume of others' proximity messages.")]
    public bool Personalization { get; set; } = true;

}
