using System.IO;
using System.IO.Enumeration;
using System.Text.RegularExpressions;
using IoFtp.Core.Abstractions;

namespace IoFtp.Desktop.Services;

internal sealed class VisionaryPayloadProfile(string name, string directory, string complete, string allow, string skip, bool expressions, int retries,
    bool markerBased = false, string folderAllow = "", string folderSkip = "", string detection = "")
{
    public string Name { get; } = name;
    public string FileFilterDetail => $"profile={Name}; allow={allow}; skip={skip}; regex={expressions}";
    public string FolderFilterDetail => $"profile={Name}; allow={folderAllow}; skip={folderSkip}; regex={expressions}";
    public string Directory { get; } = directory;
    public int Attempts { get; } = retries + 1;
    public bool MarkerBased { get; } = markerBased;
    public Regex? Detection { get; } = detection.Length == 0 ? null : Pattern(detection);
    private readonly Regex _complete = Pattern(complete);
    private readonly Regex? _allow = Filter(allow, expressions);
    private readonly Regex? _skip = Filter(skip, expressions);
    private readonly Regex? _folderAllow = Filter(folderAllow, expressions);
    private readonly Regex? _folderSkip = Filter(folderSkip, expressions);
    public bool AllowsFolder(string name) => (_folderAllow is null || _folderAllow.IsMatch(name)) && (_folderSkip is null || !_folderSkip.IsMatch(name));
    public bool MatchesComplete(string name) => _complete.IsMatch(name);
    public bool Allows(RemoteEntry file) => !file.IsDirectory &&
        (allow.Length == 0 || Match(file.Name, allow, _allow)) && (skip.Length == 0 || !Match(file.Name, skip, _skip));
    public bool Completes(RemoteEntry file) => Allows(file) && file.Size > 0 && _complete.IsMatch(file.Name);
    public bool Pending(RemoteEntry file) => Allows(file) && !(file.Size > 0) && _complete.IsMatch(file.Name);
    private static bool Match(string name, string value, Regex? regex) => regex?.IsMatch(name) ?? true;
    private static Regex? Filter(string value, bool expressions)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var parts = value.Split(expressions ? [';'] : [';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string Glob(string text) => "^" + Regex.Escape(text).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
        // Rush exports can mix RS_EXPR with explicit wildcard lists; do not compile '*.jpg' as regex.
        return Pattern(string.Join("|", parts.Select(part => !expressions || part.StartsWith('*') || part.StartsWith('?') ? Glob(part) : "(?:" + part + ")")), allowEmpty: true);
    }
    private static Regex Pattern(string value, bool allowEmpty = false)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException("A sample/covers completeflag is required.");
        var regex = new Regex(value, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!allowEmpty && regex.IsMatch("")) throw new InvalidDataException("Sample/covers completeflag must not match empty text.");
        return regex;
    }
}

internal sealed class VisionaryTransferProfiles
{
    internal sealed record SavedEntry(string Section, string Key, string Value);
    public static SavedEntry[] Import(string path) => new VisionaryConfiguration(path).Entries
        .Select(entry => new SavedEntry(entry.Section, entry.Key, entry.Value)).ToArray();
    private readonly VisionaryPayloadProfile[] _profiles;
    private VisionaryTransferProfiles(VisionaryPayloadProfile[] profiles) => _profiles = profiles;
    public VisionaryPayloadProfile? ForDirectory(string path) => _profiles.FirstOrDefault(profile => profile.Directory.Length > 0 &&
        ("/" + path.Trim('/')).EndsWith("/" + profile.Directory, StringComparison.OrdinalIgnoreCase)) ??
        _profiles.FirstOrDefault(profile => profile.Detection?.IsMatch(path) == true) ??
        _profiles.FirstOrDefault(profile => profile.Name == "normal");

    public static VisionaryTransferProfiles Load(string path = "", SavedEntry[]? savedEntries = null)
    {
        var entries = savedEntries ?? (string.IsNullOrWhiteSpace(path) ? [] : Import(path));
        string Value(string section, string key, string fallback) => entries.LastOrDefault(entry =>
            entry.Section.Equals(section, StringComparison.OrdinalIgnoreCase) && entry.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value ?? fallback;
        var profiles = new List<VisionaryPayloadProfile>();
        foreach (var name in new[] { "sample", "covers", "proof", "subs", "multicd", "FLAC", "MVID", "MP3", "normal" })
        {
            if (name is not ("sample" or "covers") && !entries.Any(entry => entry.Section.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
            var isDirectoryProfile = name is "sample" or "covers" or "proof" or "subs";
            var detection = Value("detections", name, ".:/" + name);
            if (isDirectoryProfile && (!detection.StartsWith(".:/", StringComparison.Ordinal) || detection[3..].Split('/').Any(part =>
                string.IsNullOrWhiteSpace(part) || part is "." or ".." || part.Contains('\\') || part.Any(char.IsControl)))
                ) throw new InvalidDataException($"Unsupported {name} detection '{detection}'. Expected .:/directory.");
            var payload = name is "sample" or "covers" or "proof";
            var defaultPattern = !payload ? VisionaryRaceOptions.DefaultCompleteFlag : name == "sample" ? @"\.(mp4|avi|mpg|mpeg|vob|mkv|wmv)$" : @"\.(jpg|jpeg|gif|bmp|png|webp)$";
            var useRegex = Value(name, "rushopt", "RS_EXPR").Split([' ', '|'], StringSplitOptions.RemoveEmptyEntries)
                .Any(option => option.Equals("RS_EXPR", StringComparison.OrdinalIgnoreCase));
            var allow = Value(name, "fileallow", "");
            var skip = Value(name, "fileskip", "");
            if (!int.TryParse(Value(name, "retrycount", "2"), out var retries) || retries is < 0 or > 100)
                throw new InvalidDataException($"Invalid {name} retrycount.");
            profiles.Add(new(name, isDirectoryProfile ? detection[3..] : "", Value(name, "completeflag", defaultPattern), allow, skip, useRegex, retries,
                markerBased: !payload, Value(name, "folderallow", ""), Value(name, "folderskip", ""),
                !isDirectoryProfile && name != "normal" ? Value("detections", name, "") : ""));
        }
        return new(profiles.ToArray());
    }

    public static string ReleasePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 1024 || value.Contains('\\') || value.Any(char.IsWhiteSpace) || value.Any(char.IsControl) ||
            value.Split('/').Any(part => part.Length == 0 || part is "." or ".."))
            throw new ArgumentException("Expected Release or Release/subdirectory without traversal or empty path segments.");
        return value;
    }
}
