namespace ScpProximityChat.SecretAPI.Settings;

internal abstract class VolumeSettingBase : CustomSliderSetting
{

    protected VolumeSettingBase(int? id, string label, string? hint = null)
        : base(id, label, 0, 200, 100, true, "0'%'", hint: hint)
        => IsShared = SettingsRegistry.Shared;

    public float Volume => Mathf.Clamp(SelectedValueInt, 0, 200) * 0.01f;

}
