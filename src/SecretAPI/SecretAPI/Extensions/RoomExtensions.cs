namespace SecretAPI.Extensions;

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using LabApi.Features.Wrappers;
using MapGeneration;
using PlayerRoles.FirstPersonControl;
using PlayerRoles.PlayableScps.Scp106;
using SecretAPI.Enums;
using UnityEngine;

public static class RoomExtensions
{
    private const float RaycastDistance = 2;

    public static List<RoomName> KnownUnsafeRooms { get; } =
    [
        RoomName.EzEvacShelter,
        RoomName.EzCollapsedTunnel,
        RoomName.HczWaysideIncinerator,
        RoomName.Hcz096,
    ];

    public static bool IsSafeToTeleport(this Room room, RoomSafetyFailReason failReasons)
    {
        if (failReasons.HasFlagFast(RoomSafetyFailReason.Warhead) && Warhead.IsDetonated && room.Zone != FacilityZone.Surface)
            return false;

        if (failReasons.HasFlagFast(RoomSafetyFailReason.Decontamination) && Decontamination.IsDecontaminating && room.Zone == FacilityZone.LightContainment)
            return false;

        if (failReasons.HasFlagFast(RoomSafetyFailReason.Tesla) && room.Name == RoomName.HczTesla)
            return false;

        if (failReasons.HasFlagFast(RoomSafetyFailReason.KnownBad) && KnownUnsafeRooms.Contains(room.Name))
            return false;

        if (failReasons.HasFlagFast(RoomSafetyFailReason.MissingFloor) && !Physics.Raycast(room.Position, Vector3.down, out _, RaycastDistance, FpcStateProcessor.Mask))
            return false;

        return true;
    }

    public static bool TryGetTeleportLocation(this Player player, [NotNullWhen(true)] out Vector3? position, FacilityZone zone = FacilityZone.None, float defaultRadius = Scp106PocketExitFinder.RaycastRange)
    {
        position = null;
        return player.RoleBase is IFpcRole fpc && TryGetTeleportLocation(fpc, out position, zone);
    }

    public static bool TryGetTeleportLocation(this IFpcRole fpc, [NotNullWhen(true)] out Vector3? position, FacilityZone zone = FacilityZone.None, float defaultRadius = Scp106PocketExitFinder.RaycastRange)
    {
        position = null;

        IEnumerable<Pose> poses = zone == FacilityZone.None
            ? SafeLocationFinder.GetLocations(null, null)
            : Scp106PocketExitFinder.GetPosesForZone(zone);

        if (!poses.TryGetRandomValue(out Pose pose))
            return false;

        float radius = defaultRadius;
        if (Room.TryGetRoomAtPosition(pose.position, out Room? room))
            radius = Scp106PocketExitFinder.GetRaycastRange(room.Zone);

        position = SafeLocationFinder.GetSafePosition(pose.position, pose.forward, radius, fpc.FpcModule.CharController);
        return true;
    }
}
