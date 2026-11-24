using LabApi.Events.CustomHandlers;
using LabApi.Loader.Features.Plugins;
using HarmonyLib;
using LabApi.Loader.Features.Plugins.Enums;
using PlayerRoles.Voice;
using PlayerRoles.PlayableScps;
using PlayerRoles.PlayableScps.Scp3114;

namespace ScpProximityChat.Core;

public sealed class ProximityChatPlugin : Plugin<ProximityChatConfig>
{

    private static ProximityChatPlugin _instance = null!;

    public static ProximityChatConfig Cfg => _instance.Config!;

    public override string Name => "SCP Proximity Chat";

    public override string Description => "Proximity chat for SCPs";

    public override string Author => "Axwabo";

    public override Version Version => GetType().Assembly.GetName().Version;

    public override Version RequiredApiVersion { get; } = new(1, 0, 0);

    public override LoadPriority Priority => LoadPriority.High;

    private readonly EventHandlers _eventHandlers = new();
    private readonly Harmony _harmony = new("methadrenaline.scp-proximity.intercom");

    public override void Enable()
    {
        _instance = this;
        _harmony.CreateClassProcessor(typeof(IntercomRouting)).Patch();
        _harmony.CreateClassProcessor(typeof(IntercomVoiceDelivery)).Patch();
        Intercom.OnServerBeginUsage += IntercomSpeech.Begin;
        StaticUnityMethods.OnUpdate += IntercomSpeech.Update;
        ProximityChatEvents.Toggled += OnToggled;
        CustomHandlersManager.RegisterEventsHandler(_eventHandlers);
        StaticUnityMethods.OnUpdate += ProximityChatState.SynchronizePositions;
        var allowedRoles = Config!.AllowedRoles;
        if (allowedRoles == null)
            ProximityChatState.Conditions.Add(player => player.RoleBase is FpcStandardScp and not Scp3114Role);
        else
            ProximityChatState.Conditions.Add(player => allowedRoles.Contains(player.Role));
    }

    public override void Disable()
    {
        ProximityChatEvents.Toggled -= OnToggled;
        CustomHandlersManager.UnregisterEventsHandler(_eventHandlers);
        StaticUnityMethods.OnUpdate -= ProximityChatState.SynchronizePositions;
        StaticUnityMethods.OnUpdate -= IntercomSpeech.Update;
        Intercom.OnServerBeginUsage -= IntercomSpeech.Begin;
        IntercomSpeech.Reset();
        _harmony.UnpatchAll(_harmony.Id);
    }

    private void OnToggled(Player player, bool enabled)
    {
        if (!enabled) IntercomSpeech.Forget(player);
        if (Config!.ShowToggledHint)
            player.SendHint(enabled ? Config.Enabled : Config.Disabled);
    }

}
