using System.IO.Enumeration;
using IoFtp.Core.Models;

namespace IoFtp.Desktop.Services;

internal static class ImportedTransferSettings
{
    public static int PriorityRank(string name, IEnumerable<ConnectionProfile?> profiles, int fallback)
    {
        var patterns = profiles.Where(profile => profile is not null)
            .SelectMany(profile => profile!.EffectiveOptions.ImportedPriorityRules.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            .Select(line => line.Trim()).Where(line => line.StartsWith('1')).Select(line => line[1..]).ToArray();
        for (var i = 0; i < patterns.Length; i++)
            if (patterns[i].Length > 0 && FileSystemName.MatchesSimpleExpression(patterns[i], name, true)) return i;
        return patterns.Length + fallback;
    }
}
