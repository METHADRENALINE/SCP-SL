namespace SecretAPI.Extensions.Pickups;

using LabApi.Features.Wrappers;
using UnityEngine;

public static partial class PickupExtensions
{
    extension<T>(T pickup)
        where T : TimedGrenadeProjectile
    {
        public T WithRemainingTime(double time)
        {
            pickup.RemainingTime = time;
            return pickup;
        }
    }

    extension(ExplosiveGrenadeProjectile pickup)
    {
        public ExplosiveGrenadeProjectile WithMaxRadius(float radius)
        {
            pickup.MaxRadius = radius;
            return pickup;
        }

        public ExplosiveGrenadeProjectile WithScpDamageMultiplier(float multiplier)
        {
            pickup.ScpDamageMultiplier = multiplier;
            return pickup;
        }
    }

    public static FlashbangProjectile WithBlindTime(this FlashbangProjectile pickup, float time)
    {
        pickup.BaseBlindTime = time;
        return pickup;
    }

    public static Scp018Projectile WithVelocity(this Scp018Projectile pickup, Vector3 velocity)
    {
        pickup.Velocity = velocity;
        return pickup;
    }

    public static Scp2176Projectile WithLockdownDuration(this Scp2176Projectile pickup, float duration)
    {
        pickup.LockdownDuration = duration;
        return pickup;
    }
}
