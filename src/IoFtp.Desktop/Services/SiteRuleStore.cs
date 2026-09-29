using System.IO;
using System.IO.Enumeration;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace IoFtp.Desktop.Services;

internal sealed record SiteRule(string Field = "release", string Match = "glob", string[]? Values = null,
    bool Negate = false, string Action = "drop", string Reason = "");
internal sealed record SiteRuleSection(string Name, string Path, SiteRule[] Rules, string DefaultAction = "allow",
    bool RequiresMetadata = false);
internal sealed record SiteRuleFile(int Version, string Site, SiteRule[] Rules, SiteRuleSection[] Sections);
internal sealed record SiteRuleResult(bool Accepted, string Message);

internal sealed class SiteRuleStore(string? directory = null)
{
    public static string DefaultDirectory => System.IO.Path.Combine(AppContext.BaseDirectory, "Rules", "_site");
    private readonly string _directory = directory ?? DefaultDirectory;
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public IReadOnlyList<SiteRuleFile> Load()
    {
        if (!Directory.Exists(_directory)) return [];
        var result = new List<SiteRuleFile>();
        foreach (var path in Directory.EnumerateFiles(_directory, "*.rules.json").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (result.Count >= 128 || new FileInfo(path).Length > 256 * 1024) throw new InvalidDataException("Rule file limit exceeded.");
                var file = JsonSerializer.Deserialize<SiteRuleFile>(File.ReadAllText(path), Json) ?? throw new InvalidDataException("Empty rule file.");
                Validate(file);
                if (result.Any(f => f.Site.Equals(file.Site, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException($"Duplicate site '{file.Site}'.");
                result.Add(file);
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException or IOException or InvalidDataException)
            { throw new InvalidDataException($"Rules file {System.IO.Path.GetFileName(path)}: {ex.Message}", ex); }
        }
        return result;
    }

    public string? ResolvePath(string site, string section) => Find(site)?.Sections.FirstOrDefault(s => s.Name.Equals(section, StringComparison.OrdinalIgnoreCase))?.Path;
    public void Save(SiteRuleFile file)
    {
        Validate(file);
        var loaded = Load(); // Never overwrite an invalid or ambiguous configuration.
        Directory.CreateDirectory(_directory);
        var existing = Directory.EnumerateFiles(_directory, "*.rules.json").FirstOrDefault(path =>
            JsonSerializer.Deserialize<SiteRuleFile>(File.ReadAllText(path), Json)?.Site.Equals(file.Site, StringComparison.OrdinalIgnoreCase) == true);
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(file.Site.ToUpperInvariant())))[..24];
        var target = existing ?? System.IO.Path.Combine(_directory, $"site-{key}.rules.json");
        if (existing is null && loaded.Count >= 128) throw new InvalidDataException("Too many rule files.");
        var json = JsonSerializer.Serialize(file, new JsonSerializerOptions(Json) { WriteIndented = true });
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 256 * 1024) throw new InvalidDataException("Rule file is too large.");
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, json);
            File.Move(temporary, target, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private SiteRuleFile? Find(string site) => Load().SingleOrDefault(f => f.Site.Equals(site, StringComparison.OrdinalIgnoreCase));

    public SiteRuleResult Evaluate(string site, string section, string release)
    {
        try
        {
            var file = Find(site);
            if (file is null) return new(true, "No site rule file.");
            var selected = file.Sections.FirstOrDefault(s => s.Name.Equals(section, StringComparison.OrdinalIgnoreCase));
            if (selected is null) return new(false, $"{site}: section '{section}' is not defined in its rules file.");
            return Evaluate(file, selected, release);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or RegexMatchTimeoutException)
        { return new(false, ex.Message); }
    }

    public SiteRuleResult EvaluateDestination(string site, string destination, string? section = null, string? release = null)
    {
        try
        {
            var file = Find(site);
            if (file is null) return new(true, "No site rule file.");
            destination = Normalize(destination);
            var candidates = file.Sections.Where(s => destination.StartsWith(Root(s.Path), StringComparison.Ordinal)).ToArray();
            if (section is not null)
            {
                candidates = candidates.Where(s => s.Name.Equals(section, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (candidates.Length == 0) return new(false, $"{site}: destination no longer matches section '{section}'.");
            }
            else if (candidates.Length > 0)
            {
                var length = candidates.Max(s => Root(s.Path).Length);
                candidates = candidates.Where(s => Root(s.Path).Length == length).ToArray();
            }
            if (candidates.Length == 0) return new(false, $"{site}: destination is outside the configured rule sections.");
            foreach (var candidate in candidates)
            {
                var name = destination[Root(candidate.Path).Length..].Split('/')[0];
                if (release is not null && !name.Equals(release, StringComparison.Ordinal))
                    return new(false, $"{site}: destination does not match the queued release.");
                var result = Evaluate(file, candidate, name);
                if (!result.Accepted) return result;
            }
            return new(true, $"{site}: destination accepted by site rules.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or RegexMatchTimeoutException or ArgumentException)
        { return new(false, ex.Message); }
    }

    private static SiteRuleResult Evaluate(SiteRuleFile file, SiteRuleSection section, string release)
    {
        if (string.IsNullOrWhiteSpace(release) || release is "." or ".." || release.Contains('/') || release.Contains('\\') || release.Any(char.IsControl))
            return new(false, "Invalid release name.");
        var global = FirstMatch(file.Rules, release);
        if (global?.Action.Equals("drop", StringComparison.OrdinalIgnoreCase) == true) return Decision(file.Site, section.Name, global);
        if (section.RequiresMetadata) return new(false, $"{file.Site}/{section.Name}: IMDb/TV/music metadata is required but lookup is not available in FluxFTP yet.");
        var match = FirstMatch(section.Rules, release);
        return match is not null ? Decision(file.Site, section.Name, match) : new(section.DefaultAction.Equals("allow", StringComparison.OrdinalIgnoreCase),
            $"{file.Site}/{section.Name}: default {section.DefaultAction}.");
    }

    private static SiteRuleResult Decision(string site, string section, SiteRule rule) => new(rule.Action.Equals("allow", StringComparison.OrdinalIgnoreCase),
        $"{site}/{section}: {rule.Action} — {(string.IsNullOrWhiteSpace(rule.Reason) ? $"{rule.Field} {rule.Match}: {string.Join(", ", rule.Values ?? [])}" : rule.Reason)}");

    private static SiteRule? FirstMatch(IEnumerable<SiteRule> rules, string release)
    {
        foreach (var rule in rules)
        {
            var subjects = rule.Field.ToLowerInvariant() switch
            {
                "release" => new[] { release },
                "group" => new[] { release.LastIndexOf('-') is var i && i >= 0 ? release[(i + 1)..] : "" },
                "tag" => release.Split(['.', '_', '-'], StringSplitOptions.RemoveEmptyEntries),
                _ => throw new InvalidDataException("Unsupported rule field.")
            };
            var matched = rule.Match.Equals("always", StringComparison.OrdinalIgnoreCase) || subjects.Any(subject => (rule.Values ?? []).Any(value => rule.Match.ToLowerInvariant() switch
            {
                "regex" => Regex.IsMatch(subject, value, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)),
                "glob" => FileSystemName.MatchesSimpleExpression(value, subject, true),
                "in" => subject.Equals(value, StringComparison.OrdinalIgnoreCase),
                _ => false
            }));
            if (rule.Negate ? !matched : matched) return rule;
        }
        return null;
    }

    private static void Validate(SiteRuleFile file)
    {
        if (file.Version != 1 || string.IsNullOrWhiteSpace(file.Site) || file.Rules is null || file.Sections is null)
            throw new InvalidDataException("Expected version 1, site, rules and sections.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in file.Sections)
        {
            if (section is null || string.IsNullOrWhiteSpace(section.Name) || !names.Add(section.Name) || section.Rules is null || !Action(section.DefaultAction))
                throw new InvalidDataException("Invalid or duplicate section.");
            if (string.IsNullOrWhiteSpace(section.Path) || Normalize(section.Path) != section.Path.TrimEnd('/') && section.Path != "/")
                throw new InvalidDataException($"Section {section.Name} needs an absolute, normalized FTP path.");
        }
        var rules = file.Rules.Concat(file.Sections.SelectMany(s => s.Rules)).ToArray();
        if (rules.Length > 1000) throw new InvalidDataException("Too many rules.");
        foreach (var rule in rules)
        {
            if (rule is null || !Action(rule.Action) || !new[] { "release", "group", "tag" }.Contains(rule.Field, StringComparer.OrdinalIgnoreCase) ||
                !new[] { "regex", "glob", "in", "always" }.Contains(rule.Match, StringComparer.OrdinalIgnoreCase) ||
                (!rule.Match.Equals("always", StringComparison.OrdinalIgnoreCase) && (rule.Values is null || rule.Values.Length == 0)))
                throw new InvalidDataException("Unsupported rule. Fields: release/group/tag; matches: regex/glob/in/always; actions: allow/drop.");
            foreach (var value in rule.Values ?? [])
            {
                if (string.IsNullOrEmpty(value) || value.Length > 512) throw new InvalidDataException("Invalid rule value.");
                if (rule.Match.Equals("regex", StringComparison.OrdinalIgnoreCase)) _ = new Regex(value, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            }
        }
    }

    private static bool Action(string? action) => action is not null && (action.Equals("allow", StringComparison.OrdinalIgnoreCase) || action.Equals("drop", StringComparison.OrdinalIgnoreCase));
    private static string Root(string path) => path.TrimEnd('/') + "/";
    private static string Normalize(string path)
    {
        if (!path.StartsWith('/') || path.Contains('\\') || path.Any(char.IsControl) || path.Split('/').Any(p => p is "." or ".."))
            throw new InvalidDataException("Invalid FTP destination path.");
        return "/" + string.Join('/', path.Split('/', StringSplitOptions.RemoveEmptyEntries));
    }
}
