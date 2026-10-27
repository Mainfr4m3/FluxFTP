namespace IoFtp.Desktop.Services;

internal sealed class RaceWatchSlots(int capacity) : IDisposable
{
    private readonly object _gate = new();
    private int _capacity = Math.Clamp(capacity, 1, 2048);
    private int _active;
    private bool _disposed;
    private TaskCompletionSource _changed = NewSignal();
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Configure(int capacity)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _capacity = Math.Clamp(capacity, 1, 2048);
            Pulse();
        }
    }
    private void Pulse()
    {
        var previous = _changed;
        _changed = NewSignal();
        previous.TrySetResult();
    }
    public async Task<IDisposable> EnterAsync(CancellationToken token)
    {
        while (true)
        {
            Task changed;
            lock (_gate)
            {
                token.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_active < _capacity) { _active++; return new Lease(this); }
                changed = _changed.Task;
            }
            await changed.WaitAsync(token).ConfigureAwait(false);
        }
    }
    private void Release()
    {
        lock (_gate) { _active--; Pulse(); }
    }
    private sealed class Lease(RaceWatchSlots owner) : IDisposable
    {
        private RaceWatchSlots? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release();
    }
    public void Dispose()
    {
        lock (_gate) { _disposed = true; Pulse(); }
    }
}
