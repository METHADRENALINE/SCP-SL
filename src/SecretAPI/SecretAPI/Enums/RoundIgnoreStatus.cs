namespace SecretAPI.Enums;

using System;

[Flags]
public enum RoundIgnoreStatus
{
    None = 0,

    RoundEndingCheck = 1 << 0,

    ScpTargetCount = 1 << 1,
}
