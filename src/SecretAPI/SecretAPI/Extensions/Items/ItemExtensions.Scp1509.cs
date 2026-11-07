namespace SecretAPI.Extensions.Items;

using LabApi.Features.Wrappers;

public static partial class ItemExtensions
{
    extension(Scp1509Item item)
    {
        public Scp1509Item WithShieldRegenRate(float rate)
        {
            item.ShieldRegenRate = rate;
            return item;
        }

        public Scp1509Item WithShieldDecayRate(float rate)
        {
            item.ShieldDecayRate = rate;
            return item;
        }

        public Scp1509Item WithUnequipDecayDelay(float time)
        {
            item.UnequipDecayDelay = time;
            return item;
        }

        public Scp1509Item WithReviveCooldown(double cooldown)
        {
            item.ReviveCooldown = cooldown;
            return item;
        }

        public Scp1509Item WithEquippedHumeShield(float hs)
        {
            item.EquippedHS = hs;
            return item;
        }
    }
}
