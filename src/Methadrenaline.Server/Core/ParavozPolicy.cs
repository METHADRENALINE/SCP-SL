using System;
using System.Collections.Generic;
using System.Linq;

namespace Methadrenaline.Server.Core;

public enum ParavozMember { Runner, Paravoz, Hunter, Eliminated }
public enum ParavozWinner { None, Humans, Virus, Draw }

/// <summary>One round's roster; infection survives the victim's native death and respawn.</summary>
public sealed class ParavozPolicy
{
    public const float IsolationSeconds = 60;
    public const float GatherSeconds = 360;
    public const int GatherWarningSeconds = 10;
    public const string GatherMessage = "You will be pulled to Paravoz in {0} seconds!";
    public static int GatherCountdown(float elapsed)
        => elapsed < GatherSeconds - GatherWarningSeconds || elapsed >= GatherSeconds
            ? 0 : (int)Math.Ceiling(GatherSeconds - elapsed);
    public const float GrenadeInterval = 300;
    public const string WaitingMessage = "Release in: {0}";
    public static int WaitingSeconds(float elapsed)
        => Math.Max(0, Math.Min((int)IsolationSeconds, (int)Math.Ceiling(IsolationSeconds - elapsed)));
    private readonly Dictionary<uint, ParavozMember> members = new();
    public IReadOnlyDictionary<uint, ParavozMember> Members => members;
    public void Clear() => members.Clear();

    public void Begin(IEnumerable<uint> participants, uint firstParavoz)
    {
        members.Clear();
        foreach (uint id in participants) members.Add(id, id == firstParavoz ? ParavozMember.Paravoz : ParavozMember.Runner);
        if (members.Count < 2 || !members.ContainsKey(firstParavoz)) throw new ArgumentException("Paravoz requires at least two participants and a valid initial carrier.");
    }

    public bool Is(uint id, ParavozMember state) => members.TryGetValue(id, out var current) && current == state;
    public bool Escape(uint id)
    {
        if (!Is(id, ParavozMember.Runner)) return false;
        members[id] = ParavozMember.Hunter;
        return true;
    }
    public bool Infect(uint victim, uint attacker)
    {
        if (!Is(attacker, ParavozMember.Paravoz) || !members.TryGetValue(victim, out var state) || state is ParavozMember.Paravoz or ParavozMember.Eliminated) return false;
        members[victim] = ParavozMember.Paravoz;
        return true;
    }
    public void Eliminate(uint id) { if (members.ContainsKey(id)) members[id] = ParavozMember.Eliminated; }
    public ParavozWinner Winner
    {
        get
        {
            int runners = members.Values.Count(x => x == ParavozMember.Runner);
            int hunters = members.Values.Count(x => x == ParavozMember.Hunter);
            int infected = members.Values.Count(x => x == ParavozMember.Paravoz);
            if (members.Count == 0) return ParavozWinner.None;
            if (runners + hunters + infected == 0) return ParavozWinner.Draw;
            if (infected == 0 || runners == 0 && hunters > 0) return ParavozWinner.Humans;
            return runners + hunters == 0 ? ParavozWinner.Virus : ParavozWinner.None;
        }
    }
}
