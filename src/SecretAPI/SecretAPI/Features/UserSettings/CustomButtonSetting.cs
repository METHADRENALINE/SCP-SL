namespace SecretAPI.Features.UserSettings;

using System;
using System.Diagnostics;
using global::UserSettings.ServerSpecific;

public abstract class CustomButtonSetting : CustomSetting, ISetting<SSButton>
{
    protected CustomButtonSetting(SSButton button)
        : base(button)
    {
        Base = button;
    }

    protected CustomButtonSetting(int? id, string label, string buttonText, float? holdTimeSeconds = null, string? hint = null)
        : this(new SSButton(id, label, buttonText, holdTimeSeconds, hint))
    {
    }

    public new SSButton Base { get; }

    public Stopwatch LastPressWatch => Base.SyncLastPress;

    [Obsolete("Use TimeSinceLastPress instead - This will be removed in 4.0")]
    public TimeSpan LastPress => TimeSinceLastPress;

    public TimeSpan TimeSinceLastPress => LastPressWatch.Elapsed;

    public bool EverPressed => LastPressWatch.IsRunning;

    public string Text
    {
        get => Base.ButtonText;
        set
        {
            Base.ButtonText = value;
            SendButtonUpdate();
        }
    }

    public float RequiredHoldTime
    {
        get => Base.HoldTimeSeconds;
        set
        {
            Base.HoldTimeSeconds = value;
            SendButtonUpdate();
        }
    }

    private void SendButtonUpdate()
    {
        if (!IsCurrentlyAccessible)
            return;

        Base.SendButtonUpdate(Text, RequiredHoldTime, false, IsKnownOwnerHub);
    }
}
