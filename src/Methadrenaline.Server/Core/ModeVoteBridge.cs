using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Methadrenaline.Server.Core;

public sealed class ModeVoteSnapshot
{
    private readonly byte[] json;
    private readonly long published = Stopwatch.GetTimestamp();
    public ModeVoteSnapshot(ModeVoteStatus status) { json = ModeVoteJson.Write(status); }
    public byte[] Json => (byte[])json.Clone();
    public bool Fresh => (Stopwatch.GetTimestamp() - published) / (double)Stopwatch.Frequency < 5;
}

public sealed class ModeVoteBridge : IDisposable
{
    private readonly ConcurrentQueue<PendingVote> queue = new();
    private ModeVoteSnapshot? snapshot;
    private int pending;
    private int disposed;
    public ModeVoteSnapshot? Snapshot => Volatile.Read(ref snapshot);
    public void Publish(ModeVoteStatus status) => Volatile.Write(ref snapshot, new ModeVoteSnapshot(status));

    public PendingVote? Enqueue(ModeVoteRequest request)
    {
        if (Volatile.Read(ref disposed) != 0 || Snapshot?.Fresh != true) return null;
        if (Interlocked.Increment(ref pending) > 64) { Interlocked.Decrement(ref pending); return null; }
        var vote = new PendingVote(request);
        queue.Enqueue(vote);
        if (Volatile.Read(ref disposed) != 0) Dispose();
        return vote;
    }

    public void Drain(Func<ModeVoteRequest, string> accept)
    {
        for (int i = 0; i < 64 && queue.TryDequeue(out var vote); i++)
        {
            Interlocked.Decrement(ref pending);
            vote.Execute(accept);
        }
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref disposed, 1);
        while (queue.TryDequeue(out var vote)) { Interlocked.Decrement(ref pending); vote.Cancel(); }
    }
}

public sealed class PendingVote
{
    private readonly ModeVoteRequest request;
    private readonly long created = Stopwatch.GetTimestamp();
    private readonly TaskCompletionSource<string?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int state;
    public PendingVote(ModeVoteRequest request)
    {
        this.request = new ModeVoteRequest { BootId = request.BootId, PollId = request.PollId, UserId = request.UserId, Mode = request.Mode };
    }
    public Task<string?> Completion => completion.Task;
    public void Cancel()
    {
        if (Interlocked.CompareExchange(ref state, 2, 0) == 0) completion.TrySetResult(null);
    }
    internal void Execute(Func<ModeVoteRequest, string> accept)
    {
        if ((Stopwatch.GetTimestamp() - created) / (double)Stopwatch.Frequency >= 2) Cancel();
        if (Interlocked.CompareExchange(ref state, 1, 0) != 0) return;
        try { completion.TrySetResult(accept(request)); }
        catch { completion.TrySetResult(null); }
    }
}
