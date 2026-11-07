namespace SecretAPI.Enums;

using System;
using MapGeneration;
using SecretAPI.Extensions;
using UnityEngine;

[Flags]
public enum RoomSafetyFailReason
{
    None = 0,

    Warhead = 1 << 0,

    Decontamination = 1 << 1,

    Tesla = 1 << 2,

    MissingFloor = 1 << 3,

    KnownBad = 1 << 4,
}
