namespace SecretAPI.Extensions.Items;

using LabApi.Features.Wrappers;

public static partial class ItemExtensions
{
    public static RadioItem WithBatteryPercent(this RadioItem item, byte battery)
    {
        item.BatteryPercent = battery;
        return item;
    }
}
