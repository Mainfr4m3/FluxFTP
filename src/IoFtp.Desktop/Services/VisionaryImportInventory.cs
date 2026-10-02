using System.IO;
using System.Text.Json;

namespace IoFtp.Desktop.Services;

internal sealed record VisionaryImportFile(string Path, int Settings, string Behavior);

internal static class VisionaryImportInventory
{
    public static IReadOnlyList<VisionaryImportFile> ReadFolder(string folder)
    {
        var root = Directory.Exists(System.IO.Path.Combine(folder, "User_Files")) ? System.IO.Path.Combine(folder, "User_Files") : folder;
        var paths = Directory.EnumerateFiles(root).Where(path => System.IO.Path.GetExtension(path).ToLowerInvariant() is ".ini" or ".cha").ToList();
        var sites = System.IO.Path.Combine(root, "inifiles");
        if (Directory.Exists(sites)) paths.AddRange(Directory.EnumerateFiles(sites, "*.ini"));
        var rules = System.IO.Path.Combine(root, "RULES");
        if (Directory.Exists(rules)) paths.AddRange(Directory.EnumerateFiles(rules, "*.ini"));
        return paths.OrderBy(path => path).Select(path => new VisionaryImportFile(System.IO.Path.GetFullPath(path),
            new VisionaryConfiguration(path).Entries.Count,
            "Preserved in full; VISIONARY remains the rule engine. Editable in the VISIONARY panel.")).ToArray();
    }

    public static string IndexPath => System.IO.Path.Combine(AppContext.BaseDirectory, "FluxFTP-visionary-files.json");
    public static string[] Load() => File.Exists(IndexPath) ? JsonSerializer.Deserialize<string[]>(File.ReadAllText(IndexPath)) ?? [] : [];
    public static void Link(IEnumerable<string> paths)
    {
        var merged = Load().Concat(paths.Select(System.IO.Path.GetFullPath)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        File.WriteAllText(IndexPath + ".tmp", JsonSerializer.Serialize(merged));
        File.Move(IndexPath + ".tmp", IndexPath, true);
    }
}
