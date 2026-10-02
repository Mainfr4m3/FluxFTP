using System.IO;
using System.Text;

namespace IoFtp.Desktop.Services;

internal sealed class VisionaryRuleText
{
    private byte[] _original;
    private readonly Encoding _encoding;
    private readonly byte[] _preamble;
    public string Path { get; }
    public string Text { get; private set; }

    public VisionaryRuleText(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        _original = File.ReadAllBytes(Path);
        if (_original.Length > 4 * 1024 * 1024) throw new InvalidDataException("Rule text exceeds 4 MB.");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _preamble = [];
        if (_original.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }))
        { _encoding = new UTF8Encoding(false, true); _preamble = [0xef, 0xbb, 0xbf]; }
        else if (_original.AsSpan().StartsWith(new byte[] { 0xff, 0xfe }))
        { _encoding = new UnicodeEncoding(false, false, true); _preamble = [0xff, 0xfe]; }
        else if (_original.AsSpan().StartsWith(new byte[] { 0xfe, 0xff }))
        { _encoding = new UnicodeEncoding(true, false, true); _preamble = [0xfe, 0xff]; }
        else
        {
            try { new UTF8Encoding(false, true).GetString(_original); _encoding = new UTF8Encoding(false, true); }
            catch (DecoderFallbackException) { _encoding = Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback); }
        }
        Text = _encoding.GetString(_original, _preamble.Length, _original.Length - _preamble.Length);
    }

    public string Save(string text)
    {
        if (!File.ReadAllBytes(Path).SequenceEqual(_original))
            throw new IOException("The file changed outside FluxFTP. Reload before saving.");
        var bytes = _preamble.Concat(_encoding.GetBytes(text)).ToArray();
        if (bytes.Length > 4 * 1024 * 1024) throw new InvalidDataException("Rule text exceeds 4 MB.");
        if (bytes.SequenceEqual(_original)) return "No changes to save.";
        var backup = Path + ".fluxftp-" + Guid.NewGuid().ToString("N") + ".bak";
        var temporary = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Replace(temporary, Path, backup);
            _original = bytes; Text = text;
            return "Saved. Backup: " + backup;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static string ResolveFolder(string folder)
    {
        foreach (var candidate in new[] { System.IO.Path.Combine(folder, "User_Files", "RULES"),
            System.IO.Path.Combine(folder, "RULES"), folder })
            if (Directory.Exists(candidate) && (System.IO.Path.GetFileName(candidate).Equals("RULES", StringComparison.OrdinalIgnoreCase) ||
                Directory.EnumerateFiles(candidate, "*.txt").Any())) return System.IO.Path.GetFullPath(candidate);
        throw new DirectoryNotFoundException("Select VISIONARY, User_Files, or the RULES folder containing site .txt files.");
    }
}
