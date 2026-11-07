namespace SecretAPI.Extensions.Pickups;

using InventorySystem.Items.Jailbird;
using JailbirdPickup = LabApi.Features.Wrappers.JailbirdPickup;

public static partial class PickupExtensions
{
    extension(JailbirdPickup pickup)
    {
        public JailbirdPickup WithTotalDamageDealt(float damage)
        {
            pickup.TotalDamageDealt += damage;
            return pickup;
        }

        public JailbirdPickup WithTotalChargesPerformed(int charges)
        {
            pickup.TotalChargesPerformed += charges;
            return pickup;
        }

        public JailbirdPickup WithWearState(JailbirdWearState state)
        {
            pickup.WearState = state;
            return pickup;
        }
    }
}
