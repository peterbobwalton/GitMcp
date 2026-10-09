namespace GitMcp.Server.Git;

/// <summary>
/// Async reader/writer lock: any number of readers, or one writer. Waiting writers are preferred so a
/// stream of reads cannot starve a commit.
/// </summary>
public sealed class AsyncReaderWriterLock
{
    private readonly object _gate = new();
    private readonly LinkedList<TaskCompletionSource<IDisposable>> _writers = new();
    private readonly LinkedList<TaskCompletionSource<IDisposable>> _readers = new();
    private int _activeReaders;
    private bool _writerActive;

    public int ActiveReaders { get { lock (_gate) { return _activeReaders; } } }
    public bool WriterActive { get { lock (_gate) { return _writerActive; } } }

    public Task<IDisposable> ReadAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_writerActive && _writers.Count == 0)
            {
                _activeReaders++;
                return Task.FromResult<IDisposable>(new Releaser(this, write: false));
            }
            return Enqueue(_readers, ct);
        }
    }

    public Task<IDisposable> WriteAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_writerActive && _activeReaders == 0)
            {
                _writerActive = true;
                return Task.FromResult<IDisposable>(new Releaser(this, write: true));
            }
            return Enqueue(_writers, ct);
        }
    }

    /// <summary>Caller holds _gate.</summary>
    private Task<IDisposable> Enqueue(LinkedList<TaskCompletionSource<IDisposable>> queue, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<IDisposable>(TaskCreationOptions.RunContinuationsAsynchronously);
        var node = queue.AddLast(tcs);
        if (ct.CanBeCanceled)
        {
            var registration = ct.Register(() =>
            {
                lock (_gate)
                {
                    if (node.List is null)
                    {
                        return; // already granted
                    }
                    queue.Remove(node);
                    tcs.TrySetCanceled(ct);
                    Pump(); // a cancelled writer may have been holding readers back
                }
            });
            tcs.Task.ContinueWith(_ => registration.Dispose(), TaskScheduler.Default);
        }
        return tcs.Task;
    }

    private void Release(bool write)
    {
        lock (_gate)
        {
            if (write)
            {
                _writerActive = false;
            }
            else
            {
                _activeReaders--;
            }
            Pump();
        }
    }

    /// <summary>Grant whatever can run now. Caller holds _gate.</summary>
    private void Pump()
    {
        if (_writerActive)
        {
            return;
        }
        if (_writers.Count > 0)
        {
            if (_activeReaders == 0)
            {
                var w = _writers.First!.Value;
                _writers.RemoveFirst();
                _writerActive = true;
                w.TrySetResult(new Releaser(this, write: true));
            }
            return;
        }
        while (_readers.Count > 0)
        {
            var r = _readers.First!.Value;
            _readers.RemoveFirst();
            _activeReaders++;
            r.TrySetResult(new Releaser(this, write: false));
        }
    }

    private sealed class Releaser(AsyncReaderWriterLock owner, bool write) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.Release(write);
            }
        }
    }
}
