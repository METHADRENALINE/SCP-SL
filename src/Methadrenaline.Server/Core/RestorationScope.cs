using System;
using System.Collections.Generic;

namespace Methadrenaline.Server.Core;

public sealed class RestorationScope : IDisposable
{
    private readonly Stack<Action> actions = new();
    private readonly Action<Exception> report;
    private bool disposed;

    public RestorationScope(Action<Exception> report) => this.report = report;
    public int Count => actions.Count;

    public void Add(Action restore)
    {
        if (disposed) throw new ObjectDisposedException(nameof(RestorationScope));
        actions.Push(restore);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        while (actions.Count > 0)
        {
            try { actions.Pop()(); }
            catch (Exception error) { report(error); }
        }
    }
}
