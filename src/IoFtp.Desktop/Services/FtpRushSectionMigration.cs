using System.IO;

namespace IoFtp.Desktop.Services;

internal sealed class FtpRushSectionRow
{
    public bool Selected { get; set; }
    public string Section { get; set; } = "";
    public string Site { get; set; } = "";
    public string Path { get; set; } = "";
    public string Note { get; set; } = "";
}

internal static class FtpRushSectionMigration
{
    public static bool IsRemotePath(string path) => path.StartsWith('/') && !path.Contains(':') &&
        !path.Contains('\\') && !path.Any(char.IsControl) && !path.Split('/').Any(part => part is "." or "..");

    public static List<FtpRushSectionRow> Preview(IEnumerable<SiteBookmark> bookmarks)
    {
        return bookmarks.Where(bookmark => !string.IsNullOrWhiteSpace(bookmark.Name))
            .GroupBy(bookmark => (bookmark.SiteName.Trim().ToUpperInvariant(), bookmark.Name.Trim().ToUpperInvariant()))
            .Select(group =>
            {
                var first = group.First();
                var paths = group.Select(bookmark => bookmark.Path.Trim()).Distinct(StringComparer.Ordinal).ToArray();
                var conflict = paths.Length > 1;
                var valid = !conflict && IsRemotePath(paths[0]);
                var site = first.SiteName.Trim();
                return new FtpRushSectionRow
                {
                    Selected = valid && site.Length > 0,
                    Section = first.Name.Trim(), Site = site, Path = conflict ? "" : paths[0],
                    Note = conflict ? "Choose a path: " + string.Join(" | ", paths) :
                        !valid ? "Not an absolute FTP path (local bookmarks are not sections)." :
                        site.Length == 0 ? "Global bookmark: choose the destination site explicitly." : "Ready"
                };
            }).OrderBy(row => row.Site).ThenBy(row => row.Section).ToList();
    }

    public static List<SectionDefinition> Merge(IEnumerable<SectionDefinition> existing,
        IEnumerable<FtpRushSectionRow> selected, IReadOnlyDictionary<string, string> siteNames, bool replace)
    {
        var result = existing.Select(section => section with
        { SitePaths = new Dictionary<string, string>(section.SitePaths, StringComparer.OrdinalIgnoreCase) }).ToList();
        var incoming = new Dictionary<(string Section, string Site), string>();
        foreach (var row in selected.Where(row => row.Selected))
        {
            var sectionName = row.Section.Trim(); var siteName = row.Site.Trim(); var path = row.Path.Trim();
            if (sectionName.Length == 0 || sectionName.Any(char.IsControl) || !IsRemotePath(path))
                throw new InvalidDataException($"Section '{sectionName}' needs a name and an absolute FTP path.");
            if (!siteNames.TryGetValue(siteName, out var resolvedSite))
                throw new InvalidDataException($"Section '{sectionName}': select/import site '{siteName}' or map it to an existing site.");
            var key = (sectionName.ToUpperInvariant(), resolvedSite.ToUpperInvariant());
            if (incoming.TryGetValue(key, out var previous) && previous != path)
                throw new InvalidDataException($"Conflicting paths for {resolvedSite}/{sectionName}.");
            incoming[key] = path;
            var index = result.FindIndex(section => section.Name.Equals(sectionName, StringComparison.OrdinalIgnoreCase));
            if (index < 0) { result.Add(new(sectionName, new(StringComparer.OrdinalIgnoreCase) { [resolvedSite] = path })); continue; }
            if (replace || !result[index].SitePaths.ContainsKey(resolvedSite)) result[index].SitePaths[resolvedSite] = path;
        }
        return result;
    }
}
