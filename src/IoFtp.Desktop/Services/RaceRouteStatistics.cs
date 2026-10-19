namespace IoFtp.Desktop.Services;

internal sealed record RaceRouteCount(string Source, string Destination, long Races)
{
    public string Label => $"{Source} → {Destination}: {Races:N0} RACE";
}
internal sealed record RaceDestinationWins(string Site, long Wins, long Results)
{
    public string Label => $"{Site}: {Wins:N0} wins / {Results:N0} confirmed results";
}
internal sealed class RaceRouteStatistics
{
    private readonly object _gate = new();
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _recent = new();
    private readonly Dictionary<string, RaceRouteCount> _routes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _destinations = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _results = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RaceDestinationWins> _wins = new(StringComparer.OrdinalIgnoreCase);
    public void Record(string section, string release, string source, string destination)
    {
        lock (_gate)
        {
            var key = string.Join('\t', section, release, source, destination);
            if (!_seen.Add(key)) return;
            _destinations.Add(destination + "\t" + release);
            _recent.Enqueue(key);
            if (_recent.Count > 100_000) _seen.Remove(_recent.Dequeue());
            var route = source + "\t" + destination;
            _routes[route] = _routes.TryGetValue(route, out var prior) ? prior with { Races = prior.Races + 1 } : new(source, destination, 1);
        }
    }
    public IReadOnlyList<RaceRouteCount> Snapshot()
    {
        lock (_gate) return _routes.Values.OrderByDescending(row => row.Races).ThenBy(row => row.Source).ThenBy(row => row.Destination).ToArray();
    }
    public bool RecordResult(string site, string release, string winner, string expectedUser)
    {
        lock (_gate)
        {
            var key = site + "\t" + release;
            if (string.IsNullOrWhiteSpace(expectedUser) || !_destinations.Contains(key) || !_results.Add(key)) return false;
            var won = winner.Equals(expectedUser.TrimStart('!'), StringComparison.OrdinalIgnoreCase);
            var prior = _wins.GetValueOrDefault(site) ?? new(site, 0, 0);
            _wins[site] = prior with { Wins = prior.Wins + (won ? 1 : 0), Results = prior.Results + 1 };
            return true;
        }
    }
    public IReadOnlyList<RaceDestinationWins> Wins()
    {
        lock (_gate) return _wins.Values.OrderByDescending(row => row.Wins).ThenBy(row => row.Site).ToArray();
    }
}
