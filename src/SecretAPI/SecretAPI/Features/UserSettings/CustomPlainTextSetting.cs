namespace SecretAPI.Features.UserSettings;

using System;
using global::UserSettings.ServerSpecific;
using TMPro;

public abstract class CustomPlainTextSetting : CustomSetting, ISetting<SSPlaintextSetting>
{
    protected CustomPlainTextSetting(SSPlaintextSetting setting)
        : base(setting)
    {
        Base = setting;
    }

    protected CustomPlainTextSetting(
        int? id,
        string label,
        string placeholder = "...",
        int characterLimit = 64,
        TMP_InputField.ContentType contentType = TMP_InputField.ContentType.Standard,
        string? hint = null,
        byte collectionId = byte.MaxValue,
        bool isServerSetting = false)
        : this(new SSPlaintextSetting(id, label, placeholder, characterLimit, contentType, hint, collectionId, isServerSetting))
    {
    }

    public new SSPlaintextSetting Base { get; }

    public override bool HasValueChanged => LastInputText != InputText;

    public string LastInputText { get; private set; } = string.Empty;

    public string InputText => Base.SyncInputText;

    public TMP_InputField.ContentType ContentType
    {
        get => Base.ContentType;
        set
        {
            Base.ContentType = value;
            SendPlaintextUpdate();
        }
    }

    public string Placeholder
    {
        get => Base.Placeholder;
        set
        {
            Base.Placeholder = value;
            SendPlaintextUpdate();
        }
    }

    public int CharacterLimit
    {
        get => Base.CharacterLimit;
        set
        {
            Base.CharacterLimit = value;
            SendPlaintextUpdate();
        }
    }

    public void SendServerUpdate(string text) => Base.SendValueUpdate(text, false, IsKnownOwnerHub);

    protected override void HandleBeforeSettingUpdate()
    {
        base.HandleBeforeSettingUpdate();
        if (LastUpdateType != SettingResponseType.Initial)
            LastInputText = InputText;
    }

    private void SendPlaintextUpdate()
    {
        if (!IsCurrentlyAccessible)
            return;

        Base.SendPlaintextUpdate(Placeholder, (ushort)Math.Clamp(CharacterLimit, ushort.MinValue, ushort.MaxValue), ContentType, false, IsKnownOwnerHub);
    }
}
