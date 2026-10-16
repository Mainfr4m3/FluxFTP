using System.IO;
using System.Text.Json;

namespace IoFtp.Desktop.Services;

internal sealed record TotalStatistics(DateTimeOffset StartedAt, long Uploaded = 0, long Downloaded = 0,
    long Fxp = 0, long Local = 0, long Completed = 0, long PeakSpeedBytesPerSecond = 0);

internal sealed class TotalStatisticsStore
{
    private readonly object _gate = new();
    private readonly string _path;
    private TotalStatistics _totals;
    private readonly bool _blocked;
    public string? LastError { get; private set; }
    public TotalStatisticsStore(string? directory = null)
    {
        _path = Path.Combine(directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluxFTP"), "total-statistics.json");
        try { _totals = Load(_path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            try
            {
                if (!File.Exists(_path + ".bak")) throw new IOException("No statistics backup available.");
                _totals = Load(_path + ".bak");
                LastError = "Total statistics recovered from backup; the latest update may be missing.";
            }
            catch
            {
                _totals = new(DateTimeOffset.UtcNow);
                _blocked = true;
                LastError = "Cannot read saved total statistics. Existing files have been preserved: " + exception.Message;
            }
        }
    }

    private static TotalStatistics Load(string path) => !File.Exists(path) ? new(DateTimeOffset.UtcNow)
        : JsonSerializer.Deserialize<TotalStatistics>(File.ReadAllText(path)) ?? throw new JsonException("Empty statistics file.");
    public TotalStatistics Snapshot() { lock (_gate) return _totals; }

    public void RecordSpeed(long bytesPerSecond)
    {
        lock (_gate)
        {
            if (bytesPerSecond > _totals.PeakSpeedBytesPerSecond)
                _totals = _totals with { PeakSpeedBytesPerSecond = bytesPerSecond };
            else if (LastError is null) return;
            SaveLocked();
        }
    }

    public void Complete(string kind, long newBytes)
    {
        lock (_gate)
        {
            var bytes = Math.Max(0, newBytes);
            _totals = kind switch
            {
                "Upload" => _totals with { Uploaded = checked(_totals.Uploaded + bytes) },
                "Download" => _totals with { Downloaded = checked(_totals.Downloaded + bytes) },
                "FXP" => _totals with { Fxp = checked(_totals.Fxp + bytes) },
                "Local" => _totals with { Local = checked(_totals.Local + bytes) },
                _ => throw new ArgumentException("Unknown transfer kind", nameof(kind))
            };
            _totals = _totals with { Completed = checked(_totals.Completed + 1) };
            SaveLocked();
        }
    }

    private void SaveLocked()
    {
        if (_blocked) return;
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(_totals, new JsonSerializerOptions { WriteIndented = true }));
            if (File.Exists(_path)) File.Replace(temporary, _path, _path + ".bak");
            else File.Move(temporary, _path);
            LastError = null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { LastError = "Total statistics could not be saved; current totals remain in memory: " + exception.Message; }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
