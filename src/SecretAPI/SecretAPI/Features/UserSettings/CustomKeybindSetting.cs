namespace SecretAPI.Features.UserSettings;

using global::UserSettings.ServerSpecific;
using UnityEngine;

public abstract class CustomKeybindSetting : CustomSetting, ISetting<SSKeybindSetting>
{
    protected CustomKeybindSetting(SSKeybindSetting setting)
        : base(setting)
    {
        Base = setting;
    }

    protected CustomKeybindSetting(
        int? id,
        string label,
        KeyCode suggestedKey = KeyCode.None,
        bool preventInteractionOnGui = true,
        bool allowSpectatorTrigger = true,
        string? hint = null,
        byte collectionId = byte.MaxValue)
        : this(new SSKeybindSetting(id, label, suggestedKey, preventInteractionOnGui, allowSpectatorTrigger, hint, collectionId))
    {
    }

    public new SSKeybindSetting Base { get; }

    public bool IsPressed => Base.SyncIsPressed;
}
