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
    public const string PerksOn = "perks_on";
    public const string PerksOff = "perks_off";
    public const int EscortMinimum = 3;
    private readonly Dictionary<string, string> votes = new(StringComparer.Ordinal);
    public string? PollId { get; private set; }
    public double Deadline { get; private set; }
    public CommunityRoundMode? Selected { get; private set; }
    public bool? Perks { get; private set; }
    public int Players { get; private set; }
    public bool InLobby { get; private set; }
    public bool AdminSelected { get; private set; }
    public bool VotingPerks => Selected.HasValue && !Perks.HasValue;
    public bool Open => InLobby && Players >= MinimumPlayers && PollId != null && !Perks.HasValue;
    public static bool AllowsPerks(CommunityRoundMode mode) => mode is CommunityRoundMode.Plain or CommunityRoundMode.Blackout;

    private static CommunityRoundMode[] Eligible(int players) => players >= EscortMinimum
        ? new[] { CommunityRoundMode.Plain, CommunityRoundMode.Paravoz, CommunityRoundMode.Blackout, CommunityRoundMode.Escort }
        : players >= MinimumPlayers
        ? new[] { CommunityRoundMode.Plain, CommunityRoundMode.Paravoz, CommunityRoundMode.Blackout }
        : new[] { CommunityRoundMode.Plain, CommunityRoundMode.Blackout };

    internal static CommunityRoundMode RandomMode(int players, Func<int, int> random)
    {
        var modes = Eligible(players);
        return modes[random(modes.Length)];
    }

    public CommunityRoundMode[] Available => Players < MinimumPlayers || VotingPerks ? Array.Empty<CommunityRoundMode>() : Eligible(Players);

    public void Tick(int players, bool inLobby, double now, Func<int, int> random)
    {
        Players = Math.Max(0, players);
        InLobby = inLobby;
        if (!inLobby) return;
        if (Players < EscortMinimum && !Selected.HasValue && votes.ContainsValue("escort"))
            foreach (var voter in votes.Where(v => v.Value == "escort").Select(v => v.Key).ToArray())
                votes.Remove(voter);

        if (Selected == CommunityRoundMode.Escort && Players < EscortMinimum ||
            Selected == CommunityRoundMode.Paravoz && Players < MinimumPlayers)
        {
            Reset();
            InLobby = true;
            Players = players;
        }

        if (Players < MinimumPlayers)
        {
            if (Players == 0 && !AdminSelected || !Selected.HasValue) ResetBallots();
            else if (VotingPerks) ClearPoll();
            return;
        }
        if (Perks.HasValue) return;
        if (PollId == null) StartPoll(now);
        if (now < Deadline) return;
        if (VotingPerks) Perks = ChoosePerks(random);
        else
        {
            Selected = ChooseMode(random);
            if (AllowsPerks(Selected.Value)) StartPoll(now);
            else Perks = false;
        }
    }

    private void ClearPoll()
    {
        votes.Clear();
        PollId = null;
        Deadline = 0;
    }

    private void StartPoll(double now)
    {
        votes.Clear();
        PollId = Guid.NewGuid().ToString("D");
        Deadline = now + Duration;
    }

    public string Vote(string? pollId, string? userId, string choice, double now)
    {
        if (!Open || pollId != PollId || now >= Deadline) return "closed";
        if (!ValidUser(userId)) throw new ArgumentException("Invalid Discord user ID.");
        if (VotingPerks ? choice != PerksOn && choice != PerksOff :
            !TryMode(choice, out var mode) || !Available.Contains(mode)) return "closed";
        if (votes.ContainsKey(userId!)) return "duplicate";
        votes.Add(userId!, choice);
        return "accepted";
    }

    public string Vote(string? pollId, string? userId, CommunityRoundMode mode, double now) =>
        Vote(pollId, userId, Id(mode), now);

    public CommunityRoundMode Finish(Func<int, int> random)
    {
        bool finishingPerkVote = VotingPerks && PollId != null;
        if (!Selected.HasValue) Selected = ChooseMode(random);
        if (!Perks.HasValue) Perks = finishingPerkVote && ChoosePerks(random);
        InLobby = false;
        return Selected.Value;
    }

    public void ForceFinish(Func<int, int> random)
    {
        Finish(random);
        AdminSelected = true;
    }

    public void Override(CommunityRoundMode mode)
    {
        ClearPoll();
        AdminSelected = true;
        Selected = mode == CommunityRoundMode.Perks ? CommunityRoundMode.Plain : mode;
        Perks = mode == CommunityRoundMode.Perks ? true : AllowsPerks(mode) ? (bool?)null : false;
    }

    private CommunityRoundMode ChooseMode(Func<int, int> random)
    {
        var available = Eligible(Players);
        int Count(CommunityRoundMode mode) => votes.Values.Count(v => v == Id(mode));
        int maximum = available.Max(Count);
        var tied = available.Where(m => Count(m) == maximum).ToArray();
        return tied.Length == 1 ? tied[0] : tied[random(tied.Length)];
    }

    private bool ChoosePerks(Func<int, int> random)
    {
        int yes = votes.Values.Count(v => v == PerksOn);
        int no = votes.Count - yes;
        return yes == no ? random(2) == 1 : yes > no;
    }

    private void ResetBallots()
    {
        ClearPoll();
        Selected = null;
        Perks = null;
        AdminSelected = false;
    }

    public void Reset()
    {
        ResetBallots();
        Players = 0;
        InLobby = false;
    }

    public static string Id(CommunityRoundMode mode) => mode.ToString().ToLowerInvariant();
    public static string Label(CommunityRoundMode mode, bool withPerks = false)
    {
        string name = mode == CommunityRoundMode.Plain ? "Vanilla (almost)" : mode.ToString();
        return withPerks && AllowsPerks(mode) ? name + " + perks" : name;
    }
    public static bool TryMode(string? value, out CommunityRoundMode mode)
    {
        mode = CommunityRoundMode.Plain;
        switch (value)
        {
            case "plain": return true;
            case "perks": mode = CommunityRoundMode.Perks; return true;
            case "escort": mode = CommunityRoundMode.Escort; return true;
            case "blackout": mode = CommunityRoundMode.Blackout; return true;
            case "paravoz": mode = CommunityRoundMode.Paravoz; return true;
            default: return false;
        }
    }

    public static bool ValidUser(string? value) => value != null && value.Length <= 20 && value.Length > 0 && value[0] != '0'
        && value.All(c => c >= '0' && c <= '9') && ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _);
}
