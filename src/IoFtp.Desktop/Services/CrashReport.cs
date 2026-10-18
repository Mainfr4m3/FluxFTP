using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace IoFtp.Desktop.Services;

internal static class CrashReport
{
    private static readonly object Gate = new();

    // Best effort only: reporting must never replace the original fatal exception.
    // The application still terminates normally for an unhandled exception.
    internal static void Write(string source, Exception? exception)
    {
        try
        {
            lock (Gate)
            {
                var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluxFTP");
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "crash.log");
                if (File.Exists(path) && new FileInfo(path).Length >= 1024 * 1024)
                    File.Move(path, path + ".previous", overwrite: true);
                File.AppendAllText(path,
                    $"{DateTimeOffset.Now:O} {source}; FluxFTP {Assembly.GetExecutingAssembly().GetName().Version}; {RuntimeInformation.FrameworkDescription}; {RuntimeInformation.OSDescription}{Environment.NewLine}" +
                    $"{exception?.ToString() ?? "Unhandled non-Exception object"}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch { }
    }
}
