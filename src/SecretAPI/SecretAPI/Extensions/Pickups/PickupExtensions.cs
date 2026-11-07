namespace SecretAPI.Extensions.Pickups;

using InventorySystem.Items.Usables.Scp330;
using LabApi.Features.Wrappers;
using Scp330Pickup = LabApi.Features.Wrappers.Scp330Pickup;

public static partial class PickupExtensions
{
    extension<T>(T pickup)
        where T : Pickup
    {
        public T WithWeight(float weight)
        {
            pickup.Weight = weight;
            return pickup;
        }

        public T WithLockedState(bool locked)
        {
            pickup.IsLocked = locked;
            return pickup;
        }
    }

    public static AmmoPickup WithAmmo(this AmmoPickup pickup, ushort ammo)
    {
        pickup.Ammo = ammo;
        return pickup;
    }

    public static Scp330Pickup WithExposedCandy(this Scp330Pickup pickup, CandyKindID kind)
    {
        pickup.ExposedCandy = kind;
        return pickup;
    }
}
