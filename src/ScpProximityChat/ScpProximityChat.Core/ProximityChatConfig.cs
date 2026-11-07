using System.ComponentModel;
using PlayerRoles;
using SecretLabNAudio.Core;

namespace ScpProximityChat.Core;

[Serializable]
public sealed class ProximityChatConfig
{

    public bool ShowToggledHint { get; set; } = true;

    public string Enabled { get; set; } = "Proximity Chat enabled.";

    public string Disabled { get; set; } = "Proximity Chat disabled.";

    public SpeakerSettings AudioSettings { get; set; } = SpeakerSettings.Default with { Volume = 1.5f };

    [Description("Use ordinary human proximity voice attenuation. SpeakerToy attenuation is flattened and replaced by the native human curve per listener.")]
    public bool MatchHumanVoiceDistances { get; set; } = true;

    [Description("Set to null to use the default condition (all SCPs except SCP-079 and SCP-3114).")]
    public List<RoleTypeId>? AllowedRoles { get; set; }

    [Description("Set to true if the receivers' voice modules should filter whether a player receives the message. Might mess with AudioSettings, but can counter cheaters.")]
    public bool ValidateReceive { get; set; }

    [Description("Set to false to disable spectators receiving Proximity Chat audio.")]
    public bool AudibleToSpectators { get; set; } = true;

}
