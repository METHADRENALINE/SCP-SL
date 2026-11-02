using System;

namespace Methadrenaline.Server.Core;

public enum CommunityRoundMode { Plain, Perks, Paravoz }

public sealed class PerkRoundPolicy
{
    public const string WithPerksMessage = "Перки раскиданы по карте! Удачи выжить :)";
    public const string WithoutPerksMessage = "Можно пробовать выжить, ведь катка без перков!";
    public const string ParavozMessage = "Паравоз!";
    public CommunityRoundMode? Mode { get; private set; }
    public bool? HasPerks => Mode.HasValue ? Mode == CommunityRoundMode.Perks : null;
    public string Message => Mode == CommunityRoundMode.Paravoz ? ParavozMessage : HasPerks == true ? WithPerksMessage : WithoutPerksMessage;

    public bool Begin(Func<bool> fairCoin)
    {
        return BeginMode(() => fairCoin() ? CommunityRoundMode.Perks : CommunityRoundMode.Plain) == CommunityRoundMode.Perks;
    }

    public CommunityRoundMode BeginMode(Func<CommunityRoundMode> choose)
    {
        if (!Mode.HasValue) Mode = choose();
        return Mode.Value;
    }

    public void Reset() => Mode = null;
}
