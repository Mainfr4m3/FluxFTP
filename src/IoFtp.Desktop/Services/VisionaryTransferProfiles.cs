using System.IO;
using System.IO.Enumeration;
using System.Text.RegularExpressions;
using IoFtp.Core.Abstractions;

namespace IoFtp.Desktop.Services;

internal sealed class VisionaryPayloadProfile(string name, string directory, string complete, string allow, string skip, bool expressions, int retries)
{
    public string Name { get; } = name;
    public string Directory { get; } = directory;
    public int Attempts { get; } = retries + 1;
    private readonly Regex _complete = Pattern(complete);
    private readonly Regex? _allow = expressions && allow.Length > 0 ? Pattern(allow, allowEmpty: true) : null;
    private readonly Regex? _skip = expressions && skip.Length > 0 ? Pattern(skip, allowEmpty: true) : null;
    public bool Allows(RemoteEntry file) => !file.IsDirectory &&
        (allow.Length == 0 || Match(file.Name, allow, _allow)) && (skip.Length == 0 || !Match(file.Name, skip, _skip));
    public bool Completes(RemoteEntry file) => Allows(file) && file.Size > 0 && _complete.IsMatch(file.Name);
    public bool Pending(RemoteEntry file) => Allows(file) && !(file.Size > 0) && _complete.IsMatch(file.Name);
    private static bool Match(string name, string value, Regex? regex) => regex?.IsMatch(name) ??
        value.Split('|', StringSplitOptions.RemoveEmptyEntries).Any(pattern => FileSystemName.MatchesSimpleExpression(pattern, name, true));
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
    private readonly VisionaryPayloadProfile[] _profiles;
    private VisionaryTransferProfiles(VisionaryPayloadProfile[] profiles) => _profiles = profiles;
    public VisionaryPayloadProfile? ForDirectory(string path) => _profiles.FirstOrDefault(profile =>
        ("/" + path.Trim('/')).EndsWith("/" + profile.Directory, StringComparison.OrdinalIgnoreCase));

    public static VisionaryTransferProfiles Load(string path = "")
    {
        var entries = string.IsNullOrWhiteSpace(path) ? [] : new VisionaryConfiguration(path).Entries;
        string Value(string section, string key, string fallback) => entries.LastOrDefault(entry =>
            entry.Section.Equals(section, StringComparison.OrdinalIgnoreCase) && entry.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value ?? fallback;
        var profiles = new List<VisionaryPayloadProfile>();
        foreach (var name in new[] { "sample", "covers" })
        {
            var detection = Value("detections", name, ".:/" + name);
            if (!detection.StartsWith(".:/", StringComparison.Ordinal) || detection[3..].Split('/').Any(part =>
                string.IsNullOrWhiteSpace(part) || part is "." or ".." || part.Contains('\\') || part.Any(char.IsControl)))
                throw new InvalidDataException($"Unsupported {name} detection '{detection}'. Expected .:/directory.");
            var defaultPattern = name == "sample" ? @"\.(mp4|avi|mpg|mpeg|vob|mkv|wmv)$" : @"\.(jpg|jpeg|gif|bmp|png|webp)$";
            var useRegex = Value(name, "rushopt", "RS_EXPR").Split([' ', '|'], StringSplitOptions.RemoveEmptyEntries)
                .Any(option => option.Equals("RS_EXPR", StringComparison.OrdinalIgnoreCase));
            var allow = Value(name, "fileallow", "");
            var skip = Value(name, "fileskip", "");
            if (!int.TryParse(Value(name, "retrycount", "2"), out var retries) || retries is < 0 or > 100)
                throw new InvalidDataException($"Invalid {name} retrycount.");
            profiles.Add(new(name, detection[3..], Value(name, "completeflag", defaultPattern), allow, skip, useRegex, retries));
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
