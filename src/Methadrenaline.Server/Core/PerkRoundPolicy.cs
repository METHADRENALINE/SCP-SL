using System;

namespace Methadrenaline.Server.Core;

public enum CommunityRoundMode { Plain, Perks, Paravoz, Blackout, Escort }

public sealed class PerkRoundPolicy
{
    public const string WithPerksMessage = "Перки раскиданы по карте! Удачи выжить :)";
    public const string WithoutPerksMessage = "Можно пробовать выжить, ведь катка без перков!";
    public const string ParavozMessage = "Паравоз!";
    public CommunityRoundMode? Mode { get; private set; }
    public bool? HasPerks { get; private set; }
    public string Message => Mode switch
    {
        CommunityRoundMode.Escort => "Escort",
        CommunityRoundMode.Blackout => ModeVotePolicy.Label(CommunityRoundMode.Blackout, HasPerks == true),
        CommunityRoundMode.Paravoz => ParavozMessage,
        CommunityRoundMode.Plain when HasPerks == true => ModeVotePolicy.Label(CommunityRoundMode.Plain, true) + "\n" + WithPerksMessage,
        _ => WithoutPerksMessage
    };

    public bool Begin(Func<bool> fairCoin)
    {
        BeginMode(() => CommunityRoundMode.Plain, fairCoin);
        return HasPerks == true;
    }

    public CommunityRoundMode BeginMode(Func<CommunityRoundMode> choose, Func<bool>? perks = null)
    {
        if (!Mode.HasValue)
        {
            var choice = choose();
            Mode = choice == CommunityRoundMode.Perks ? CommunityRoundMode.Plain : choice;
            HasPerks = choice == CommunityRoundMode.Perks || ModeVotePolicy.AllowsPerks(choice) && perks?.Invoke() == true;
        }
        return Mode.Value;
    }

    public void Reset()
    {
        Mode = null;
        HasPerks = null;
    }
}
