namespace SecretAPI.Features.Effects;

using PlayerRoles.FirstPersonControl;

public class Energized : CustomPlayerEffect, IStaminaModifier
{
    public bool StaminaModifierActive => IsEnabled;

    public float StaminaUsageMultiplier => 0;

    public override EffectClassification Classification => EffectClassification.Positive;
}
