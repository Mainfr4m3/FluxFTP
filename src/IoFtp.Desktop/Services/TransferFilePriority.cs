using System.IO.Enumeration;
using IoFtp.Core.Models;
using IoFtp.Desktop.Models;

namespace IoFtp.Desktop.Services;

internal static class TransferFilePriority
{
    public const string DefaultOrder = "SFV\nMain files\nSample\nProof\nNFO";
    private const int CategoryStride = 1_000_000;

    public static string[] Categories(string? order)
    {
        var defaults = DefaultOrder.Split('\n');
        var entries = (order ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return entries.Length == defaults.Length && entries.Distinct(StringComparer.OrdinalIgnoreCase).Count() == defaults.Length &&
            entries.All(entry => defaults.Contains(entry, StringComparer.OrdinalIgnoreCase))
            ? entries.Select(entry => defaults.First(value => value.Equals(entry, StringComparison.OrdinalIgnoreCase))).ToArray()
            : defaults;
    }

    public static string Category(string name, string sourcePath)
    {
        var folders = sourcePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).SkipLast(1).ToArray();
        // The nearest special folder determines the category, even when a
        // Sample/Proof folder contains its own SFV or NFO.
        foreach (var folder in folders.Reverse())
        {
            if (folder.Equals("Sample", StringComparison.OrdinalIgnoreCase)) return "Sample";
            if (folder.Equals("Proof", StringComparison.OrdinalIgnoreCase)) return "Proof";
        }
        if (name.EndsWith(".sfv", StringComparison.OrdinalIgnoreCase)) return "SFV";
        if (name.EndsWith(".nfo", StringComparison.OrdinalIgnoreCase)) return "NFO";
        return "Main files";
    }

    public static int Rank(string name, string sourcePath, GlobalSettings settings, IEnumerable<ConnectionProfile?>? profiles = null)
    {
        var patterns = settings.PriorityPatterns.Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var withinCategory = Array.FindIndex(patterns, pattern => FileSystemName.MatchesSimpleExpression(pattern, name, true));
        if (withinCategory < 0) withinCategory = patterns.Length;
        if (profiles is not null) withinCategory = ImportedTransferSettings.PriorityRank(name, profiles, withinCategory);
        return Array.IndexOf(Categories(settings.TransferCategoryOrder), Category(name, sourcePath)) * CategoryStride
            + Math.Clamp(withinCategory, 0, CategoryStride - 1);
    }
}
