namespace SecretAPI.Extensions;

using System;
using SecretAPI.Enums;

public static class FastEnums
{
    public static bool HasFlagFast(this DoorPermissionCheck @enum, DoorPermissionCheck flag) => (@enum & flag) == flag;

    public static bool HasFlagFast(this RoomSafetyFailReason @enum, RoomSafetyFailReason flag) => (@enum & flag) == flag;

    public static bool HasFlagFast(this RoundIgnoreStatus @enum, RoundIgnoreStatus flag) => (@enum & flag) == flag;
}
