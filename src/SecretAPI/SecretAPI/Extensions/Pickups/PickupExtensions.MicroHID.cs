namespace SecretAPI.Extensions.Pickups;

using InventorySystem.Items.MicroHID.Modules;
using LabApi.Features.Wrappers;

public static partial class PickupExtensions
{
    extension(MicroHIDPickup pickup)
    {
        public MicroHIDPickup WithEnergy(float energy)
        {
            pickup.Energy = energy;
            return pickup;
        }

        public MicroHIDPickup WithPhase(MicroHidPhase phase)
        {
            pickup.Phase = phase;
            return pickup;
        }

        public MicroHIDPickup WithFiringMode(MicroHidFiringMode mode)
        {
            pickup.FiringMode = mode;
            return pickup;
        }
    }
}
