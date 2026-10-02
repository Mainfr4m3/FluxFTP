using System.IO;

namespace IoFtp.Desktop.Services;

internal static class BridgeBundle
{
    internal static readonly string[] Files =
    [
        "FluxFTP.mrc", "FluxFTP-DLL.mrc", "FluxFTP32.dll", "FluxFTP64.dll", "README.md",
        "visionary/Visionary-FluxFTP.mrc", "visionary/FluxFTPVisionary32.dll", "visionary/FluxFTPVisionary64.dll", "visionary/README.md"
    ];

    public static void Export(string directory)
    {
        var assembly = typeof(BridgeBundle).Assembly;
        var payloads = Files.Select(file =>
        {
            var resource = "FluxFTP.Bridge." + (file.StartsWith("visionary/") ? file.Replace('/', '.') : "irc." + file);
            using var stream = assembly.GetManifestResourceStream(resource) ?? throw new FileNotFoundException("Missing embedded bridge: " + file);
            using var buffer = new MemoryStream(); stream.CopyTo(buffer);
            var path = Path.Combine(directory, file.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path)) throw new IOException(file + " already exists. Select an empty folder.");
            return (Path: path, Bytes: buffer.ToArray());
        }).ToArray();
        foreach (var payload in payloads)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(payload.Path)!);
            using var output = new FileStream(payload.Path, FileMode.CreateNew, FileAccess.Write);
            output.Write(payload.Bytes);
        }
    }
}
