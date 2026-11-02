using Mirror;
using SecretLabNAudio.Core;
using System.Linq;

namespace ScpProximityChat.SecretAPI.Personalization;

/// <summary>Manages SCP Proximity Chat volume personalization.</summary>
public static class PersonalizationManager
{

    private static readonly Dictionary<string, int> IdHashes = [];

    private static readonly Dictionary<string, SpeakerPersonalization> PersonalizationInstances = [];
    private static readonly Dictionary<string, Player> Sources = [];
    private static float _nextUpdate;

    /// <summary>
    /// Gets the personalized volume setting ID for the given user ID.
    /// </summary>
    /// <param name="userId">The user ID to get the setting ID for.</param>
    /// <returns>The setting ID.</returns>
    public static int IdFor(string userId)
        => IdHashes.TryGetValue(userId, out var hash)
            ? hash
            : IdHashes[userId] = $"Volume for: {userId}".GetStableHashCode();

    /// <summary>
    /// Gets the label for the personalized volume setting for the given user ID.
    /// </summary>
    /// <param name="name">The user ID to get the label for.</param>
    /// <returns>The label.</returns>
    public static string LabelFor(string name) => $"{Translation.VolumeFor}{name}";

    private static bool IsPersonalized(CustomSetting setting) => setting is PersonalizedVolume;

    internal static void Reset()
    {
        IdHashes.Clear();
        PersonalizationInstances.Clear();
        Sources.Clear();
        _nextUpdate = 0;
        var removed = CustomSetting.CustomSettings.RemoveAll(IsPersonalized);
        SettingsRegistry.All.RemoveWhere(IsPersonalized);
        if (removed != 0)
            CustomSetting.ResyncServer();
    }

    /// <summary>
    /// Adds the player to the personalization list.
    /// </summary>
    /// <param name="player">The player to create a personalized volume entry for.</param>
    public static void Register(Player player)
    {
        if (!player.IsReady || string.IsNullOrEmpty(player.UserId))
            return;
        var setting = new PersonalizedVolume(player);
        if (!SettingsRegistry.All.Add(setting))
            return;
        CustomSetting.CustomSettings.Add(setting);
        CustomSetting.ResyncServer();
    }

    /// <summary>
    /// Unregisters the player from the personalization list.
    /// </summary>
    /// <param name="player">The player to remove the personalized volume entry of.</param>
    public static void Unregister(Player player)
    {
        if (string.IsNullOrEmpty(player.UserId))
            return;
        PersonalizationInstances.Remove(player.UserId);
        Sources.Remove(player.UserId);
        var setting = CustomSetting.CustomSettings.FindVolumeSetting(player.UserId);
        if (setting == null)
            return;
        CustomSetting.CustomSettings.Remove(setting);
        SettingsRegistry.All.Remove(setting);
        CustomSetting.ResyncServer();
    }

    /// <summary>
    /// Registers a <see cref="SpeakerPersonalization"/> instance and configures the initial volume for others.
    /// </summary>
    /// <param name="source">The source of audio.</param>
    /// <param name="personalization">The <see cref="SpeakerPersonalization"/> instance to register.</param>
    public static void RegisterPersonalization(Player source, SpeakerPersonalization personalization)
    {
        if (!source.IsReady || string.IsNullOrEmpty(source.UserId))
            return;
        PersonalizationInstances[source.UserId] = personalization;
        Sources[source.UserId] = source;
        foreach (var target in Player.ReadyList)
            if (target != source)
                Apply(personalization, target, VolumeHelpers.Personalized(target, source.UserId));
    }

    /// <summary>
    /// Configures a specific source's volume for a specific receiver.
    /// </summary>
    /// <param name="receiver">The player receiving audio.</param>
    /// <param name="sourceId">The user ID of the source of audio.</param>
    /// <param name="volume">The volume to set.</param>
    public static void Configure(Player receiver, string sourceId, float volume)
    {
        if (PersonalizationInstances.TryGetValue(sourceId, out var personalization) && IsCurrent(sourceId, personalization))
            Apply(personalization, receiver, volume * VolumeHelpers.Master(receiver));
    }

    /// <summary>
    /// Configures all sources for a specific receiver.
    /// </summary>
    /// <param name="receiver">The player receiving audio.</param>
    public static void ConfigureAll(Player receiver)
    {
        foreach (var kvp in PersonalizationInstances)
            if (IsCurrent(kvp.Key, kvp.Value)) Apply(kvp.Value, receiver, VolumeHelpers.Personalized(receiver, kvp.Key));
    }

    private static bool IsCurrent(string id, SpeakerPersonalization personalization)
        => Sources.TryGetValue(id, out var source) && !source.IsDestroyed && personalization != null &&
            !personalization.Speaker.IsDestroyed && source.GetProximitySpeaker() == personalization.Speaker;

    private static void Apply(SpeakerPersonalization personalization, Player receiver, float volume)
    {
        float gain = volume * HumanVoiceProfile.GainFor(personalization.Speaker, receiver);
        if (personalization[receiver] is { } previous && (previous.Volume == gain ||
            previous.Volume > 0 && gain > 0 && Mathf.Abs(previous.Volume - gain) < 0.001f)) return;
        personalization.OverrideVolume(receiver, gain);
    }

    internal static void UpdateVolumes()
    {
        if (Time.unscaledTime < _nextUpdate) return;
        _nextUpdate = Time.unscaledTime + 0.05f;
        var receivers = Player.ReadyList;
        foreach (var entry in PersonalizationInstances.ToArray())
        {
            if (!IsCurrent(entry.Key, entry.Value))
            {
                PersonalizationInstances.Remove(entry.Key);
                Sources.Remove(entry.Key);
                continue;
            }
            var source = Sources[entry.Key];
            foreach (var receiver in receivers)
                if (receiver != source) Apply(entry.Value, receiver, VolumeHelpers.Personalized(receiver, entry.Key));
        }
    }

}
