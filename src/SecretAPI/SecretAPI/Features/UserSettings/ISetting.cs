namespace SecretAPI.Features.UserSettings;

using global::UserSettings.ServerSpecific;

public interface ISetting<out T>
    where T : ServerSpecificSettingBase
{
    public T Base { get; }
}
