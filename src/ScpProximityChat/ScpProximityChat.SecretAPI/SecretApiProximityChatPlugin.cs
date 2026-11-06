using Hints;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.CustomHandlers;
using LabApi.Events.Handlers;
using LabApi.Loader;
using LabApi.Loader.Features.Plugins;
using RueI.API;
using RueI.API.Elements;
using RueI.API.Elements.Parameters;

namespace ScpProximityChat.SecretAPI;

/// <summary>An extension plugin for the Core that adds SSSS-based toggling and personalization.</summary>
public sealed class SecretApiProximityChatPlugin : Plugin<SecretApiProximityChatConfig>
{

    /// <summary>The setting ID of the toggle keybind.</summary>
    /// <remarks>Can be used to create an <see cref="SSKeybindHintParameter"/>.</remarks>
    public static int ToggleSettingId => SettingsRegistry.Toggle.Id;

    internal static SecretApiProximityChatTranslations Translation { get; private set; } = new();

    /// <inheritdoc/>
    public override string Name => "SSSS Proximity Chat";

    /// <inheritdoc/>
    public override string Description => "SSSS-based Proximity Chat";

    /// <inheritdoc/>
    public override string Author => "Axwabo";

    /// <inheritdoc/>
    public override Version Version => GetType().Assembly.GetName().Version;

    /// <inheritdoc/>
    public override Version RequiredApiVersion { get; } = new(1, 0, 0);

    private readonly PersonalizationEventHandlers _handlers = new();
    private static readonly Tag VoiceTipTag = new("scp-proximity.keybind");
    private const string HoldTip = "Hold {0} to talk to nearby players.";

    /// <inheritdoc />
    public override void LoadConfigs()
    {
        base.LoadConfigs();
        Translation = this.LoadConfig<SecretApiProximityChatTranslations>("translations.yml") ?? new SecretApiProximityChatTranslations();
        Headers.Reload();
    }

    /// <inheritdoc/>
    public override void Enable()
    {
        PlayerEvents.ChangedRole += OnChangedRole;
        ProximityChatEvents.Receiving += Receiving;
        ProximityChatEvents.Personalizing += PersonalizationManager.RegisterPersonalization;
        CustomHandlersManager.RegisterEventsHandler(_handlers);
        StaticUnityMethods.OnUpdate += PersonalizationManager.UpdateVolumes;
        CustomSetting.Register(SettingsRegistry.Toggle, SettingsRegistry.Input, SettingsRegistry.Mute, SettingsRegistry.Master);
        SettingsRegistry.Shared = Config!.Shared;
        if (!(VolumeHelpers.CanPersonalize = Config!.Personalization))
            return;
        SettingsRegistry.All.Add(SettingsRegistry.PersonalizationVisibility);
        CustomSetting.Register(SettingsRegistry.PersonalizationVisibility);
    }

    /// <inheritdoc/>
    public override void Disable()
    {
        PlayerEvents.ChangedRole -= OnChangedRole;
        foreach (var player in Player.List)
            if (!player.IsDestroyed) RueDisplay.Get(player).Remove(VoiceTipTag);
        ProximityChatEvents.Receiving -= Receiving;
        ProximityChatEvents.Personalizing -= PersonalizationManager.RegisterPersonalization;
        CustomHandlersManager.UnregisterEventsHandler(_handlers);
        StaticUnityMethods.OnUpdate -= PersonalizationManager.UpdateVolumes;
        CustomSetting.UnRegister(SettingsRegistry.All);
        PersonalizationManager.Reset();
    }

    private void OnChangedRole(PlayerChangedRoleEventArgs ev)
    {
        var player = ev.Player;
        if (player.IsHost || player.IsDestroyed) return;
        var display = RueDisplay.Get(player);
        if (!player.CanUseProximityChat())
        {
            display.Remove(VoiceTipTag);
            return;
        }

        if (InputMethod.UsesHold(player)) player.DisableProximityChat();
        else player.EnableProximityChat();
        if (!Config!.ShowAvailableHint) return;
        display.Show(VoiceTipTag, new DynamicElement(58, () =>
            "<align=center><size=34><nobr>" + (InputMethod.UsesHold(player) ? HoldTip : Config.AvailableHint) + "</nobr></size></align>")
        {
            Parameters = [new KeybindParameter(ToggleSettingId, "<b>{0}</b>")]
        }, 30f);
    }

    private static void Receiving(Player sender, Player target, ref bool allow)
    {
        if (allow && CustomSetting.TryGetPlayerSetting(target, out ProximityChatMute? mute) && mute.Muted)
            allow = false;
    }

}
