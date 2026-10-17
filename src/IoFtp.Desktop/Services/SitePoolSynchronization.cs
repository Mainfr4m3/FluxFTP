using System.Collections.Concurrent;

namespace IoFtp.Desktop.Services;

// Pool ownership is per site, including when a profile's options change.
// A slow login/NOOP must not hold up unrelated sites.
internal sealed class SitePoolSynchronization
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _gates = new();
    public SemaphoreSlim For(Guid siteId) => _gates.GetOrAdd(siteId, _ => new SemaphoreSlim(1, 1));
}
