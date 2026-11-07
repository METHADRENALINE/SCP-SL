using Mirror;
using SecretLabNAudio.Core;
using System.Linq;

namespace ScpProximityChat.SecretAPI.Personalization;

public static class PersonalizationManager
{

    private static readonly Dictionary<string, int> IdHashes = [];

    private static readonly Dictionary<string, SpeakerPersonalization> PersonalizationInstances = [];
    private static readonly Dictionary<string, Player> Sources = [];
    private static float _nextUpdate;

    public static int IdFor(string userId)
        => IdHashes.TryGetValue(userId, out var hash)
            ? hash
            : IdHashes[userId] = $"Volume for: {userId}".GetStableHashCode();

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

    public static void Configure(Player receiver, string sourceId, float volume)
    {
        if (PersonalizationInstances.TryGetValue(sourceId, out var personalization) && IsCurrent(sourceId, personalization))
            Apply(personalization, receiver, volume * VolumeHelpers.Master(receiver));
    }

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
