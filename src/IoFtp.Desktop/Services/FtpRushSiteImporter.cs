using System.Text;
using System.Text.Json;
using System.IO;
using System.Xml.Linq;
using IoFtp.Core.Models;

namespace IoFtp.Desktop.Services;

internal sealed record FtpRushImportedSite(ConnectionProfile Profile, string GroupPath);
internal sealed record FtpRushImportPackage(IReadOnlyList<FtpRushImportedSite> Sites, IReadOnlyList<SiteBookmark> Bookmarks);

internal static class FtpRushPasswordFile
{
    // Split only at the first '='. Password whitespace and remaining '=' are significant.
    public static IReadOnlyDictionary<string, string> Read(string path, IEnumerable<ConnectionProfile> profiles)
    {
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("Password file exceeds 1 MB.");
        var imported = profiles.ToArray();
        var sites = imported.GroupBy(profile => profile.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = File.ReadAllLines(path, new UTF8Encoding(false, true));
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#') || line.TrimStart().StartsWith(';')) continue;
            string site, password;
            if (line.TrimStart().StartsWith("ftp://", StringComparison.OrdinalIgnoreCase))
            {
                var address = line.Trim()[6..];
                var at = address.LastIndexOf('@');
                var colon = address.IndexOf(':');
                if (at <= 0 || colon <= 0 || colon >= at ||
                    !Uri.TryCreate("ftp://" + address[(at + 1)..], UriKind.Absolute, out var endpoint) ||
                    endpoint.Host.Length == 0 || endpoint.UserInfo.Length != 0 || endpoint.Port < 1 ||
                    endpoint.AbsolutePath is not ("" or "/") || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0)
                    throw new InvalidDataException($"Password file line {i + 1}: expected ftp://user:password@host:port.");
                var username = Uri.UnescapeDataString(address[..colon]);
                password = Uri.UnescapeDataString(address[(colon + 1)..at]);
                var matches = imported.Where(profile =>
                    profile.Host.Trim().Trim('[', ']').Equals(endpoint.Host.Trim('[', ']'), StringComparison.OrdinalIgnoreCase) &&
                    profile.Port == endpoint.Port && profile.Username.Equals(username, StringComparison.Ordinal)).ToArray();
                if (matches.Length != 1)
                    throw new InvalidDataException($"Password file line {i + 1}: address, port and username must match exactly one imported site.");
                site = matches[0].Name.Trim();
            }
            else
            {
                var separator = line.IndexOf('=');
                if (separator <= 0) throw new InvalidDataException($"Password file line {i + 1}: expected Site name=password or an FTP URL.");
                site = line[..separator].Trim();
                password = line[(separator + 1)..];
            }
            if (!sites.TryGetValue(site, out var count) || count != 1)
                throw new InvalidDataException($"Password file line {i + 1}: site name must match exactly one imported site.");
            if (password.Length == 0 || password.Any(char.IsControl))
                throw new InvalidDataException($"Password file line {i + 1}: password must be nonempty and contain no control characters.");
            if (!result.TryAdd(site, password)) throw new InvalidDataException($"Password file line {i + 1}: duplicate site entry.");
        }
        if (result.Count == 0) throw new InvalidDataException("No passwords found in the file.");
        return result;
    }
}

internal static class FtpRushSiteImporter
{
    internal static ConnectionProfile MergeProfile(ConnectionProfile existing, ConnectionProfile incoming, bool replace, bool updatePassword)
    {
        if (!replace) return updatePassword && incoming.Password.Length > 0 ? existing with { Password = incoming.Password } : existing;
        return incoming with { Id = existing.Id, Name = existing.Name,
            Password = incoming.Password.Length > 0 ? incoming.Password : existing.Password };
    }
    public static IReadOnlyList<FtpRushImportedSite> Import(string path)
        => ImportPackage(path).Sites;

