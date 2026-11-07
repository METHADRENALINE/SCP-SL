namespace SecretAPI.Features;

using System.Collections.Generic;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using LabApi.Features.Wrappers;
using SecretAPI.Enums;
using SecretAPI.Extensions;

public static class PlayerRoundIgnore
{
    extension(Player player)
    {
        public RoundIgnoreStatus RoundIgnoreStatus
        {
            get => PlayerToStatus.GetValueOrDefault(player, RoundIgnoreStatus.None);
            set => PlayerToStatus[player] = value;
        }
    }

    static PlayerRoundIgnore()
    {
        SecretApi.Harmony.PatchCategory(nameof(PlayerRoundIgnore));
        PlayerEvents.Left += OnPlayerLeft;
    }

    public static Dictionary<Player, RoundIgnoreStatus> PlayerToStatus { get; } = new();

    private static void OnPlayerLeft(PlayerLeftEventArgs ev) => PlayerToStatus.Remove(ev.Player);
}
