namespace SecretAPI.Features.UserSettings;

using System;
using global::UserSettings.ServerSpecific;

public abstract class CustomDropdownSetting : CustomSetting, ISetting<SSDropdownSetting>
{
    protected CustomDropdownSetting(SSDropdownSetting setting)
        : base(setting)
    {
        Base = setting;
    }

    protected CustomDropdownSetting(
        int? id,
        string label,
        string[] options,
        int defaultOptionIndex = 0,
        SSDropdownSetting.DropdownEntryType entryType = SSDropdownSetting.DropdownEntryType.Regular,
        string? hint = null,
        byte collectionId = byte.MaxValue,
        bool isServerSetting = false)
        : this(new SSDropdownSetting(id, label, options, defaultOptionIndex, entryType, hint, collectionId, isServerSetting))
    {
    }

    public new SSDropdownSetting Base { get; }

    public override bool HasValueChanged => LastSelectedIndex != SyncedIndex;

    public int SyncedIndex => Base.SyncSelectionIndexRaw;

    public int ValidatedSelectedIndex => Math.Clamp(SyncedIndex, 0, Options.Length - 1);

    public string[] Options
    {
        get => Base.Options;
        set
        {
            Base.Options = value;
            SendDropdownUpdate();
        }
    }

    public string SelectedOption => Options[ValidatedSelectedIndex];

    public SSDropdownSetting.DropdownEntryType EntryType => Base.EntryType;

    public int LastSelectedIndex { get; private set; } = -1;

    public string? LastSelectedOption => LastSelectedIndex < 0 || LastSelectedIndex >= Options.Length ? null : Options[LastSelectedIndex];

    public void SendServerUpdate(int selectionId) => Base.SendValueUpdate(selectionId, false, IsKnownOwnerHub);

    protected override void HandleBeforeSettingUpdate()
    {
        base.HandleBeforeSettingUpdate();

        if (LastUpdateType != SettingResponseType.Initial)
            LastSelectedIndex = SyncedIndex;
    }

    private void SendDropdownUpdate()
    {
        if (!IsCurrentlyAccessible)
            return;

        Base.SendDropdownUpdate(Options, false, IsKnownOwnerHub);
    }
}
