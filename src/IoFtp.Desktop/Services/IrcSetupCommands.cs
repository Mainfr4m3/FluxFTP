using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using IoFtp.Core.Models;

namespace IoFtp.Desktop.Services;

/// <summary>Invoked only after IRC identity and current FTP admin verification.</summary>
internal sealed class IrcSetupCommands(Func<IReadOnlyList<ConnectionProfile>> loadProfiles,
    Action<IEnumerable<ConnectionProfile>> saveProfiles, SiteRuleStore rules, Guid? authoritySiteId,
    IrcRoutingStore? routing = null, IrcCatcher? catcher = null, string primaryNetwork = "Default")
{
    public const string Help = "Setup (private): !addsite SITE USER PASSWORD HOST:PORT [explicit|implicit|off], !site SITE, !sites, !slots SITE N, !maxupdn SITE UP DOWN, !maxidle SITE SECONDS, !tls SITE explicit|implicit|off, !setaffils SITE GROUPS, !setdir SITE SECTION /path, !ruleadd SITE SECTION|* if ... then allow|drop, !siterules SITE";

    public string Execute(string line)
    {
        try { return ExecuteCore(line); }
        catch (SetupException ex) { return "ERROR: " + ex.Message; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        { return "ERROR: Could not save configuration. Check the local files and rule syntax; no success was confirmed."; }
    }

    private string ExecuteCore(string line)
    {
        if (line.Length > 1500 || line.Any(char.IsControl)) throw new SetupException("Use one command per line, without control characters.");
        var isRule = line.StartsWith("!ruleadd ", StringComparison.OrdinalIgnoreCase);
        var args = Tokenize(isRule ? line[..FindCondition(line)] : line);
        if (args.Count == 0) throw new SetupException("Empty command.");
        var command = args[0].ToLowerInvariant();
        if (command.StartsWith("!irc", StringComparison.Ordinal) || command.StartsWith("!catch", StringComparison.Ordinal))
            return new IrcRoutingCommands(routing ?? new(), catcher ?? new(), loadProfiles, primaryNetwork).Execute(args);
        var supported = new[] { "!addsite", "!site", "!sites", "!slots", "!maxupdn", "!maxidle", "!tls", "!setaffils", "!setdir", "!ruleadd", "!siterules" };
        if (!supported.Contains(command)) throw new SetupException("Unsupported command. Use !help setup.");
        var profiles = loadProfiles().ToList();
        if (command == "!sites")
        {
            Count(args, 1, "!sites");
            return profiles.Count == 0 ? "OK: No saved sites." : $"OK: {profiles.Count} sites: {string.Join(", ", profiles.Take(10).Select(p => p.Name))}{(profiles.Count > 10 ? " (first 10)" : "")}";
        }
        if (args.Count < 2) throw new SetupException("A site name is required. Use !help setup.");
        var name = args[1];
        if (name.Length is 0 or > 64 || name.Contains('=') || name.StartsWith(';')) throw new SetupException("Invalid site name.");
        var index = profiles.FindIndex(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (command == "!addsite")
        {
            if (args.Count is not (5 or 6)) throw new SetupException("Syntax: !addsite SITE USER PASSWORD HOST:PORT [explicit|implicit|off]. Quote values containing spaces.");
            if (index >= 0 || profiles.Any(p => p.Description.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new SetupException("Site already exists; it was not overwritten.");
            if (!IrcProtocol.FtpUser(args[2]) || args[3].Length == 0) throw new SetupException("Invalid FTP username or empty password.");
            if (!SiteEndpoint.TryParse(args[4], 21, out var endpoint) || Uri.CheckHostName(endpoint.Host) == UriHostNameType.Unknown)
                throw new SetupException("Invalid FTP host/port.");
            var protocol = args.Count == 6 ? Protocol(args[5]) : TransferProtocol.FtpsExplicit;
            profiles.Add(new(Guid.NewGuid(), name, endpoint.Host, endpoint.Port, args[2], protocol, args[3], Options: new(MaxSlots: 4, MaxUploadSlots: 2, MaxDownloadSlots: 2)));
            saveProfiles(profiles);
            return $"OK: Site {name} created ({TransferProtocolNames.Display(protocol)}). Password saved with Windows protection; no connection started.";
        }
        if (index < 0) throw new SetupException("Site not found. Create it with !addsite first.");
        var profile = profiles[index];
        var options = profile.EffectiveOptions;
        switch (command)
        {
            case "!site":
                Count(args, 2, "!site SITE");
                return $"OK: {profile.Name}; {profile.Host}:{profile.Port}; {TransferProtocolNames.Display(profile.Protocol)}; slots {options.MaxSlots}, up {options.MaxUploadSlots}, down {options.MaxDownloadSlots}, idle {options.MaxIdleSeconds}s.";
            case "!slots":
                Count(args, 3, "!slots SITE N");
                var slots = Number(args[2], 1, 100, "slots");
                options = options with { MaxSlots = slots, MaxUploadSlots = Math.Min(slots, options.MaxUploadSlots), MaxDownloadSlots = Math.Min(slots, options.MaxDownloadSlots) };
                break;
            case "!maxupdn":
                Count(args, 4, "!maxupdn SITE UP DOWN");
                options = options with { MaxUploadSlots = Number(args[2], 0, options.MaxSlots, "upload slots"), MaxDownloadSlots = Number(args[3], 0, options.MaxSlots, "download slots") };
                break;
            case "!maxidle":
                Count(args, 3, "!maxidle SITE SECONDS (FluxFTP accepts one idle value)");
                options = options with { MaxIdleSeconds = Number(args[2], 0, 86400, "idle seconds") };
                break;
            case "!tls":
                Count(args, 3, "!tls SITE explicit|implicit|off");
                if (profile.Id == authoritySiteId) throw new SetupException("Change the IRC verification site's TLS settings locally in FluxFTP.");
                profile = profile with { Protocol = Protocol(args[2]) };
                break;
            case "!setaffils":
                if (args.Count < 3) throw new SetupException("Syntax: !setaffils SITE GROUP1 GROUP2 ...");
                options = options with { Affils = string.Join(' ', args.Skip(2)) };
                break;
            case "!setdir":
                Count(args, 4, "!setdir SITE SECTION /path");
                if (args[2].Length is 0 or > 64 || args[2] == "*") throw new SetupException("Use a section name; * is reserved for global rules.");
                var file = FindRules(profile.Name);
                var sections = file.Sections.ToList();
                var sectionIndex = sections.FindIndex(s => s.Name.Equals(args[2], StringComparison.OrdinalIgnoreCase));
                if (sectionIndex >= 0) sections[sectionIndex] = sections[sectionIndex] with { Path = args[3] };
                else sections.Add(new(args[2], args[3], []));
                rules.Save(file with { Sections = sections.ToArray() });
                return $"OK: {profile.Name}/{args[2]} -> {args[3]}. Saved in Rules/_site.";
            case "!siterules":
                Count(args, 2, "!siterules SITE");
                var saved = FindRules(profile.Name);
                return $"OK: {profile.Name}: {saved.Rules.Length} global rules; {saved.Sections.Length} sections: {string.Join(", ", saved.Sections.Take(10).Select(s => s.Name))}.";
            case "!ruleadd":
                // Preserve regex backslashes and whitespace; tokenize only the command/site/section header.
                if (args.Count != 3) throw new SetupException("Syntax: !ruleadd SITE SECTION|* if CONDITION then allow|drop.");
                var conditionStart = FindCondition(line);
                var rule = ParseRule(line[conditionStart..]);
                var rulesFile = FindRules(profile.Name);
                if (args[2] == "*") rulesFile = rulesFile with { Rules = [.. rulesFile.Rules, rule] };
                else
                {
                    var section = Array.FindIndex(rulesFile.Sections, s => s.Name.Equals(args[2], StringComparison.OrdinalIgnoreCase));
                    if (section < 0) throw new SetupException("Section not found. Use !setdir first.");
                    var updated = rulesFile.Sections.ToArray();
                    updated[section] = updated[section] with { Rules = [.. updated[section].Rules, rule] };
                    rulesFile = rulesFile with { Sections = updated };
                }
                rules.Save(rulesFile);
                return $"OK: Rule appended to {profile.Name}/{args[2]}. Rules are evaluated in order.";
        }
        profiles[index] = profile with { Options = options };
        saveProfiles(profiles);
        return $"OK: {command[1..]} saved for {profile.Name}.";
    }

    private SiteRuleFile FindRules(string site) => rules.Load().SingleOrDefault(f => f.Site.Equals(site, StringComparison.OrdinalIgnoreCase)) ?? new(1, site, [], []);
    private static TransferProtocol Protocol(string value) => value.ToLowerInvariant() switch
    {
        "explicit" => TransferProtocol.FtpsExplicit, "implicit" => TransferProtocol.FtpsImplicit, "off" => TransferProtocol.Ftp,
        _ => throw new SetupException("TLS must be explicit, implicit or off; slftp numeric modes are not interpreted.")
    };
    private static void Count(List<string> args, int count, string syntax) { if (args.Count != count) throw new SetupException("Syntax: " + syntax); }
    private static int Number(string value, int min, int max, string label) => int.TryParse(value, out var n) && n >= min && n <= max ? n : throw new SetupException($"{label} must be {min}..{max}.");
    private static int FindCondition(string line)
    {
        var match = Regex.Match(line, "^\\S+\\s+(?:\"[^\"]+\"|\\S+)\\s+(?:\"[^\"]+\"|\\S+)\\s+(if\\s+)", RegexOptions.IgnoreCase);
        if (!match.Success) throw new SetupException("Expected: !ruleadd SITE SECTION|* if CONDITION then allow|drop.");
        return match.Groups[1].Index;
    }
    internal static SiteRule ParseRule(string text)
    {
        var match = Regex.Match(text, @"\Aif\s+(.+)\s+then\s+(allow|drop)\s*\z", RegexOptions.IgnoreCase);
        if (!match.Success) throw new SetupException("Expected: if CONDITION then allow|drop.");
        var action = match.Groups[2].Value.ToLowerInvariant();
        var expression = match.Groups[1].Value;
        if (expression.Equals("default", StringComparison.OrdinalIgnoreCase)) return new(Match: "always", Action: action);
        var condition = Regex.Match(expression, @"\A(not\s+)?(releasename|release|group|tag)\s+(=~|in)\s+(.+)\z", RegexOptions.IgnoreCase);
        if (!condition.Success) throw new SetupException("Supported conditions: [not] releasename =~ /regex/i, [not] group/tag in VALUES, or default. Metadata conditions are not supported.");
        var field = condition.Groups[2].Value.ToLowerInvariant().Replace("releasename", "release");
        var value = condition.Groups[4].Value;
        string mode; string[] values;
        if (condition.Groups[3].Value == "=~")
        {
            var pattern = Regex.Match(value, @"\A/(.*)/i?\z", RegexOptions.IgnoreCase);
            if (!pattern.Success) throw new SetupException("Regex must use /pattern/i syntax.");
            mode = "regex"; values = [pattern.Groups[1].Value];
        }
        else { mode = "glob"; values = value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); }
        return new(field, mode, values, condition.Groups[1].Success, action);
    }
    private static List<string> Tokenize(string line)
    {
        var result = new List<string>(); var token = new StringBuilder(); var quoted = false; var started = false;
        foreach (var c in line)
        {
            if (c == '"') { quoted = !quoted; started = true; }
            else if (char.IsWhiteSpace(c) && !quoted) { if (started) { result.Add(token.ToString()); token.Clear(); started = false; } }
            else { token.Append(c); started = true; }
        }
        if (quoted) throw new SetupException("Unclosed quote.");
        if (started) result.Add(token.ToString());
        return result;
    }
    private sealed class SetupException(string message) : Exception(message);
}
