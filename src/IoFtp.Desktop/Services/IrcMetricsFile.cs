using System.IO;
using System.Text;

namespace IoFtp.Desktop.Services;

// A local heartbeat lets IRC clients read metrics without blocking on IPC or loading a DLL.
internal sealed class IrcMetricsFile(string? directory = null, string? locatorDirectory = null)
{
    private readonly string _path = Path.GetFullPath(Path.Combine(directory ?? AppContext.BaseDirectory, "irc-metrics.txt"));
    private readonly string? _locatorPath = locatorDirectory is not null || directory is null
        ? Path.Combine(locatorDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluxFTP"), "irc-metrics.path") : null;
    private readonly string _temporarySuffix = $".{Environment.ProcessId}.tmp";
    private bool _locatorPublished;
    public string MetricsPath => _path;

    public void Publish(string title)
    {
        try
        {
            WriteAtomic(_path, $"{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}\t{title}\n");
            // Only a small discovery pointer lives in AppData; the heartbeat itself
            // stays beside FluxFTP.exe, even when the bridge is in another folder.
            if (_locatorPath is not null && (!_locatorPublished || !File.Exists(_locatorPath)))
            {
                WriteAtomic(_locatorPath, _path + Environment.NewLine);
                _locatorPublished = true;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Metrics are optional. A locked/missing file must never interrupt transfers.
        }
    }
    private void WriteAtomic(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + _temporarySuffix;
        try
        {
            File.WriteAllText(temporary, text, new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }
}
