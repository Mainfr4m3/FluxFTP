using IoFtp.Core.Models;

namespace IoFtp.Desktop.Services;

internal static class SectionChainPolicy
{
    internal static bool CanRoute(ConnectionProfile source, ConnectionProfile target) => RouteBlockReason(source, target) is null;

    internal static string? RouteBlockReason(ConnectionProfile source, ConnectionProfile target)
    {
        var a = source.EffectiveOptions; var b = target.EffectiveOptions;
        bool Blocked(string list, string site) => list.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(site, StringComparer.OrdinalIgnoreCase);
        if (source.Id == target.Id || source.Name.Equals(target.Name, StringComparison.OrdinalIgnoreCase)) return "source and destination are the same site";
        if (a.LinkSpeedGbps is not (1 or 10)) return $"{source.Name}: source speed class is not set to 1 or 10 Gbit";
        if (b.LinkSpeedGbps is not (1 or 10)) return $"{target.Name}: destination speed class is not set to 1 or 10 Gbit";
        if (a.LinkSpeedGbps == 1 && b.LinkSpeedGbps == 10) return "1 Gbit source to 10 Gbit destination is blocked";
        if (!a.AllowDownload || a.MaxDownloadSlots <= 0) return $"{source.Name}: downloads are disabled or have zero slots";
        if (!b.AllowUpload || b.MaxUploadSlots <= 0) return $"{target.Name}: uploads are disabled or have zero slots";
        if (a.MaxSlots <= 0 || b.MaxSlots <= 0) return "source or destination has zero total login slots";
        if (Blocked(a.BlockTransfersTo, target.Name)) return $"{source.Name}: destination is in Block transfers to";
        if (Blocked(b.BlockTransfersFrom, source.Name)) return $"{target.Name}: source is in Block transfers from";
        return null;
    }

    internal static ConnectionProfile[] Participants(SectionDefinition section, IEnumerable<ConnectionProfile> profiles) => profiles
        .Where(p => section.TradingSites?.Contains(p.Name, StringComparer.OrdinalIgnoreCase) == true
            && section.SitePaths.Any(path => path.Key.Equals(p.Name, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(path.Value)))
        .ToArray();

    internal static string[][] Plan(SectionDefinition section, IEnumerable<ConnectionProfile> profiles, string[][] incoming)
        => PlanWithDiagnostics(section, profiles, incoming, null);

    internal static string[][] PlanWithDiagnostics(SectionDefinition section, IEnumerable<ConnectionProfile> profiles, string[][] incoming,
        Action<string>? rejected)
    {
        var saved = profiles.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        var participants = Participants(section, saved.Values).ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        if (participants.Count > 64) throw new InvalidOperationException("Section chain mode supports at most 64 trading-active sites.");
        var result = new List<string[]>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? Missing(string name)
        {
            if (!saved.ContainsKey(name)) return $"{name}: site is not saved in FluxFTP";
            if (section.TradingSites?.Contains(name, StringComparer.OrdinalIgnoreCase) != true) return $"{name}: Trading active is off for {section.Name}";
            return $"{name}: section path is missing for {section.Name}";
        }
        void Add(string[] pair, bool report = false)
        {
            var reason = !participants.TryGetValue(pair[0], out var a) ? Missing(pair[0])
                : !participants.TryGetValue(pair[1], out var b) ? Missing(pair[1]) : RouteBlockReason(a, b);
            if (reason is not null)
            {
                if (report) rejected?.Invoke($"{pair[0]} -> {pair[1]}: {reason}");
                return;
            }
            if (seen.Add(pair[0] + "\t" + pair[1])) result.Add(pair);
        }
        // Retain Visionary's pair order and live slot values before adding routes.
        foreach (var pair in incoming) Add(pair, report: true);
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
        => LoadPlanWithDiagnostics(section, incoming, null);

    internal static string[][] LoadPlanWithDiagnostics(string section, string[][] incoming, Action<string>? log)
    {
        var config = new SectionStore().Load().FirstOrDefault(s => s.Name.Equals(section, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Unknown trading section: {section}");
        var result = SectionChainPolicy.PlanWithDiagnostics(config, new ProfileStore().Load(), incoming,
            message => log?.Invoke($"VISIONARY CHAIN FILTER [{section}] {message}"));
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
