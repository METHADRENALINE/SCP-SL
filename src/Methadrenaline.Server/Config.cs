using System;

namespace Methadrenaline.Server;

public sealed class Config
{
    public bool ModeVotingEnabled { get; set; } = false;
    public string ModeVotingBindAddress { get; set; } = "127.0.0.1";
    public int ModeVotingPort { get; set; } = 8790;
    public string ModeVotingTokenFile { get; set; } = "";
    public string[] BasicAsnExemptUserIds { get; set; } = Array.Empty<string>();
    public bool PreferredScpRoles { get; set; } = true;
    public bool RadiosForHumans { get; set; } = true;
    public bool ClassDStartingCard { get; set; } = true;
    public float SmallServerRespawnLimit { get; set; } = 90;
    public bool EventsEnabled { get; set; } = true;
    public float FirstChaosMinimumDelay { get; set; } = 45;
    public float FirstChaosMaximumDelay { get; set; } = 120;
    public float MinimumChaosGap { get; set; } = 90;
    public float MaximumChaosGap { get; set; } = 180;
    public float EventDuration { get; set; } = 22;
    public float GravityMultiplier { get; set; } = 0.35f;
    public float LevitationAcceleration { get; set; } = 12;
    public int MinimumEventPlayers { get; set; } = 1;
    public int MaximumAffectedRooms { get; set; } = 16;
    public int MaximumAffectedDoors { get; set; } = 32;
    public float DoorPulseInterval { get; set; } = 1.5f;
    public float SignalPulseInterval { get; set; } = 5;

    public void Validate()
    {
        if (ModeVotingEnabled && (ModeVotingPort < 1024 || ModeVotingPort > 65535 || !Voting.ModeVoteHttpService.PrivateAddress(ModeVotingBindAddress)))
            throw new ArgumentException("Mode voting requires a specific private IPv4 address and an unprivileged port.");
        SmallServerRespawnLimit = Finite(SmallServerRespawnLimit, 90, 60, 180);
        FirstChaosMinimumDelay = Finite(FirstChaosMinimumDelay, 45, 15, 300);
        FirstChaosMaximumDelay = Finite(FirstChaosMaximumDelay, 120, FirstChaosMinimumDelay, 600);
        MinimumChaosGap = Finite(MinimumChaosGap, 90, 60, 600);
        MaximumChaosGap = Finite(MaximumChaosGap, 180, MinimumChaosGap, 900);
        EventDuration = Finite(EventDuration, 22, 5, 40);
        GravityMultiplier = Finite(GravityMultiplier, 0.35f, 0.35f, 0.8f);
        LevitationAcceleration = Finite(LevitationAcceleration, 12, 3, 25);
        MinimumEventPlayers = Math.Max(1, Math.Min(20, MinimumEventPlayers));
        MaximumAffectedRooms = Math.Max(1, Math.Min(16, MaximumAffectedRooms));
        MaximumAffectedDoors = Math.Max(1, Math.Min(32, MaximumAffectedDoors));
        DoorPulseInterval = Finite(DoorPulseInterval, 1.5f, 1, 10);
        SignalPulseInterval = Finite(SignalPulseInterval, 5, 3, 15);
    }

    private static float Finite(float value, float fallback, float min, float max)
        => Math.Max(min, Math.Min(max, float.IsNaN(value) || float.IsInfinity(value) ? fallback : value));
}
