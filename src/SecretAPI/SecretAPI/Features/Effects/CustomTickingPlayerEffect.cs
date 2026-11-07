namespace SecretAPI.Features.Effects;

using CustomPlayerEffects;
using UnityEngine;

public abstract class CustomTickingPlayerEffect : CustomPlayerEffect
{
    private float timeTillTick;

    protected virtual float TimePerTick => 1;

    public override void Enabled()
    {
        timeTillTick = TimePerTick;
    }

    public override void OnEffectUpdate()
    {
        base.OnEffectUpdate();

        if (TimePerTick == 0)
            return;

        timeTillTick -= Time.deltaTime;
        if (timeTillTick > 0)
            return;

        timeTillTick += TimePerTick;
        OnTick();
    }

    public virtual void OnTick()
    {
    }
}
