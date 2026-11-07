namespace SecretAPI.Features.UserSettings;

using global::UserSettings.ServerSpecific;
using UnityEngine;

public abstract class CustomSliderSetting : CustomSetting, ISetting<SSSliderSetting>
{
    protected CustomSliderSetting(SSSliderSetting setting)
        : base(setting)
    {
        Base = setting;
    }

    protected CustomSliderSetting(
        int? id,
        string label,
        float minValue,
        float maxValue,
        float defaultValue = 0.0f,
        bool integer = false,
        string valueToStringFormat = "0.##",
        string finalDisplayFormat = "{0}",
        string? hint = null,
        byte collectionId = byte.MaxValue,
        bool isServerSetting = false)
        : this(new SSSliderSetting(id, label, minValue, maxValue, defaultValue, integer, valueToStringFormat, finalDisplayFormat, hint, collectionId, isServerSetting))
    {
    }

    public new SSSliderSetting Base { get; }

    public override bool HasValueChanged => !Mathf.Approximately(LastSelectedValueFloat, SelectedValueFloat);

    public float LastSelectedValueFloat { get; private set; }

    public int LastSelectedValueInt => Mathf.RoundToInt(LastSelectedValueFloat);

    public float SelectedValueFloat => Base.SyncFloatValue;

    public int SelectedValueInt => Base.SyncIntValue;

    public string ValueToStringFormat
    {
        get => Base.ValueToStringFormat;
        set
        {
            Base.ValueToStringFormat = value;
            SendSliderUpdate();
        }
    }

    public string FinalDisplayFormat
    {
        get => Base.FinalDisplayFormat;
        set
        {
            Base.FinalDisplayFormat = value;
            SendSliderUpdate();
        }
    }

    public float MinimumValue
    {
        get => Base.MinValue;
        set
        {
            Base.MinValue = value;
            SendSliderUpdate();
        }
    }

    public float MaximumValue
    {
        get => Base.MaxValue;
        set
        {
            Base.MaxValue = value;
            SendSliderUpdate();
        }
    }

    public float DefaultValue
    {
        get => Base.DefaultValue;
        set => Base.DefaultValue = value;
    }

    public bool UseInteger
    {
        get => Base.Integer;
        set
        {
            Base.Integer = value;
            SendSliderUpdate();
        }
    }

    public void SendServerUpdate(float value) => Base.SendValueUpdate(value, false, IsKnownOwnerHub);

    protected override void HandleBeforeSettingUpdate()
    {
        base.HandleBeforeSettingUpdate();
        LastSelectedValueFloat = SelectedValueFloat;
    }

    private void SendSliderUpdate()
    {
        if (!IsCurrentlyAccessible)
            return;

        Base.SendSliderUpdate(MinimumValue, MaximumValue, UseInteger, ValueToStringFormat, FinalDisplayFormat, false, IsKnownOwnerHub);
    }
}
