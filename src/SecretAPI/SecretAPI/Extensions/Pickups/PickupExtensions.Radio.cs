namespace SecretAPI.Extensions.Pickups;

using InventorySystem.Items.Radio;
using RadioPickup = LabApi.Features.Wrappers.RadioPickup;

public static partial class PickupExtensions
{
    extension(RadioPickup pickup)
    {
        public RadioPickup WithEnabledState(bool enabled)
        {
            pickup.IsEnabled = enabled;
            return pickup;
        }

        public RadioPickup WithRangeLevel(RadioMessages.RadioRangeLevel range)
        {
            pickup.RangeLevel = range;
            return pickup;
        }

        public RadioPickup WithBattery(float battery)
        {
            pickup.Battery = battery;
            return pickup;
        }
    }
}
