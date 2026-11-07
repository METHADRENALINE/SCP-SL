namespace SecretAPI.Features.Effects;

using PlayerRoles.FirstPersonControl;

public class Depleted : CustomPlayerEffect, IStaminaModifier
{
    public bool StaminaModifierActive => IsEnabled;

    public float StaminaRegenMultiplier => 0;

    public override EffectClassification Classification => EffectClassification.Negative;

    public override void Enabled()
    {
        Owner.StaminaRemaining = 0;
    }

    public override void Disabled()
    {
    }
}
