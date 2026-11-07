namespace SecretAPI.Extensions;

using System.Linq;
using LabApi.Features.Interfaces;
using LabApi.Features.Wrappers;
using Scp914;

public static class Scp914Extensions
{
    public static void Process914Player(this Player player, bool heldOnly, Scp914KnobSetting setting)
    {
        if (heldOnly)
        {
            Process914Item(player.CurrentItem, setting);
            return;
        }

        foreach (Item item in player.Items.ToList())
            Process914Item(item, setting);
    }

    public static void Process914Item(this Item? item, Scp914KnobSetting setting)
    {
        if (item == null)
            return;

        IScp914ItemProcessor? processor = Scp914.GetItemProcessor(item.Type);
        processor?.UpgradeItem(setting, item);
    }
}