    public static FtpRushImportPackage ImportPackage(string path)
    {
        if (Path.GetExtension(path).Equals(".xml", StringComparison.OrdinalIgnoreCase))
            return ImportLegacyXml(path);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("RootItem", out var root))
            throw new InvalidDataException("The file is not a supported FTPRush site.json file.");
        var result = new List<FtpRushImportedSite>();
        var bookmarks = new List<SiteBookmark>();
        Walk(root, "", result, bookmarks);
        var settingsPath = Path.Combine(Path.GetDirectoryName(path)!, "core_setting.json");
        if (File.Exists(settingsPath))
        {
            using var settings = JsonDocument.Parse(File.ReadAllText(settingsPath));
            ReadBookmarksRecursive(settings.RootElement, "", bookmarks);
        }
        return new(result, bookmarks.DistinctBy(item => $"{item.SiteName}\0{item.Name}\0{item.Path}",
            StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static FtpRushImportPackage ImportLegacyXml(string path)
    {
        var document = XDocument.Load(path, LoadOptions.None);
        var result = new List<FtpRushImportedSite>();
        var bookmarks = new List<SiteBookmark>();
        if (document.Root is null) throw new InvalidDataException("The FTPRush XML file has no root element.");
        WalkLegacy(document.Root, "", result, bookmarks);
        return new(result, bookmarks.DistinctBy(item => $"{item.SiteName}\0{item.Name}\0{item.Path}",
            StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static void WalkLegacy(XElement node, string parentPath, List<FtpRushImportedSite> result,
        List<SiteBookmark> bookmarks)
    {
        var name = node.Attribute("NAME")?.Value?.Trim();
        var host = LegacyValue(node, "HOST");
        var isSite = !string.IsNullOrWhiteSpace(host) || node.Attribute("UID") is not null;
        if (isSite && !string.IsNullOrWhiteSpace(host))
        {
            var port = int.TryParse(LegacyValue(node, "PORT", "FTPPORT"), out var parsedPort) ? parsedPort : 21;
            var protocol = TransferProtocol.FtpsExplicit;
            var remotePath = LegacyValue(node, "REMOTEPATH", "REMOTE_PATH", "PATH", "DEFAULTREMOTEPATH").Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(remotePath)) remotePath = "/";
            if (!remotePath.StartsWith('/')) remotePath = "/" + remotePath;
            var profile = new ConnectionProfile(Guid.NewGuid(), string.IsNullOrWhiteSpace(name) ? host : name, host, port,
                LegacyValue(node, "USERNAME", "USER"), protocol, "", false, DirectoryListingMode.Auto,
                new SiteOptions(BasePath: remotePath,
                    ImportedSkipRules: string.Join("\n", node.Elements("SKIP").Elements("I").Select(item => item.Value.Trim())),
                    ImportedPriorityRules: string.Join("\n", node.Elements("PRIO").Elements("I").Select(item => item.Value.Trim()))));
            result.Add(new(profile, parentPath));
            ReadLegacyBookmarks(node, profile.Name, bookmarks);
        }
        var nextPath = !isSite && !string.IsNullOrWhiteSpace(name)
            ? string.IsNullOrWhiteSpace(parentPath) ? name : $"{parentPath} / {name}"
            : parentPath;
        foreach (var child in node.Elements().Where(child => !IsLegacyValueElement(child.Name.LocalName)))
            WalkLegacy(child, nextPath, result, bookmarks);
    }

    private static void ReadLegacyBookmarks(XElement site, string siteName, List<SiteBookmark> result)
    {
        foreach (var element in site.Descendants())
        {
            var inBookmarkTree = element.AncestorsAndSelf().Any(item =>
                item.Name.LocalName.Contains("BOOKMARK", StringComparison.OrdinalIgnoreCase));
            if (!inBookmarkTree) continue;
            var path = LegacyValue(element, "REMOTE", "REMOTEPATH", "REMOTE_PATH", "PATH", "DIRECTORY", "DIR")
                .Trim().Replace('\\', '/');
            if (path.Length == 0) continue;
            if (!path.StartsWith('/')) path = "/" + path;
            var name = LegacyValue(element, "CAPTION", "NAME", "TITLE", "LABEL").Trim();
            if (name.Length == 0) name = path.TrimEnd('/').Split('/').LastOrDefault() ?? path;
            result.Add(new(name, path, siteName));
        }
    }

    private static string LegacyValue(XElement node, params string[] names)
    {
        foreach (var name in names)
        {
            var element = node.Elements().FirstOrDefault(child => child.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (element is not null) return element.Value.Trim();
            var attribute = node.Attributes().FirstOrDefault(item => item.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (attribute is not null) return attribute.Value.Trim();
        }
        return "";
    }


    private static bool IsLegacyValueElement(string name) => name.Equals("HOST", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("PORT", StringComparison.OrdinalIgnoreCase) || name.Equals("FTPPORT", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("USERNAME", StringComparison.OrdinalIgnoreCase) || name.Equals("USER", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("PASSWORD", StringComparison.OrdinalIgnoreCase) || name.Equals("PASS", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("REMOTEPATH", StringComparison.OrdinalIgnoreCase) || name.Equals("REMOTE_PATH", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("PATH", StringComparison.OrdinalIgnoreCase) || name.Equals("DEFAULTREMOTEPATH", StringComparison.OrdinalIgnoreCase);

    private static void Walk(JsonElement node, string parentPath, List<FtpRushImportedSite> result, List<SiteBookmark> bookmarks)
    {
        var nodeName = Text(node, "Name");
        var groupPath = string.IsNullOrWhiteSpace(nodeName) ? parentPath : string.IsNullOrWhiteSpace(parentPath) ? nodeName : $"{parentPath} / {nodeName}";
        if (node.TryGetProperty("Server", out var server) && server.ValueKind == JsonValueKind.Object)
        {
            var protocolNumber = Number(server, "Protocol", 1);
            var protocol = protocolNumber switch
            {
                1 => Number(server, "FTPEnryptMode", 0) switch
                {
                    1 => TransferProtocol.FtpsExplicit,
                    2 => TransferProtocol.FtpsImplicit,
                    _ => TransferProtocol.Ftp
                },
                2 => TransferProtocol.Sftp,
                _ => (TransferProtocol?)null
            };
            var host = Text(server, "Host");
            if (protocol is not null && !string.IsNullOrWhiteSpace(host))
            {
                var name = Text(server, "Name");
                if (string.IsNullOrWhiteSpace(name)) name = nodeName;
                if (string.IsNullOrWhiteSpace(name)) name = host;
                var port = Number(server, "Port", protocol == TransferProtocol.Sftp ? 22 : protocol == TransferProtocol.FtpsImplicit ? 990 : 21);
                var remotePath = Text(server, "DefaultRemotePath").Replace('\\', '/');
                if (string.IsNullOrWhiteSpace(remotePath)) remotePath = "/";
                if (!remotePath.StartsWith('/')) remotePath = "/" + remotePath;
                var password = DecodeBase64(Text(server, "Base64Password"));
                var profile = new ConnectionProfile(Guid.NewGuid(), name, host, port, Text(server, "Username"),
                    protocol == TransferProtocol.Sftp ? TransferProtocol.Sftp : TransferProtocol.FtpsExplicit,
                    password, false, DirectoryListingMode.Auto, new SiteOptions(BasePath: remotePath));
                result.Add(new(profile, parentPath));
                ReadBookmarks(server, profile.Name, bookmarks);
            }
        }
        if (node.TryGetProperty("Children", out var children) && children.ValueKind == JsonValueKind.Array)
            foreach (var child in children.EnumerateArray()) Walk(child, groupPath, result, bookmarks);
    }

    private static void ReadBookmarks(JsonElement element, string siteName, List<SiteBookmark> result)
    {
        if (!TryGetProperty(element, "BookMarks", out var bookmarks) || bookmarks.ValueKind != JsonValueKind.Array) return;
        foreach (var bookmark in bookmarks.EnumerateArray())
        {
            var name = Text(bookmark, "Name").Trim();
            var path = FirstText(bookmark, "Path", "RemotePath", "DefaultRemotePath").Trim().Replace('\\', '/');
            if (name.Length > 0 && path.Length > 0) result.Add(new(name, path, siteName));
        }
    }

    private static void ReadBookmarksRecursive(JsonElement element, string siteName, List<SiteBookmark> result)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            ReadBookmarks(element, siteName, result);
            foreach (var property in element.EnumerateObject()) ReadBookmarksRecursive(property.Value, siteName, result);
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) ReadBookmarksRecursive(item, siteName, result);
    }

    private static string FirstText(JsonElement element, params string[] names)
    {
        foreach (var name in names)
            if (TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString() ?? "";
        return "";
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                { value = property.Value; return true; }
        value = default;
        return false;
    }

    private static string Text(JsonElement element, string name) => FirstText(element, name);

    private static int Number(JsonElement element, string name, int fallback) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : fallback;

    private static string DecodeBase64(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(value)); }
        catch { return ""; }
    }
}
