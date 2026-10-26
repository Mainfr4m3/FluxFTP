using IoFtp.Core.Models;
namespace IoFtp.Linux;
internal static class LinuxSettings
{
    public static void Validate(LinuxOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.DownloadPath) || !Path.IsPathFullyQualified(options.DownloadPath) || options.DownloadPath.Any(char.IsControl))
            throw new ArgumentException("Choose an absolute local download folder.");
        if (File.Exists(options.DownloadPath)) throw new ArgumentException("Download folder points to a file.");
        if (options.MaxLocalDownloads is < 0 or > 2048 || options.MaxLocalUploads is < 0 or > 2048)
            throw new ArgumentException("Local slots must be between 0 and 2048 (0 = disabled).");
        if (options.StarterTimeoutSeconds is < 1 or > 3600 || options.MaxTransferRuntimeMinutes is < 0 or > 10080)
            throw new ArgumentException("Connection timeout must be 1–3600 seconds; runtime must be 0–10080 minutes (0 = unlimited).");
        if (options.LogBufferLines is < 100 or > 20000) throw new ArgumentException("Log history must be between 100 and 20000 lines.");
        var proxy = options.Proxy;
        if (proxy is { Type: not ProxyType.None } && (string.IsNullOrWhiteSpace(proxy.Host) || proxy.Host.Any(char.IsControl) || proxy.Port is < 1 or > 65535))
            throw new ArgumentException("Enter a proxy host and port 1–65535.");
    }
    public static void SaveOptions(LinuxConfiguration configuration, LinuxOptions edited)
    {
        Validate(edited);
        Directory.CreateDirectory(edited.DownloadPath);
        configuration.Update(state => state with { Options = state.Options with
        {
            DownloadPath = Path.GetFullPath(edited.DownloadPath), MaxLocalDownloads = edited.MaxLocalDownloads,
            MaxLocalUploads = edited.MaxLocalUploads, SerializeLocalTransfers = edited.SerializeLocalTransfers,
            StarterTimeoutSeconds = edited.StarterTimeoutSeconds, MaxTransferRuntimeMinutes = edited.MaxTransferRuntimeMinutes,
            Proxy = edited.Proxy, LogBufferLines = edited.LogBufferLines, ClearCompletedManualTransfers = edited.ClearCompletedManualTransfers
        } });
    }
    public static string RemotePath(string path)
    {
        path = path.Trim();
        if (!path.StartsWith('/') || path.Any(char.IsControl) || path.Contains('\\') || path.Split('/').Any(part => part is "." or ".."))
            throw new ArgumentException("Use an absolute remote path without . or .. components.");
        return path.Length > 1 ? path.TrimEnd('/') : path;
    }
    public static void Name(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || name.Any(char.IsControl) || name.IndexOfAny(['/', '\\']) >= 0)
            throw new ArgumentException("Enter a name of 1–128 characters without slashes or control characters.");
    }
    public static void SaveSite(LinuxConfiguration configuration, ConnectionProfile profile)
    {
        Name(profile.Name);
        if (string.IsNullOrWhiteSpace(profile.Host) || profile.Host.Any(char.IsControl) || profile.Port is < 1 or > 65535 || profile.Username.Any(char.IsControl))
            throw new ArgumentException("Enter a host, valid port and username without control characters.");
        var options = profile.EffectiveOptions;
        if (options.MaxSlots is < 1 or > 2048 || options.MaxDownloadSlots is < 0 or > 2048 || options.MaxUploadSlots is < 0 or > 2048)
            throw new ArgumentException("Login slots must be 1–2048; upload/download slots must be 0–2048.");
        if (options.MaxDownloadSlots > options.MaxSlots || options.MaxUploadSlots > options.MaxSlots)
            throw new ArgumentException("Upload/download slots cannot exceed login slots.");
        _ = RemotePath(options.BasePath);
        configuration.Update(state =>
        {
            if (state.Sites.Any(site => site.Profile.Id != profile.Id && site.Profile.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("A site with this name already exists.");
            var previous = state.Sites.FirstOrDefault(site => site.Profile.Id == profile.Id)?.Profile.Name;
            var sections = state.Sections.Select(section =>
            {
                var paths = new Dictionary<string, string>(section.SitePaths, StringComparer.OrdinalIgnoreCase);
                if (previous is not null && paths.Remove(previous, out var path)) paths[profile.Name] = path;
                return section with { SitePaths = paths };
            }).ToList();
            return state with { Sites = state.Sites.Where(site => site.Profile.Id != profile.Id).Append(SavedSite.Capture(profile)).ToList(), Sections = sections };
        });
    }
    public static void DeleteSite(LinuxConfiguration configuration, Guid id) => configuration.Update(state =>
    {
        var name = state.Sites.Single(site => site.Profile.Id == id).Profile.Name;
        return state with { Sites = state.Sites.Where(site => site.Profile.Id != id).ToList(), Sections = state.Sections.Select(section => section with
            { SitePaths = section.SitePaths.Where(pair => !pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).ToDictionary() }).ToList() };
    });
    public static void SaveSection(LinuxConfiguration configuration, string? original, string name, Dictionary<string, string> paths)
    {
        Name(name);
        var clean = paths.ToDictionary(pair => pair.Key, pair => RemotePath(pair.Value), StringComparer.OrdinalIgnoreCase);
        configuration.Update(state =>
        {
            if (clean.Keys.Any(key => !state.Sites.Any(site => site.Profile.Name.Equals(key, StringComparison.OrdinalIgnoreCase))))
                throw new ArgumentException("A section refers to a site that no longer exists. Reopen Sections and try again.");
            if (state.Sections.Any(section => !string.Equals(section.Name, original, StringComparison.OrdinalIgnoreCase) && section.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("A section with this name already exists.");
            return state with { Sections = state.Sections.Where(section => !section.Name.Equals(original, StringComparison.OrdinalIgnoreCase)).Append(new LinuxSection(name, clean)).ToList() };
        });
    }
}
