using System.IO;
using System.Text;
using IoFtp.Core.Models;

namespace IoFtp.Desktop.Services;

internal static class VisionarySiteMigration
{
    public static ConnectionProfile Apply(ConnectionProfile profile, IEnumerable<string> paths)
    {
        var options = profile.EffectiveOptions;
        var text = new StringBuilder();
        var affils = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            var name = Path.GetFileName(path);
            var siteFile = name.Equals(profile.Name + ".ini", StringComparison.OrdinalIgnoreCase);
            var global = name.Equals("options.ini", StringComparison.OrdinalIgnoreCase);
            var rush = name.Equals("rushopt.ini", StringComparison.OrdinalIgnoreCase);
            if (!siteFile && !global && !rush) continue;
            var entries = new VisionaryConfiguration(path).Entries;
            if (global) entries = entries.Where(entry => entry.Section.Equals("SKIP", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var group in entries.GroupBy(entry => entry.Section))
            {
                text.AppendLine($"[{(siteFile ? "SITE" : global ? "GLOBAL" : "RUSHOPT")}:{group.Key}]");
                foreach (var entry in group)
                {
                    text.AppendLine(entry.Key + "=" + entry.Value);
                    if (siteFile && entry.Key.Equals("siteaffils", StringComparison.OrdinalIgnoreCase))
                        foreach (var affil in entry.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)) affils.Add(affil);
                }
            }
        }
        return profile with { Options = options with {
            Affils = affils.Count > 0 ? string.Join(' ', affils) : options.Affils,
            VisionaryRules = text.Length > 0 ? text.ToString() : options.VisionaryRules } };
    }
}
