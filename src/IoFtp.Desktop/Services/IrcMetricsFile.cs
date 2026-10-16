using System.IO;
using System.Text;

namespace IoFtp.Desktop.Services;

// A local heartbeat lets IRC clients read metrics without blocking on IPC or loading a DLL.
internal sealed class IrcMetricsFile(string? directory = null)
{
    private readonly string _path = Path.Combine(directory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluxFTP"), "irc-metrics.txt");
    private readonly string _temporarySuffix = $".{Environment.ProcessId}.tmp";

    public void Publish(string title)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path + _temporarySuffix,
                $"{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}\t{title}\n", new UTF8Encoding(false));
            File.Move(_path + _temporarySuffix, _path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Metrics are optional. A locked/missing file must never interrupt transfers.
        }
    }
}
