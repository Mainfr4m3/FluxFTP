using IoFtp.Core.Models;

namespace IoFtp.Desktop.Services;

internal static class SectionChainPolicy
{
    internal static bool CanRoute(ConnectionProfile source, ConnectionProfile target)
    {
        var a = source.EffectiveOptions; var b = target.EffectiveOptions;
        bool Blocked(string list, string site) => list.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(site, StringComparer.OrdinalIgnoreCase);
        return source.Id != target.Id && !source.Name.Equals(target.Name, StringComparison.OrdinalIgnoreCase)
            && a.LinkSpeedGbps is 1 or 10 && b.LinkSpeedGbps is 1 or 10
            && !(a.LinkSpeedGbps == 1 && b.LinkSpeedGbps == 10)
            && a.AllowDownload && b.AllowUpload && a.MaxSlots > 0 && b.MaxSlots > 0
            && a.MaxDownloadSlots > 0 && b.MaxUploadSlots > 0
            && !Blocked(a.BlockTransfersTo, target.Name) && !Blocked(b.BlockTransfersFrom, source.Name);
    }

    internal static ConnectionProfile[] Participants(SectionDefinition section, IEnumerable<ConnectionProfile> profiles) => profiles
        .Where(p => section.TradingSites?.Contains(p.Name, StringComparer.OrdinalIgnoreCase) == true
            && section.SitePaths.Any(path => path.Key.Equals(p.Name, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(path.Value)))
        .ToArray();

    internal static string[][] Plan(SectionDefinition section, IEnumerable<ConnectionProfile> profiles, string[][] incoming)
    {
        var participants = Participants(section, profiles).ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        if (participants.Count > 64) throw new InvalidOperationException("Section chain mode supports at most 64 trading-active sites.");
        var result = new List<string[]>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string[] pair)
        {
            if (participants.TryGetValue(pair[0], out var a) && participants.TryGetValue(pair[1], out var b)
                && CanRoute(a, b) && seen.Add(a.Name + "\t" + b.Name)) result.Add(pair);
        }
        // Retain Visionary's pair order and live slot values before adding routes.
        foreach (var pair in incoming) Add(pair);
        foreach (var a in participants.Values.OrderByDescending(p => p.EffectiveOptions.LinkSpeedGbps).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            foreach (var b in participants.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)) Add([a.Name, b.Name]);
        if (result.Count > 4096) throw new InvalidOperationException("Section chain plan exceeds 4096 routes; reduce trading-active participants.");
        return result.ToArray();
    }

}

internal static class SectionChainRuntime
{
    private static readonly object Gate = new();
    private static long _refreshAfter;
    private static Dictionary<string, ConnectionProfile[]> _participants = new(StringComparer.OrdinalIgnoreCase);
    internal static string[][] LoadPlan(string section, string[][] incoming)
    {
        var config = new SectionStore().Load().FirstOrDefault(s => s.Name.Equals(section, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Unknown trading section: {section}");
        var result = SectionChainPolicy.Plan(config, new ProfileStore().Load(), incoming);
        if (result.Length == 0) throw new InvalidOperationException($"No permitted section chains for {section}. Set Trading active and manual 1/10 Gbit classes.");
        return result;
    }

    internal static bool LoadAllows(string section, string source, string target)
    {
        lock (Gate)
        {
            if (Environment.TickCount64 >= _refreshAfter)
            {
                var all = new ProfileStore().Load();
                _participants = new SectionStore().Load().ToDictionary(s => s.Name,
                    s => SectionChainPolicy.Participants(s, all), StringComparer.OrdinalIgnoreCase);
                _refreshAfter = Environment.TickCount64 + 1000;
            }
            if (!_participants.TryGetValue(section, out var profiles)) return false;
            var a = profiles.FirstOrDefault(p => p.Name.Equals(source, StringComparison.OrdinalIgnoreCase));
            var b = profiles.FirstOrDefault(p => p.Name.Equals(target, StringComparison.OrdinalIgnoreCase));
            return a is not null && b is not null && SectionChainPolicy.CanRoute(a, b);
        }
    }
}
