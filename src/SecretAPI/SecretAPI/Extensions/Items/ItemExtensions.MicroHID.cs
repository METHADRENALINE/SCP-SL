namespace SecretAPI.Extensions.Items;

using InventorySystem.Items.MicroHID.Modules;
using LabApi.Features.Wrappers;

public static partial class ItemExtensions
{
    extension(MicroHIDItem item)
    {
        public MicroHIDItem WithEnergy(float energy)
        {
            item.Energy = energy;
            return item;
        }

        public MicroHIDItem WithBrokenState(bool brokenState)
        {
            item.IsBroken = brokenState;
            return item;
        }

        public MicroHIDItem WithPhase(MicroHidPhase phase)
        {
            item.Phase = phase;
            return item;
        }

        public MicroHIDItem WithFiringMode(MicroHidFiringMode mode)
        {
            item.FiringMode = mode;
            return item;
        }
    }
}
