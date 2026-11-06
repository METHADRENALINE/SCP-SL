using System.Collections.Generic;
using System;

namespace Methadrenaline.Server.Core;

internal sealed class ParavozCamping<TRoom> where TRoom : notnull
{
    private const int WarningSeconds = 10;
    private readonly List<(TRoom Room, float LastVisit)> recent = new(5);
    private float started;

    internal (int Rooms, float Deadline)? Observe(TRoom room, float now)
    {
        if (recent.Count == 0) started = now;
        int existing = recent.FindIndex(visit => EqualityComparer<TRoom>.Default.Equals(visit.Room, room));
        if (existing >= 0) recent.RemoveAt(existing);
        recent.Insert(0, (room, now));
        if (recent.Count > 5) recent.RemoveAt(5);

        (int Rooms, float Deadline)? penalty = null;
        for (int rooms = 1; rooms <= Math.Min(recent.Count, 4); rooms++)
        {
            // A visit outside the most recent rooms resets time confined to them.
            float since = recent.Count > rooms ? recent[rooms].LastVisit : started;
            float warningAt = since + (rooms switch { 3 => 40, 4 => 55, _ => 25 });
            if (now < warningAt) continue;
            float deadline = warningAt + WarningSeconds;
            // Prefer the first penalty due, then the smallest route already overdue.
            if (penalty == null || penalty.Value.Deadline > now && deadline < penalty.Value.Deadline)
                penalty = (rooms, deadline);
        }
        return penalty;
    }
}
