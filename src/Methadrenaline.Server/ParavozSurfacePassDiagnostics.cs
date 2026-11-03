using System;
using System.Collections.Generic;
using System.Linq;
using LabApi.Features.Wrappers;
using MEC;

namespace Methadrenaline.Server;

internal static class ParavozSurfacePassDiagnostics
{
    internal static IEnumerator<float> Probe(Player runner, Action<string, bool> check)
    {
        var card = runner.AddItem(ItemType.KeycardScientist);
        var pass = Pickup.Create(ItemType.SurfaceAccessPass, runner.Position) ?? throw new InvalidOperationException("Cannot create pass probe.");
        try
        {
            check("paravoz_surface_pass_inventory_grants_blocked", runner.AddItem(ItemType.SurfaceAccessPass) == null);
            yield return Timing.WaitForSeconds(0.1f);
            check("paravoz_surface_pass_late_pickup_removed", pass.IsDestroyed && !Pickup.List.Any(p => p.Type == ItemType.SurfaceAccessPass));
            check("paravoz_normal_keycards_retained", card != null && runner.Items.Contains(card));
        }
        finally
        {
            if (!pass.IsDestroyed) pass.Destroy();
            if (card != null && runner.Items.Contains(card)) runner.RemoveItem(card);
        }
    }
}
