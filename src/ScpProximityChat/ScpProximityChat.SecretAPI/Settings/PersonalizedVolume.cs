namespace ScpProximityChat.SecretAPI.Settings;

internal sealed class PersonalizedVolume : VolumeSettingBase
{

    public string Name { get; }
    public string UserId { get; }

    private PersonalizationVisibility? _visibility;

    public PersonalizedVolume(Player player) : this(player.Nickname, player.UserId)
    {
    }

    public PersonalizedVolume(string name, string userId) : base(PersonalizationManager.IdFor(userId), PersonalizationManager.LabelFor(name), "Volume of this player's SCP local voice, on top of the overall SCP volume. 100% = normal; above 100% = boost; 0% = mute. Only affects what you hear.")
    {
        Name = name;
        UserId = userId;
    }

    protected override bool CanView(Player player)
    {
        if (player.UserId == UserId)
            return false;
        _visibility ??= GetPlayerSetting<PersonalizationVisibility>(SettingsRegistry.PersonalizationVisibility.Id, player);
        return _visibility?.Visible ?? false;
    }

    protected override CustomSetting CreateDuplicate() => new PersonalizedVolume(Name, UserId);

    protected override void HandleSettingUpdate() => PersonalizationManager.Configure(KnownOwner!, UserId, Volume);

    public override CustomHeader Header => Headers.Personalization;

    public override int GetHashCode() => Id;

}
