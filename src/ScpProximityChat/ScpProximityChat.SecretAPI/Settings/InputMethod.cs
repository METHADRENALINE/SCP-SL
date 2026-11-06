namespace ScpProximityChat.SecretAPI.Settings;

internal sealed class InputMethod : CustomTwoButtonSetting
{

    public const string Text = "Input Method";

    public InputMethod() : base(Text.GetStableHashCode(),
        Translation.InputMethodLabel,
        Translation.Toggle,
        Translation.Hold,
        hint: Translation.InputMethodHint
    ) => IsShared = SettingsRegistry.Shared;

    public bool Hold => IsOptionB;
    internal static bool UsesHold(Player player) => TryGetPlayerSetting(player, out InputMethod? input) && input.Hold;

    protected override CustomSetting CreateDuplicate() => new InputMethod();

    protected override void HandleSettingUpdate()
    {
        var player = KnownOwner!;
        ProximityChatToggle.InvalidateCancellation(player);
        if (!player.CanUseProximityChat()) return;
        if (Hold && (!TryGetPlayerSetting(player, out ProximityChatToggle? toggle) || !toggle.IsPressed))
            player.DisableProximityChat();
        RueI.API.RueDisplay.Get(player).Update();
    }

    public override CustomHeader Header => Headers.ProximityChat;

}
