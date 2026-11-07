namespace SecretAPI.Features.UserSettings;

using global::UserSettings.ServerSpecific;
using TMPro;

public abstract class CustomTextAreaSetting : CustomSetting, ISetting<SSTextArea>
{
    protected CustomTextAreaSetting(SSTextArea setting)
        : base(setting)
    {
        Base = setting;
    }

    protected CustomTextAreaSetting(
        int? id,
        string content,
        SSTextArea.FoldoutMode foldoutMode = SSTextArea.FoldoutMode.NotCollapsable,
        string? collapsedText = null,
        TextAlignmentOptions textAlignment = TextAlignmentOptions.TopLeft)
        : this(new SSTextArea(id, content, foldoutMode, collapsedText, textAlignment))
    {
    }

    public new SSTextArea Base { get; }

    public string Content
    {
        get => Label;
        set => Label = value;
    }

    public SSTextArea.FoldoutMode Foldout => Base.Foldout;

    public TextAlignmentOptions AlignmentOptions => Base.AlignmentOptions;
}
