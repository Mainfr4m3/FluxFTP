using System.Text.RegularExpressions;

namespace IoFtp.Desktop.Services;

internal sealed record IrcAnnouncement(DateTimeOffset Time, string Site, string Network, string Channel, string Event, string Release, string Section);
internal sealed class IrcCatcher(Func<string, string[]>? sections = null, Action<IrcAnnouncement>? onAnnouncement = null)
{
    private readonly object _gate = new();
    private readonly List<IrcAnnouncement> _recent = [];
    public static readonly string[] Events = ["PRE", "NEWDIR", "COMPLETE", "NUKE", "REQUEST", "ADDPRE"];
    public IReadOnlyList<IrcAnnouncement> Recent(string? site = null)
    { lock (_gate) return _recent.Where(a => site is null || a.Site.Equals(site, StringComparison.OrdinalIgnoreCase)).Take(5).ToArray(); }

    public IReadOnlyList<IrcAnnouncement> Match(IrcRouting routing, string network, string channel, string nick, string message, bool record = false)
    {
        // Strip IRC colors before token matching. Words are ANDed, as in slftp.
        var clean = Regex.Replace(message, "\\x03[0-9]{0,2}(?:,[0-9]{1,2})?|\\x04[0-9a-fA-F]{6}(?:,[0-9a-fA-F]{6})?|[\\x00-\\x1f]", "");
        var words = Regex.Split(clean, @"[^\p{L}\p{N}_.&*-]+").Where(w => w.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = Regex.Matches(clean, @"(?<![A-Za-z0-9_.-])[A-Za-z0-9][A-Za-z0-9._()&+-]{3,}-[A-Za-z0-9][A-Za-z0-9._-]*")
            .Select(m => m.Value.TrimEnd('.', '-', ')')).Where(s => s.Any(char.IsLetter)).Distinct(StringComparer.Ordinal).OrderByDescending(s => s.Length).ToArray();
        if (candidates.Length == 0) return [];
        var release = candidates[0];
        var result = new List<IrcAnnouncement>();
        foreach (var rule in routing.Catches.Where(c => c.Network.Equals(network, StringComparison.OrdinalIgnoreCase) && IrcProtocol.Same(c.Channel, channel) && c.BotNicks.Any(n => IrcProtocol.Same(n, nick))))
        {
            if (!rule.Words.All(words.Contains)) continue;
            var section = rule.Section;
            if (section.Length == 0) section = (sections?.Invoke(rule.Site) ?? []).FirstOrDefault(words.Contains) ?? "";
            var entry = new IrcAnnouncement(DateTimeOffset.UtcNow, rule.Site, network, channel, rule.Event, release, section);
            if (result.Any(a => a.Site == entry.Site && a.Event == entry.Event && a.Release == entry.Release)) continue;
            result.Add(entry);
            if (record) lock (_gate)
            {
                // Main/flood duplicates should produce one recent event.
                if (_recent.Any(a => a.Site.Equals(entry.Site, StringComparison.OrdinalIgnoreCase) && a.Event == entry.Event && a.Release == entry.Release && entry.Time - a.Time < TimeSpan.FromMinutes(2))) continue;
                _recent.Insert(0, entry);
                onAnnouncement?.Invoke(entry);
                if (_recent.Count > 500) _recent.RemoveRange(500, _recent.Count - 500);
            }
        }
        return result;
    }
}
