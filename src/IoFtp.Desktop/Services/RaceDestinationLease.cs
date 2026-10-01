namespace IoFtp.Desktop.Services;

internal static class RaceDestinationLease
{
    private static readonly Dictionary<string, object> Owners = new(StringComparer.OrdinalIgnoreCase);
    public static void Acquire(string site, string section, string release, object owner)
    {
        var key = site + "\t" + section + "\t" + release.Trim('/');
        lock (Owners)
        {
            foreach (var pair in Owners)
                if (!ReferenceEquals(pair.Value, owner) && (key.Equals(pair.Key, StringComparison.OrdinalIgnoreCase) ||
                    key.StartsWith(pair.Key + "/", StringComparison.OrdinalIgnoreCase) || pair.Key.StartsWith(key + "/", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Another race already owns this destination release.");
            Owners[key] = owner;
        }
    }
    public static void Release(object owner)
    {
        lock (Owners) foreach (var key in Owners.Where(pair => ReferenceEquals(pair.Value, owner)).Select(pair => pair.Key).ToArray()) Owners.Remove(key);
    }
}
