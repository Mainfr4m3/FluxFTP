using System.Globalization;
using System.IO;
using System.Text;

namespace IoFtp.Desktop.Services;

internal sealed class RaceLogStore(string? directory = null, Func<DateTimeOffset>? clock = null)
{
    private readonly object _gate = new();
    private readonly string _directory = directory ?? AppContext.BaseDirectory;
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    public string ActivePath => Path.Combine(_directory, "FluxFTP-RACE.log");
    public string ArchiveDirectory => Path.Combine(_directory, "logs");
    public string? LastError { get; private set; }
    private const string Header = "# FluxFTP RACE log started UTC: ";
    public const long MaxBytes = 100_000_000;
    public void Write(string category, string message) => TryWrite(() =>
    {
        var now = _clock();
        Rotate(now);
        var line = $"{now:O} [{Clean(category)}] {Clean(message)}{Environment.NewLine}";
        if (new FileInfo(ActivePath).Length + Encoding.UTF8.GetByteCount(line) > MaxBytes)
            File.WriteAllText(ActivePath, Header + now.ToString("O", CultureInfo.InvariantCulture) + Environment.NewLine, new UTF8Encoding(false));
        File.AppendAllText(ActivePath, line, new UTF8Encoding(false));
    });
    public void Maintain() => TryWrite(() => Rotate(_clock()));
    private void TryWrite(Action action)
    {
        lock (_gate)
        {
            try { action(); LastError = null; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { LastError = "RACE log could not be written. Check free disk space and write access to the FluxFTP folder."; }
        }
    }
    private void Rotate(DateTimeOffset now)
    {
        Directory.CreateDirectory(_directory);
        if (File.Exists(ActivePath))
        {
            if (new FileInfo(ActivePath).Length >= MaxBytes)
            {
                File.WriteAllText(ActivePath, Header + now.ToString("O", CultureInfo.InvariantCulture) + Environment.NewLine, new UTF8Encoding(false));
                return;
            }
            DateTimeOffset started;
            using (var reader = new StreamReader(ActivePath))
            {
                var line = reader.ReadLine() ?? "";
                if (!line.StartsWith(Header, StringComparison.Ordinal) || !DateTimeOffset.TryParse(line[Header.Length..], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out started))
                    started = File.GetLastWriteTimeUtc(ActivePath);
            }
            if (now - started < TimeSpan.FromHours(24)) return;
            Directory.CreateDirectory(ArchiveDirectory);
            var archive = Path.Combine(ArchiveDirectory, $"RACE-{started:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log");
            File.Move(ActivePath, archive); // Never overwrite an archive.
        }
        File.WriteAllText(ActivePath, Header + now.ToString("O", CultureInfo.InvariantCulture) + Environment.NewLine, new UTF8Encoding(false));
    }
    private static string Clean(string text) => new(text.Where(c => !char.IsControl(c)).ToArray());
    public async Task ExportAsync(string path, string destination)
    {
        if (Path.GetFullPath(path).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Choose a different destination from the live log.");
        await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, true);
        var remaining = source.Length;
        await using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true);
        var buffer = new byte[65536];
        while (remaining > 0)
        {
            var count = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)));
            if (count == 0) break;
            await target.WriteAsync(buffer.AsMemory(0, count)); remaining -= count;
        }
    }
    public string ReadTail(string path)
    {
        lock (_gate)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var truncated = stream.Length > 512 * 1024;
            if (truncated) stream.Seek(-512 * 1024, SeekOrigin.End);
            using var reader = new StreamReader(stream);
            if (truncated) reader.ReadLine();
            return (truncated ? "[Showing latest 512 KB; full history is saved in the file.]\n" : "") + reader.ReadToEnd();
        }
    }
}
