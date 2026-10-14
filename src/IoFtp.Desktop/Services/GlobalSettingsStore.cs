using System.IO;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using IoFtp.Desktop.Models;

namespace IoFtp.Desktop.Services;

internal sealed class GlobalSettingsStore
{
    private readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluxFTP", "settings.json");
    private readonly string _oldPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ioFTP", "settings.json");
    internal GlobalSettingsStore(string? directory = null)
    {
        if (directory is null) return;
        _path = Path.Combine(directory, "settings.json");
        _oldPath = Path.Combine(directory, "old-settings.json");
    }
    public GlobalSettings Load()
    {
        try
        {
            if (File.Exists(_path)) return AddMetadataSkipDefaults(Unprotect(JsonSerializer.Deserialize<GlobalSettings>(File.ReadAllText(_path)) ?? new()));
            if (!File.Exists(_oldPath)) return AddMetadataSkipDefaults(new());
            var migrated = AddMetadataSkipDefaults(Unprotect(JsonSerializer.Deserialize<GlobalSettings>(File.ReadAllText(_oldPath)) ?? new())); Save(migrated); return migrated;
        }
        catch { return AddMetadataSkipDefaults(new()); }
    }

    private static GlobalSettings AddMetadataSkipDefaults(GlobalSettings settings)
    {
        if (!settings.ResumeOverwriteDefaultsApplied) settings = settings with { OverwriteIfResumeFails = true, ResumeOverwriteDefaultsApplied = true };
        if (settings.MetadataSkipDefaultsApplied && settings.VideoMetadataSkipDefaultsApplied) return settings;
        var patterns = settings.SkipPatterns.Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var defaults = new List<string>();
        if (!settings.MetadataSkipDefaultsApplied) defaults.AddRange([".ioFTPD.verified", "[ioNiNJA]-(*F-COMPLETE)-[ioNiNJA]"]);
        if (!settings.VideoMetadataSkipDefaultsApplied) defaults.Add("[ioNiNJA]-(V_*)-[ioNiNJA]");
        var missing = defaults.Where(pattern => !patterns.Contains(pattern, StringComparer.OrdinalIgnoreCase)).ToArray();
        return settings with { SkipPatterns = string.Join("\n", new[] { settings.SkipPatterns.TrimEnd() }.Concat(missing).Where(value => value.Length > 0)), MetadataSkipDefaultsApplied = true, VideoMetadataSkipDefaultsApplied = true };
    }
    public void Save(GlobalSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!); var temporary = _path + ".tmp";
        var protectedSettings = settings with { ResumeOverwriteDefaultsApplied = true, ApiPassword = Protect(settings.ApiPassword), ProxyPassword = Protect(settings.ProxyPassword), Irc = settings.Irc is { } irc ? irc with { Password = Protect(irc.Password) } : null };
        File.WriteAllText(temporary, JsonSerializer.Serialize(protectedSettings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, _path, true);
    }

    private static string Protect(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return "dpapi:" + Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
    }
    private static string UnprotectValue(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (!value.StartsWith("dpapi:", StringComparison.OrdinalIgnoreCase)) return value;
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value[6..]), null, DataProtectionScope.CurrentUser)); } catch { return ""; }
    }
    private static GlobalSettings Unprotect(GlobalSettings settings) => settings with { ApiPassword = UnprotectValue(settings.ApiPassword), ProxyPassword = UnprotectValue(settings.ProxyPassword), Irc = settings.Irc is { } irc ? irc with { Password = UnprotectValue(irc.Password) } : null };
}
