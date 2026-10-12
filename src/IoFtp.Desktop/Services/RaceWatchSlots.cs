namespace IoFtp.Desktop.Services;

internal sealed class RaceWatchSlots(int capacity) : IDisposable
{
    private readonly SemaphoreSlim _slots = new(capacity, capacity);
    public async Task<IDisposable> EnterAsync(CancellationToken token)
    {
        await _slots.WaitAsync(token).ConfigureAwait(false);
        return new Lease(_slots);
    }
    private sealed class Lease(SemaphoreSlim slots) : IDisposable
    {
        private SemaphoreSlim? _slots = slots;
        public void Dispose() => Interlocked.Exchange(ref _slots, null)?.Release();
    }
    public void Dispose() => _slots.Dispose();
}
