using PlayerRoles;
using PlayerRoles.Voice;

namespace ScpProximityChat.Core;

public static class HumanVoiceProfile
{
    private static AnimationCurve? _curve;
    private static float _range;

    internal static void Refresh()
    {
        _curve = null;
        if (!ProximityChatPlugin.Cfg.MatchHumanVoiceDistances ||
            !PlayerRoleLoader.AllRoles.TryGetValue(RoleTypeId.ClassD, out var role) ||
            role is not IVoiceRole { VoiceModule: HumanVoiceModule human }) return;
        var source = human.FirstProxPlayback.GetComponent<AudioSource>();
        if (source.rolloffMode != AudioRolloffMode.Custom || source.maxDistance <= 0) return;
        _curve = source.GetCustomCurve(AudioSourceCurveType.CustomRolloff);
        _range = source.maxDistance;
        ProximityChatPlugin.Cfg.AudioSettings = ProximityChatPlugin.Cfg.AudioSettings with
        {
            MinDistance = _range,
            MaxDistance = _range
        };
    }

    public static float GainFor(SpeakerToy speaker, Player receiver)
    {
        if (_curve == null) return 1;
        var viewer = receiver.CurrentlySpectating ?? receiver;
        Vector3 position = viewer.Role == RoleTypeId.Scp079 && viewer.RoleBase is ICameraController camera
            ? camera.CameraPosition : viewer.ReferenceHub.PlayerCameraReference.position;
        float distance = Vector3.Distance(speaker.Transform.position, position);
        return distance >= _range ? 0 : Mathf.Clamp01(_curve.Evaluate(distance / _range));
    }
}
