namespace SecretAPI.Extensions;

using CustomPlayerEffects;
using Interactables.Interobjects.DoorUtils;
using LabApi.Features.Wrappers;
using SecretAPI.Enums;

public static class PlayerExtensions
{
    extension(Player player)
    {
        public StatusEffectBase GetEffect(string name)
            => player.ReferenceHub.playerEffectsController.TryGetEffect(name, out StatusEffectBase? effect) ? effect : null!;

        public bool HasDoorPermission(IDoorPermissionRequester requester, DoorPermissionCheck checkFlags = DoorPermissionCheck.Default)
        {
            if (checkFlags.HasFlagFast(DoorPermissionCheck.Bypass) && player.IsBypassEnabled)
                return true;

            if (checkFlags.HasFlagFast(DoorPermissionCheck.Role) && player.RoleBase is IDoorPermissionProvider roleProvider && requester.PermissionsPolicy.CheckPermissions(roleProvider.GetPermissions(requester)))
                return true;

            foreach (Item item in player.Items)
            {
                bool isCurrent = item == player.CurrentItem;
                if (!checkFlags.HasFlagFast(DoorPermissionCheck.CurrentItem) && isCurrent)
                    continue;

                if (!checkFlags.HasFlagFast(DoorPermissionCheck.InventoryExcludingCurrent) && !isCurrent)
                    continue;

                if (item.Base is IDoorPermissionProvider itemProvider && requester.PermissionsPolicy.CheckPermissions(itemProvider.GetPermissions(requester)))
                    return true;
            }

            return false;
        }

        public bool HasDoorPermission(Door door, DoorPermissionCheck checkFlags = DoorPermissionCheck.Default)
            => player.HasDoorPermission(door.Base, checkFlags);

        public bool HasLockerChamberPermission(LockerChamber chamber, DoorPermissionCheck checkFlags = DoorPermissionCheck.Default)
            => player.HasDoorPermission(chamber.Base, checkFlags);

        public bool HasGeneratorPermission(Generator generator, DoorPermissionCheck checkFlags = DoorPermissionCheck.Default)
            => player.HasDoorPermission(generator.Base, checkFlags);
    }
}
