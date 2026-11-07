namespace SecretAPI.Features.UserSettings;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using global::UserSettings.ServerSpecific;
using LabApi.Events.Handlers;
using LabApi.Features.Console;
using LabApi.Features.Enums;
using LabApi.Features.Wrappers;
using Mirror;
using NorthwoodLib.Pools;
using SecretAPI.Extensions;

public abstract class CustomSetting : ISetting<ServerSpecificSettingBase>
{
    private static readonly Dictionary<Player, List<CustomSetting>> ReceivedPlayerSettings = [];

    static CustomSetting()
    {
        SecretApi.Harmony.PatchCategory(nameof(CustomSetting), SecretApi.Assembly);

        ServerSpecificSettingsSync.SendOnJoinFilter = null;
        ServerSpecificSettingsSync.DefinedSettings ??= [];
        ServerSpecificSettingsSync.ServerOnSettingValueReceived += OnSettingsUpdated;

        PlayerEvents.Joined += ev => SendSettingsToPlayer(ev.Player);
        PlayerEvents.GroupChanged += ev => SendSettingsToPlayer(ev.Player);
        PlayerEvents.Left += ev => RemoveStoredPlayer(ev.Player);
    }

    protected CustomSetting(ServerSpecificSettingBase setting)
    {
        Base = setting;
    }

    public static List<CustomSetting> CustomSettings { get; } = [];

    public static IReadOnlyDictionary<Player, List<CustomSetting>> PlayerSettings => ReceivedPlayerSettings;

    public ServerSpecificSettingBase Base { get; }

    public Player? KnownOwner { get; private set; }

    public abstract CustomHeader Header { get; }

    public SettingResponseType LastUpdateType { get; private set; } = SettingResponseType.None;

    public virtual bool HasValueChanged { get; } = false;

    public bool IsCurrentlyAccessible { get; private set; }

    public bool IsServerSetting
    {
        get => Base.IsServerOnly;
        set => Base.IsServerOnly = value;
    }

    public bool IsDefaultSetting => KnownOwner == null;

    public string Label
    {
        get => Base.Label;
        set
        {
            Base.Label = value;
            SendSettingUpdate();
        }
    }

    public string DescriptionHint
    {
        get => Base.HintDescription;
        set
        {
            Base.HintDescription = value;
            SendSettingUpdate();
        }
    }

    public int Id
    {
        get => Base.SettingId;
        init => Base.SettingId = value;
    }

    public byte CollectionId
    {
        get => Base.CollectionId;
        set => Base.CollectionId = value;
    }

    public bool IsShared
    {
        get => CollectionId < ServerSpecificSettingBase.MaxCollections;
        set => CollectionId = value ? byte.MinValue : byte.MaxValue;
    }

    public static void Register(params CustomSetting[] settings) => Register((IEnumerable<CustomSetting>)settings);

    public static void Register(IEnumerable<CustomSetting> settings)
    {
        foreach (CustomSetting setting in settings)
        {
            if (ValidateSettingInternal(setting, null))
                CustomSettings.Add(setting);
        }
    }

    public static void UnRegister(params CustomSetting[] settings) => UnRegister((IEnumerable<CustomSetting>)settings);

    public static void UnRegister(IEnumerable<CustomSetting> settings) => CustomSettings.RemoveAll(settings.Contains);

    public static void UnRegister(params Type[] types) => UnRegister((IEnumerable<Type>)types);

    public static void UnRegister(IEnumerable<Type> types) => CustomSettings.RemoveAll(s => types.Any(t => s.GetType() == t));

    public static bool TryGetPlayerSetting<TSetting>(Player player, bool requireAccessible, [NotNullWhen(true)] out TSetting? setting)
        where TSetting : CustomSetting
    {
        setting = null;

        if (!PlayerSettings.TryGetValue(player, out List<CustomSetting>? settings))
            return false;

        foreach (CustomSetting toCheck in settings)
        {
            if (toCheck is not TSetting value)
                continue;

            if (requireAccessible && !toCheck.IsCurrentlyAccessible)
                return false;

            setting = value;
            return true;
        }

        return false;
    }

    public static bool TryGetPlayerSetting<TSetting>(Player player, [NotNullWhen(true)] out TSetting? setting)
        where TSetting : CustomSetting => TryGetPlayerSetting(player, false, out setting);

    public static bool TryGetPlayerSetting(Player player, int id, bool requireAccessible, [NotNullWhen(true)] out CustomSetting? setting)
    {
        setting = null;
        if (PlayerSettings.TryGetValue(player, out List<CustomSetting>? settings))
            setting = settings.FirstOrDefault(s => s.Id == id && (!requireAccessible || s.IsCurrentlyAccessible));
        return setting != null;
    }

    public static T? GetPlayerSetting<T>(int id, Player player)
        where T : CustomSetting => PlayerSettings.TryGetValue(player, out List<CustomSetting> settings)
        ? settings.FirstOrDefault(s => s.Base.SettingId == id && s.GetType() == typeof(T)) as T
        : null;

    public static CustomSetting? Get(Type type, int id)
        => CustomSettings.FirstOrDefault(s => s.Base.SettingId == id && s.Base.GetType() == type);

    public static T? Get<T>(int id)
        where T : CustomSetting => Get(typeof(T), id) as T;

    public static void ResyncServer(int? version = null)
    {
        foreach (Player player in Player.GetAll(PlayerSearchFlags.AuthenticatedPlayers))
            SendSettingsToPlayer(player, version);
    }

