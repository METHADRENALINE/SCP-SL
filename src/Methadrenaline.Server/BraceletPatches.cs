using HarmonyLib;
using InventorySystem.Disarming;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;

namespace Methadrenaline.Server;

[HarmonyPatch(typeof(DisarmedPlayers), "ValidateEntry")]
internal static class PreserveBraceletRangePatch
{
    private static void Postfix(DisarmedPlayers.DisarmedEntry entry, ref bool __result)
    {
        if (!__result && ServerPlugin.Instance?.Bracelets?.KeepCuffs(entry) == true) __result = true;
    }
}

[HarmonyPatch(typeof(PlayerEvents), nameof(PlayerEvents.OnHurting))]
internal static class BraceletExecutionDamagePatch
{
    private static bool Prefix(PlayerHurtingEventArgs __0)
    {
        var ev = __0;
        if (ServerPlugin.Instance?.Bracelets?.IsExecuting(ev.Player) != true) return true;
        ev.IsAllowed = true;
        return false;
    }
}

[HarmonyPatch(typeof(PlayerEvents), nameof(PlayerEvents.OnDying))]
internal static class BraceletExecutionDeathPatch
{
    private static bool Prefix(PlayerDyingEventArgs __0)
    {
        var ev = __0;
        if (ServerPlugin.Instance?.Bracelets?.IsExecuting(ev.Player) != true) return true;
        ev.IsAllowed = true;
        return false;
    }
}

[HarmonyPatch(typeof(PlayerEvents), nameof(PlayerEvents.OnChangingRole))]
internal static class BraceletExecutionRolePatch
{
    private static void Postfix(PlayerChangingRoleEventArgs __0)
    {
        var ev = __0;
        if (ev.ChangeReason == PlayerRoles.RoleChangeReason.Died && ServerPlugin.Instance?.Bracelets?.IsExecuting(ev.Player) == true)
        {
            ev.IsAllowed = true;
            ev.NewRole = PlayerRoles.RoleTypeId.Spectator;
        }
        else if (ev.ChangeReason != PlayerRoles.RoleChangeReason.Died && ServerPlugin.Instance?.Bracelets?.IsCommitted(ev.Player) == true)
            ev.IsAllowed = false;
    }
}
