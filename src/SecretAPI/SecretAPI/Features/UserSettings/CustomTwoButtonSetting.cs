namespace SecretAPI.Features.UserSettings;

using global::UserSettings.ServerSpecific;

public abstract class CustomTwoButtonSetting : CustomSetting, ISetting<SSTwoButtonsSetting>
{
    protected CustomTwoButtonSetting(SSTwoButtonsSetting button)
        : base(button)
    {
        Base = button;
    }

    protected CustomTwoButtonSetting(
        int? id,
        string label,
        string optionA,
        string optionB,
        bool defaultIsB = false,
        string? hint = null,
        byte collectionId = byte.MaxValue,
        bool isServerSetting = false)
        : this(new SSTwoButtonsSetting(id, label, optionA, optionB, defaultIsB, hint, collectionId, isServerSetting))
    {
    }

    public new SSTwoButtonsSetting Base { get; }

    public string OptionA
    {
        get => Base.OptionA;
        set
        {
            Base.OptionA = value;
            SendOptionsUpdate();
        }
    }

    public string OptionB
    {
        get => Base.OptionB;
        set
        {
            Base.OptionB = value;
            SendOptionsUpdate();
        }
    }

    public override bool HasValueChanged => WasLastOptionB != IsOptionB;

    public bool WasLastOptionB { get; private set; }

    public bool IsOptionA => Base.SyncIsA;

    public bool IsOptionB => Base.SyncIsB;

    public bool IsDefault => Base.DefaultIsB ? IsOptionB : IsOptionA;

    public void SendServerUpdate(bool isB) => Base.SendValueUpdate(isB, false, IsKnownOwnerHub);

    protected override void HandleBeforeSettingUpdate()
    {
        base.HandleBeforeSettingUpdate();
        WasLastOptionB = IsOptionB;
    }

    private void SendOptionsUpdate()
    {
        if (!IsCurrentlyAccessible)
            return;

        Base.SendTwoButtonUpdate(OptionA, OptionB, false, IsKnownOwnerHub);
    }
}
