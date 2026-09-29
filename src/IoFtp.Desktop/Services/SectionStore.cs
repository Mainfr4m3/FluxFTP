using System.IO;
using System.Text.Json;

namespace IoFtp.Desktop.Services;

internal enum SectionValidationMode { Disabled, Warning, Block }

internal sealed record SectionDefinition(
    string Name,
    Dictionary<string, string> SitePaths,
    int Hotkey = 0,
    string AllowPatterns = "",
    string DenyPatterns = "",
    SectionValidationMode ValidationMode = SectionValidationMode.Disabled);

internal sealed class SectionStore
{
    private static readonly object Gate = new();
    private readonly string _path = Path.Combine(AppContext.BaseDirectory, "FluxFTP-sections.json");
    private readonly string _oldPath = Path.Combine(AppContext.BaseDirectory, "ioFTP-sections.json");

    public List<SectionDefinition> Load()
    {
        lock (Gate)
        {
            try
            {
                if (File.Exists(_path)) return JsonSerializer.Deserialize<List<SectionDefinition>>(File.ReadAllText(_path)) ?? [];
                if (File.Exists(_oldPath))
                {
                    var migrated = JsonSerializer.Deserialize<List<SectionDefinition>>(File.ReadAllText(_oldPath)) ?? [];
                    SaveCore(migrated); return migrated;
                }
                var imported = ImportIoFtpdSections();
                if (imported.Count > 0) SaveCore(imported);
                return imported;
            }
            catch { return []; }
        }
    }

    public void Save(IEnumerable<SectionDefinition> sections)
    {
        lock (Gate) SaveCore(sections);
    }

    public List<SectionDefinition> Import(string path)
    {
        if (Path.GetExtension(path).Equals(".xml", StringComparison.OrdinalIgnoreCase))
        {
            var bookmarks = CaptionBookmarkImporter.Import(path, "ioFTPD");
            if (bookmarks.Count == 0)
                throw new InvalidDataException("No CAPTION/REMOTE section entries were found in the XML file.");
            return bookmarks.GroupBy(bookmark => bookmark.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => new SectionDefinition(group.Key,
                    new(StringComparer.OrdinalIgnoreCase) { ["ioFTPD"] = group.Last().Path }))
                .ToList();
        }
        var sections = JsonSerializer.Deserialize<List<SectionDefinition>>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("The selected file does not contain a sections list.");
        if (sections.Any(section => string.IsNullOrWhiteSpace(section.Name)))
            throw new InvalidDataException("Every imported section must have a name.");
        if (sections.Any(section => section.SitePaths is null))
            throw new InvalidDataException("Every imported section must contain SitePaths.");
        return sections;
    }

    private void SaveCore(IEnumerable<SectionDefinition> sections)
    {
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(sections, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, _path, true);
    }

    private static List<SectionDefinition> ImportIoFtpdSections()
    {
        const string iniPath = @"C:\ioFTPD\system\ioFTPD.ini";
        if (!File.Exists(iniPath)) return [];
        var result = new List<SectionDefinition>(); var inSections = false;
        foreach (var raw in File.ReadLines(iniPath))
        {
            var line = raw.Trim();
            if (line.Equals("[Sections]", StringComparison.OrdinalIgnoreCase)) { inSections = true; continue; }
            if (!inSections) continue;
            if (line.StartsWith('[')) break;
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';')) continue;
            var equals = line.IndexOf('='); if (equals <= 0) continue;
            var name = line[..equals].Trim(); if (name.Equals("Default", StringComparison.OrdinalIgnoreCase)) continue;
            var fields = line[(equals + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var path = fields.LastOrDefault(field => field.StartsWith('/'))?.TrimEnd('*').TrimEnd('/');
            if (string.IsNullOrWhiteSpace(path)) continue;
            var existing = result.FindIndex(section => section.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (existing < 0) result.Add(new(name, new(StringComparer.OrdinalIgnoreCase) { ["ioFTPD"] = path }));
            else result[existing].SitePaths["ioFTPD"] = path;
        }
        return result;
    }
}
