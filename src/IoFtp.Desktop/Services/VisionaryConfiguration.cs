using System.IO;
using System.Text;

namespace IoFtp.Desktop.Services;

// Edit data only: retain comments, order, repeated keys and the original encoding.
internal sealed class VisionaryConfiguration
{
    private byte[] _original;
    private readonly Encoding _encoding;
    private readonly byte[] _preamble;
    private readonly string _newline;
    private readonly List<string> _lines;
    public string Path { get; }
    public List<Entry> Entries { get; } = [];
    public bool HasChanges => Entries.Any(entry =>
    {
        var line = _lines[entry.Line];
        var separator = Separator(line, entry.Mapping);
        return line[(separator + 1)..].Trim() != entry.Value || line[..separator].Trim() != entry.Key;
    });

    public sealed class Entry(int line, string section, string key, string value, bool mapping = false)
    {
        internal bool Mapping { get; } = mapping;
        internal int Line { get; } = line;
        public string Section { get; } = section;
        public string Key { get; set; } = key;
        public string Value { get; set; } = value;
    }

    public VisionaryConfiguration(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        _original = File.ReadAllBytes(Path);
        if (_original.Length > 4 * 1024 * 1024) throw new InvalidDataException("Configuration exceeds 4 MB.");
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
        var text = _encoding.GetString(_original, _preamble.Length, _original.Length - _preamble.Length);
        _newline = text.Contains("\r\n") ? "\r\n" : "\n";
        _lines = text.Split(_newline).ToList();
        var section = "";
        for (var i = 0; i < _lines.Count; i++)
        {
            var line = _lines[i].Trim();
            if (section.Equals("mappings", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split(';', 3);
                if (parts.Length == 3 && parts[2].StartsWith('/'))
                { Entries.Add(new(i, section, parts[0] + ";" + parts[1], parts[2], true)); continue; }
            }
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#') || line.StartsWith("//")) continue;
            if (line.StartsWith('[') && line.EndsWith(']')) { section = line[1..^1]; continue; }
            // section.cha keys are regexes and may themselves contain '='.
            var separator = System.IO.Path.GetExtension(Path).Equals(".cha", StringComparison.OrdinalIgnoreCase)
                ? line.LastIndexOf('=') : line.IndexOf('=');
            if (separator <= 0) continue;
            Entries.Add(new(i, section, line[..separator].Trim(), line[(separator + 1)..].Trim()));
        }
        if (Entries.Count == 0) throw new InvalidDataException("No configuration entries found.");
    }

    private int Separator(string line, bool mapping) => mapping
        ? line.IndexOf(';', line.IndexOf(';') + 1)
        : System.IO.Path.GetExtension(Path).Equals(".cha", StringComparison.OrdinalIgnoreCase) ? line.LastIndexOf('=') : line.IndexOf('=');

    public string Save()
    {
        if (!File.ReadAllBytes(Path).SequenceEqual(_original))
            throw new IOException("The file changed outside FluxFTP. Reimport it before saving.");
        var lines = _lines.ToArray();
        foreach (var entry in Entries)
        {
            if (entry.Value.IndexOfAny(['\r', '\n', '\0']) >= 0) throw new InvalidDataException("Values must fit on one line.");
            if (string.IsNullOrWhiteSpace(entry.Key) || entry.Key.IndexOfAny(['\r', '\n', '\0']) >= 0)
                throw new InvalidDataException("A setting / regex must be nonempty and fit on one line.");
            if (entry.Mapping && entry.Key.Count(c => c == ';') != 1)
                throw new InvalidDataException("SLFTP mapping keys must contain source;target (source can be empty).");
            if (!entry.Mapping && !System.IO.Path.GetExtension(Path).Equals(".cha", StringComparison.OrdinalIgnoreCase) && entry.Key.Contains('='))
                throw new InvalidDataException("INI setting names cannot contain '='.");
            var separator = Separator(lines[entry.Line], entry.Mapping);
            if (lines[entry.Line][..separator].Trim() != entry.Key)
                lines[entry.Line] = entry.Key + (entry.Mapping ? ";" : "=") + entry.Value;
            else if (lines[entry.Line][(separator + 1)..].Trim() != entry.Value)
                lines[entry.Line] = lines[entry.Line][..(separator + 1)] + entry.Value;
        }
        var bytes = _preamble.Concat(_encoding.GetBytes(string.Join(_newline, lines))).ToArray();
        if (bytes.SequenceEqual(_original)) return "No changes to save.";
        var backup = Path + ".fluxftp-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".bak";
        var temporary = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Replace(temporary, Path, backup);
            _original = bytes;
            _lines.Clear(); _lines.AddRange(lines);
            return $"Saved. Backup: {backup}. Reload the configuration in VISIONARY to apply it.";
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
