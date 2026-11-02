using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Methadrenaline.Server.Core;

public sealed class ModeVotePolicy
{
    public const double Duration = 30;
    public const int MinimumPlayers = 2;
    public const string VotingLabel = "Voting for a mode in Discord\n{0} s";
    private readonly Dictionary<string, CommunityRoundMode> votes = new(StringComparer.Ordinal);
    public string? PollId { get; private set; }
    public double Deadline { get; private set; }
    public CommunityRoundMode? Selected { get; private set; }
    public int Players { get; private set; }
    public bool InLobby { get; private set; }
    public bool AdminSelected { get; private set; }
    public bool Open => InLobby && Players >= MinimumPlayers && PollId != null && !Selected.HasValue;
    public CommunityRoundMode[] Available => Players < MinimumPlayers ? Array.Empty<CommunityRoundMode>()
        : new[] { CommunityRoundMode.Plain, CommunityRoundMode.Perks, CommunityRoundMode.Paravoz };

    public void Tick(int players, bool inLobby, double now, Func<int, int> random)
    {
        Players = Math.Max(0, players);
        InLobby = inLobby;
        if (AdminSelected) return;
        if (Players < MinimumPlayers) { Reset(); Players = Math.Max(0, players); InLobby = inLobby; return; }
        if (!inLobby) return;
        if (Selected.HasValue)
        {
            if (!Available.Contains(Selected.Value)) Selected = Choose(random);
            return;
        }
        if (PollId == null) { PollId = Guid.NewGuid().ToString("D"); Deadline = now + Duration; }
        if (now >= Deadline) Selected = Choose(random);
    }

    public string Vote(string? pollId, string? userId, CommunityRoundMode mode, double now)
    {
        if (!Open || pollId != PollId || now >= Deadline) return "closed";
        if (!ValidUser(userId)) throw new ArgumentException("Invalid Discord user ID.");
        if (votes.ContainsKey(userId!)) return "duplicate";
        if (!Available.Contains(mode)) return "closed";
        votes.Add(userId!, mode);
        return "accepted";
    }

    public CommunityRoundMode Finish(Func<int, int> random)
    {
        if (!Selected.HasValue) Selected = Choose(random);
        InLobby = false;
        return Selected.Value;
    }

    public void Override(CommunityRoundMode mode)
    {
        Selected = mode;
        AdminSelected = true;
        votes.Clear();
    }

    private CommunityRoundMode Choose(Func<int, int> random)
    {
        var available = Available;
        if (available.Length == 0) available = new[] { CommunityRoundMode.Plain, CommunityRoundMode.Perks };
        int maximum = available.Max(m => votes.Values.Count(v => v == m));
        var tied = available.Where(m => votes.Values.Count(v => v == m) == maximum).ToArray();
        return tied[random(tied.Length)];
    }

    public void Reset()
    {
        votes.Clear();
        PollId = null;
        Deadline = 0;
        Selected = null;
        Players = 0;
        InLobby = false;
        AdminSelected = false;
    }

    public static string Id(CommunityRoundMode mode) => mode.ToString().ToLowerInvariant();
    public static string Label(CommunityRoundMode mode) => mode == CommunityRoundMode.Plain ? "Vanilla (almost)" : mode == CommunityRoundMode.Perks ? "Perks" : "Paravoz";
    public static bool TryMode(string? value, out CommunityRoundMode mode)
    {
        mode = CommunityRoundMode.Plain;
        switch (value)
        {
            case "plain": return true;
            case "perks": mode = CommunityRoundMode.Perks; return true;
            case "paravoz": mode = CommunityRoundMode.Paravoz; return true;
            default: return false;
        }
    }

    public static bool ValidUser(string? value) => value != null && value.Length <= 20 && value.Length > 0 && value[0] != '0'
        && value.All(c => c >= '0' && c <= '9') && ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _);
}