    public static void SendSettingsToPlayer(Player player, int? version = null)
    {
        if (player.IsHost || player.IsNpc)
            return;

        List<CustomSetting> playerSettings = ListPool<CustomSetting>.Shared.Rent();
        try
        {
            foreach (CustomSetting setting in CustomSettings)
            {
                if (!setting.CanView(player))
                {
                    if (TryGetPlayerSetting(player, setting.Id, false, out CustomSetting? playerSetting))
                        playerSetting.IsCurrentlyAccessible = false;
                    continue;
                }

                CustomSetting playerSpecific = EnsurePlayerSpecificSetting(player, setting);
                playerSpecific.PersonalizeSetting();
                playerSpecific.IsCurrentlyAccessible = true;
                playerSettings.Add(playerSpecific);
            }

            if (playerSettings.Count != 0)
            {
                List<ServerSpecificSettingBase> ordered = ListPool<ServerSpecificSettingBase>.Shared.Rent();
                foreach (IGrouping<CustomHeader, CustomSetting> grouping in playerSettings.GroupBy(static setting => setting.Header))
                {
                    ordered.Add(grouping.Key.Base);
                    ordered.AddRange(grouping.Select(static setting => setting.Base));
                }

                ordered.AddRange(ServerSpecificSettingsSync.DefinedSettings);

                ServerSpecificSettingsSync.SendToPlayer(player.ReferenceHub, ordered.ToArray(), version);
                ListPool<ServerSpecificSettingBase>.Shared.Return(ordered);
            }
            else
            {
                ServerSpecificSettingsSync.SendToPlayer(player.ReferenceHub, ServerSpecificSettingsSync.DefinedSettings, version);
            }
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
        }

        ListPool<CustomSetting>.Shared.Return(playerSettings);
    }

    internal static bool ValidateSettingInternal(CustomSetting? setting, ServerSpecificSettingBase? settingBase)
    {
        if (settingBase == null && setting == null)
        {
            Logger.Error("[CustomSetting.TryValidateSetting] Failed to validate null setting! " + new StackTrace(1));
            return false;
        }

        if (settingBase is SSGroupHeader)
            return true;

        string validateSettingName = setting?.GetType().FullName ?? settingBase?.Label ?? "[Unknown]";
        int? id = setting?.Id ?? settingBase?.SettingId;

        string? sharedIdInfo = CustomSettings.FirstOrDefault(s => s.Id == id)?.GetType().FullName ?? ServerSpecificSettingsSync.DefinedSettings.FirstOrDefault(s => s.SettingId == id && s is not SSGroupHeader)?.Label;
        if (sharedIdInfo != null)
        {
            Logger.Error($"[CustomSetting.TryValidateSetting] {validateSettingName} is being registered with a duplicate ID ({id}) shared by {sharedIdInfo} {new StackTrace(1)}");
            return false;
        }

        return true;
    }

    internal bool IsKnownOwnerHub(ReferenceHub? hub) => hub && KnownOwner?.ReferenceHub == hub;

    protected virtual void HandleBeforeSettingUpdate()
    {
        LastUpdateType = LastUpdateType == SettingResponseType.None
            ? SettingResponseType.Initial
            : SettingResponseType.Update;
    }

    protected void ResyncToOwner()
    {
        if (KnownOwner == null)
            return;

        SendSettingsToPlayer(KnownOwner);
    }

    protected virtual bool CanView(Player player) => true;

    protected abstract CustomSetting CreateDuplicate();

    protected virtual void PersonalizeSetting()
    {
    }

    protected abstract void HandleSettingUpdate();

    private static void RemoveStoredPlayer(Player player) => ReceivedPlayerSettings.Remove(player);

    private static void OnSettingsUpdated(ReferenceHub hub, ServerSpecificSettingBase settingBase)
    {
        if (hub.IsHost)
            return;

        Player player = Player.Get(hub);

        try
        {
            CustomSetting? setting = CustomSettings.FirstOrDefault(s => s.Base.SettingId == settingBase.SettingId);
            if (setting == null || !setting.CanView(player))
                return;

            CustomSetting newSettingPlayer = EnsurePlayerSpecificSetting(player, setting);
            newSettingPlayer.HandleBeforeSettingUpdate();

            NetworkWriterPooled valueWriter = NetworkWriterPool.Get();
            settingBase.SerializeValue(valueWriter);
            newSettingPlayer.Base.DeserializeValue(new NetworkReader(valueWriter.buffer));
            NetworkWriterPool.Return(valueWriter);

            newSettingPlayer.HandleSettingUpdate();
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
        }
    }

    private static CustomSetting EnsurePlayerSpecificSetting(Player player, CustomSetting toMatch)
    {
        List<CustomSetting> settings = ReceivedPlayerSettings.GetOrAdd(player, static () => []);
        CustomSetting? currentSetting = settings.FirstOrDefault(s => s.Id == toMatch.Id);
        if (currentSetting == null)
        {
            currentSetting = toMatch.CreateDuplicate();
            currentSetting.KnownOwner = player;
            settings.Add(currentSetting);
        }

        return currentSetting;
    }

    private void SendSettingUpdate()
    {
        if (!IsCurrentlyAccessible)
            return;

        Base.SendUpdate(Label, DescriptionHint, false, IsKnownOwnerHub);
    }
}
