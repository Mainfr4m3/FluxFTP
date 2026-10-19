using System.IO;
using System.Text.Json;
using IoFtp.Core.Models;

namespace IoFtp.Desktop.Services;

internal sealed class VisionarySlotCache(string? directory = null)
{
    internal sealed record Slots(int? Downloads, int? Uploads);
    private readonly string _path = Path.Combine(directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluxFTP"), "visionary-slots.json");
    private readonly Dictionary<string, Slots> _values = new(StringComparer.OrdinalIgnoreCase);
    public string? LastError { get; private set; }
    public VisionarySlotCache Load()
    {
        try
        {
            if (File.Exists(_path))
                foreach (var pair in JsonSerializer.Deserialize<Dictionary<string, Slots>>(File.ReadAllText(_path)) ?? new())
                    if (pair.Value is { } slots && Valid(slots.Downloads) && Valid(slots.Uploads)) _values[pair.Key] = slots;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { LastError = ex.Message; }
        return this;
    }
    private static bool Valid(int? value) => value is null or >= 0 and <= 1024;
    public bool Update(string site, int? downloads, int? uploads)
    {
        if (!Valid(downloads) || !Valid(uploads)) throw new ArgumentOutOfRangeException(nameof(downloads));
        if (downloads is null && uploads is null) return false;
        _values.TryGetValue(site, out var old);
        var next = new Slots(downloads ?? old?.Downloads, uploads ?? old?.Uploads);
        if (next == old) return false;
        _values[site] = next;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path + ".tmp", JsonSerializer.Serialize(_values));
            File.Move(_path + ".tmp", _path, true);
            LastError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LastError = ex.Message; }
        return true;
    }
    public Slots? Get(string site) => _values.GetValueOrDefault(site);
    public SiteOptions Apply(ConnectionProfile profile, int auxiliary)
    {
        var options = profile.EffectiveOptions;
        if (Get(profile.Name) is not { } live) return options;
        var downloads = live.Downloads ?? (options.AllowDownload ? options.MaxDownloadSlots : 0);
        var uploads = live.Uploads ?? (options.AllowUpload ? options.MaxUploadSlots : 0);
        return options with { MaxDownloadSlots = downloads, MaxUploadSlots = uploads,
            AllowDownload = downloads > 0, AllowUpload = uploads > 0,
            MaxSlots = Math.Max(options.MaxSlots, downloads + uploads + auxiliary) };
    }
}
