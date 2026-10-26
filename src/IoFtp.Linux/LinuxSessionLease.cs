using IoFtp.Core.Models;
namespace IoFtp.Linux;
// Shared by browser sessions, API probes and transfer workers; these are FTP login slots, not data sockets.
internal sealed class LinuxSessionLease : IDisposable
{
    private static readonly object Gate = new();
    private static readonly Dictionary<Guid, int> Used = [];
    private readonly Guid[] _ids;
    private bool _disposed;
    private LinuxSessionLease(Guid[] ids) => _ids = ids;
    public static async Task<LinuxSessionLease> Acquire(IEnumerable<ConnectionProfile?> profiles, CancellationToken token)
    {
        var requested = profiles.OfType<ConnectionProfile>().GroupBy(profile => profile.Id).Select(group => (Site: group.First(), Count: group.Count())).ToArray();
        if (requested.Any(item => item.Site.EffectiveOptions.MaxSlots < item.Count)) throw new InvalidOperationException("Site login limit is too low for this operation.");
        while (true)
        {
            token.ThrowIfCancellationRequested();
            lock (Gate)
                if (requested.All(item => Used.GetValueOrDefault(item.Site.Id) + item.Count <= item.Site.EffectiveOptions.MaxSlots))
                {
                    foreach (var item in requested) Used[item.Site.Id] = Used.GetValueOrDefault(item.Site.Id) + item.Count;
                    return new(requested.SelectMany(item => Enumerable.Repeat(item.Site.Id, item.Count)).ToArray());
                }
            await Task.Delay(100, token);
        }
    }
    public void Dispose()
    {
        lock (Gate)
        {
            if (_disposed) return; _disposed = true;
            foreach (var id in _ids) { var count = Used[id] - 1; if (count == 0) Used.Remove(id); else Used[id] = count; }
        }
    }
}
