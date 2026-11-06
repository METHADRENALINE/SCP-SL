namespace ScpProximityChat.SecretAPI;

[Serializable]
internal sealed class SecretApiProximityChatTranslations
{

    public string ChatHeader { get; set; } = "Proximity Chat";

    public string PersonalizationHeader { get; set; } = "Individual SCP voice volumes";

    public string ToggleLabel { get; set; } = ProximityChatToggle.Text;

    public string ToggleHint { get; set; } = "Enable/disable using Proximity Chat while you're an SCP.";

    public string InputMethodLabel { get; set; } = InputMethod.Text;

    public string InputMethodHint { get; set; } = "Choose whether to toggle Proximity Chat by pressing the key once, or to hold the key to use Proximity Chat.";

    public string Toggle { get; set; } = "Toggle";

    public string Hold { get; set; } = "Hold";

    public string MuteLabel { get; set; } = ProximityChatMute.Text;

    public string MuteHint { get; set; } = "Enable/disable hearing others' SCP Proximity Chat.";

    public string Audible { get; set; } = "Audible";

    public string Muted { get; set; } = "Muted";

    public string VisibilityLabel { get; set; } = "Show individual SCP volume sliders";

    public string Shown { get; set; } = "Shown";

    public string Hidden { get; set; } = "Hidden";

    public string MasterVolumeLabel { get; set; } = "SCP local voice volume";

    public string MasterVolumeHint { get; set; } = "Changes how loud you hear nearby SCPs.";

    public string VolumeFor { get; set; } = "SCP voice: ";

}
