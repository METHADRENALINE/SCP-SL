namespace SecretAPI.Features.Effects;

using CustomPlayerEffects;
using PlayerStatsSystem;

public class BlastResistance : CustomPlayerEffect, IDamageModifierEffect
{
    public override byte MaxIntensity => 200;

    public bool DamageModifierActive => IsEnabled;

    public float GetDamageModifier(float baseDamage, DamageHandlerBase handler, HitboxType hitboxType)
    {
        if (handler is not ExplosionDamageHandler)
            return 1;

        float modifier = 1 - ((float)Intensity / 200);
        return modifier;
    }
}
