using System.IO;
using System.Text.Json;

namespace IoFtp.Desktop.Services;

internal sealed class RaceIdentityStore
{
    private readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluxFTP", "race-identities.json");
    public Dictionary<string, string> Load()
    {
        try { return new(File.Exists(_path) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path)) ?? new() : new(), StringComparer.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(StringComparer.OrdinalIgnoreCase); }
    }
    public void Save(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || parts.Any(value => value.Length is < 1 or > 128 || value.Any(char.IsWhiteSpace) || value.Any(char.IsControl)))
                throw new ArgumentException("Use one SITE=nickname per line, without spaces.");
            values[parts[0]] = parts[1];
        }
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path + ".tmp", JsonSerializer.Serialize(values));
        File.Move(_path + ".tmp", _path, true);
    }
}
