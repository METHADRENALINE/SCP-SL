namespace SecretAPI.Features.UserSettings;

using global::UserSettings.ServerSpecific;

public class CustomHeader : ISetting<SSGroupHeader>
{
    public CustomHeader(string label, bool reducedPadding = false, string? hint = null)
    {
        Base = new SSGroupHeader(label, reducedPadding, hint);
    }

    public static CustomHeader Examples { get; } = new("Examples", hint: "Features used as examples");

    public SSGroupHeader Base { get; }

    public string Label => Base.Label;

    public bool ReducedPadding => Base.ReducedPadding;
}
