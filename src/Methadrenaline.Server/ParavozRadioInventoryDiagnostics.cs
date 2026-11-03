using System;
using System.Collections.Generic;
using System.Linq;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using LabApi.Features.Wrappers;
using UnityEngine;

namespace Methadrenaline.Server;

internal static class ParavozRadioInventoryDiagnostics
{
    internal static IReadOnlyDictionary<string, bool> Run(Player carrier, Player runner)
    {
        var checks = new Dictionary<string, bool>();
        var items = carrier.Items.ToArray();
        checks["paravoz_spawns_with_only_one_radio"] = items.Length == 1 && items[0] is RadioItem;
        var radio = items.OfType<RadioItem>().Single();
        checks["paravoz_radio_is_powered_and_full"] = radio.IsUsable && radio.BatteryPercent == 100;
        var denied = new[] { ItemType.KeycardScientist, ItemType.Medkit, ItemType.GunCOM15, ItemType.Coin, ItemType.GrenadeHE, ItemType.ArmorCombat };
        checks["paravoz_nonradio_inventory_grants_rejected"] = denied.All(t => carrier.AddItem(t) == null);
        checks["paravoz_duplicate_radio_rejected"] = carrier.AddItem(ItemType.Radio) == null && carrier.Items.Count() == 1;
        var drop = new PlayerDroppingItemEventArgs(carrier.ReferenceHub, radio.Base, true);
        PlayerEvents.OnDroppingItem(drop);
        checks["paravoz_radio_drop_blocked"] = !drop.IsAllowed;

        Vector3 original = runner.Position;
        try
        {
            runner.Position = carrier.Position;
            foreach (ItemType type in new[] { ItemType.Coin, ItemType.GunCOM15, ItemType.Medkit, ItemType.KeycardScientist, ItemType.Ammo556x45 })
            {
                var pickup = Pickup.Create(type, carrier.Position) ?? throw new InvalidOperationException("Cannot create inventory probe.");
                try
                {
                    checks["paravoz_pickup_search_rejects_" + type] = !pickup.Base.GetSearchCompletor(carrier.ReferenceHub.searchCoordinator, 100).ValidateStart();
                    if (type == ItemType.Coin)
                        checks["paravoz_other_players_still_pick_up_loot"] = pickup.Base.GetSearchCompletor(runner.ReferenceHub.searchCoordinator, 100).ValidateStart();
                }
                finally { pickup.Destroy(); }
            }
            var ordinary = runner.AddItem(ItemType.Medkit);
            checks["paravoz_other_players_still_receive_items"] = ordinary != null;
            if (ordinary != null) runner.RemoveItem(ordinary);
        }
        finally { runner.Position = original; }
        return checks;
    }
}
