namespace SecretAPI.Enums;

using System;
using Interactables.Interobjects.DoorUtils;
using LabApi.Features.Wrappers;
using PlayerRoles;
using PlayerStatsSystem;
using SecretAPI.Extensions;

[Flags]
public enum DoorPermissionCheck
{
    None = 0,

    Bypass = 1 << 0,

    Role = 1 << 1,

    CurrentItem = 1 << 2,

    InventoryExcludingCurrent = 1 << 3,

    FullInventory = CurrentItem | InventoryExcludingCurrent,

    All = -1,

    Default = Bypass | Role | CurrentItem,
}
