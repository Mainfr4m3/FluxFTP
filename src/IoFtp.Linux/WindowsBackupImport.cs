using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IoFtp.Core.Models;
using IoFtp.Desktop.Services;
namespace IoFtp.Linux;
internal sealed record PortableFile(string Path, byte[] Data);
internal sealed record PortablePackage(int Version, DateTimeOffset Created, List<PortableFile> Files);
internal sealed record PortableEnvelope(string Format, byte[] Salt, byte[] Nonce, byte[] Tag, byte[] Data);
internal sealed record ImportPlan(LinuxState State, int Sites, int Sections, string Summary);
internal static class WindowsBackupImport
{
    private const int MaximumBytes = 512 * 1024 * 1024;
    public static ImportPlan Read(string path, string password, string localDirectory, LinuxState current)
    {
        if (!Path.GetExtension(path).Equals(".fluxbackup", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Choose a password-protected .fluxbackup exported from Windows, not a .fluxrestore or copied INI.");
        if (new FileInfo(path).Length > MaximumBytes * 2L) throw new InvalidDataException("Backup exceeds the import size limit.");
        var envelope = JsonSerializer.Deserialize<PortableEnvelope>(File.ReadAllBytes(path)) ?? throw new InvalidDataException("Invalid backup envelope.");
        if (envelope.Format != "FluxFTP-backup-v1" || envelope.Salt?.Length != 16 || envelope.Nonce?.Length != 12 || envelope.Tag?.Length != 16 || envelope.Data is null || envelope.Data.Length > MaximumBytes * 2L)
            throw new InvalidDataException("Unsupported backup format.");
        var key = Rfc2898DeriveBytes.Pbkdf2(password, envelope.Salt, 210000, HashAlgorithmName.SHA256, 32); var plain = new byte[envelope.Data.Length];
        try
        {
            try { using var aes = new AesGcm(key, 16); aes.Decrypt(envelope.Nonce, envelope.Data, envelope.Tag, plain); }
            catch (CryptographicException) { throw new InvalidDataException("Wrong backup password or damaged backup. No configuration changed."); }
            var package = JsonSerializer.Deserialize<PortablePackage>(plain) ?? throw new InvalidDataException("Invalid backup contents.");
            try { return Build(package, localDirectory, current); }
            finally { if (package.Files is not null) foreach (var file in package.Files) if (file?.Data is not null) CryptographicOperations.ZeroMemory(file.Data); }
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
    }
    private static ImportPlan Build(PortablePackage package, string localDirectory, LinuxState current)
    {
        if (package.Version != 1 || package.Files is null || package.Files.Count > 10000) throw new InvalidDataException("Unsupported backup package.");
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase); long total = 0;
        foreach (var file in package.Files)
        {
            if (file?.Data is null || file.Path is null || file.Path.Contains('\\') || file.Path.Contains(':') || file.Path.StartsWith('/') ||
                file.Path.Split('/').Any(part => part is "" or "." or ".." || part.Any(char.IsControl)) ||
                !(file.Path.StartsWith("app/", StringComparison.Ordinal) || file.Path.StartsWith("user/", StringComparison.Ordinal)) || !files.TryAdd(file.Path, file.Data))
                throw new InvalidDataException("Invalid or duplicate backup path.");
            total += file.Data.LongLength; if (total > MaximumBytes) throw new InvalidDataException("Backup exceeds 512 MB.");
        }
        if (!Path.IsPathFullyQualified(localDirectory) || (OperatingSystem.IsLinux() && !localDirectory.StartsWith('/'))) throw new InvalidDataException("Choose an absolute Linux folder for local downloads.");
        var sites = new List<ConnectionProfile>();
        if (files.TryGetValue("app/FluxFTP-sites.ini", out var ini) || files.TryGetValue("app/ioFTP-sites.ini", out ini))
        {
            var staging = Path.Combine(SecretStore.DirectoryPath, "import-" + Guid.NewGuid().ToString("N")); SecretStore.EnsureDirectory(staging);
            var iniPath = Path.Combine(staging, "FluxFTP-sites.ini");
            try
            {
                // Convert portable credentials before staging; never leave portable secrets in a temporary INI.
                var text = System.Text.RegularExpressions.Regex.Replace(Encoding.UTF8.GetString(ini), @"flux-secret:[A-Za-z0-9+/=]+", match => SecretStore.Protect(match.Value), System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
                SecretStore.WritePrivate(iniPath, text);
                sites = new ProfileStore(staging).Load().ToList();
            }
            finally { foreach (var file in Directory.EnumerateFiles(staging)) File.Delete(file); Directory.Delete(staging); }
        }
        var sections = new List<LinuxSection>();
        if (files.TryGetValue("app/FluxFTP-sections.json", out var sectionData))
            sections = JsonSerializer.Deserialize<List<LinuxSection>>(sectionData) ?? throw new InvalidDataException("Invalid sections.");
        if (sections.Any(section => string.IsNullOrWhiteSpace(section.Name) || section.SitePaths is null)) throw new InvalidDataException("Invalid section definitions.");
        JsonObject settings = files.TryGetValue("user/settings.json", out var settingsData) ? JsonNode.Parse(settingsData) as JsonObject ?? throw new InvalidDataException("Invalid settings.") : new();
        int Number(string name, int fallback, int min, int max) => Math.Clamp(settings[name]?.GetValue<int>() ?? fallback, min, max);
        bool Flag(string name, bool fallback) => settings[name]?.GetValue<bool>() ?? fallback;
        ProxyConfiguration? proxy = null;
        var proxyType = (ProxyType)Number("ProxyType", 0, 0, 3);
        if (proxyType != ProxyType.None) proxy = new(proxyType, settings["ProxyHost"]?.GetValue<string>() ?? "", Number("ProxyPort", 1080, 1, 65535),
            settings["ProxyUsername"]?.GetValue<string>() ?? "", SecretStore.Unprotect(settings["ProxyPassword"]?.GetValue<string>() ?? ""), Flag("ProxyDns", true), Flag("ProxyDataConnections", true));
        foreach (var site in sites)
            if (site.Id == Guid.Empty || string.IsNullOrWhiteSpace(site.Name) || string.IsNullOrWhiteSpace(site.Host) || site.Port is < 1 or > 65535) throw new InvalidDataException("Invalid site in backup.");
        var merged = current.Sites.ToDictionary(site => site.Profile.Id);
        foreach (var site in sites) merged[site.Id] = SavedSite.Capture(site);
        var mergedSections = current.Sections.ToDictionary(section => section.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var section in sections) mergedSections[section.Name] = section;
        var options = new LinuxOptions(localDirectory, Number("MaxLocalDownloadSlots", 1, 0, 2048), Number("MaxLocalUploadSlots", 1, 0, 2048), Flag("SerializeLocalTransfers", true),
            Number("StarterTimeoutSeconds", 15, 1, 3600), Number("MaxTransferRuntimeMinutes", 15, 0, 1440), proxy, current.Options.Api,
            current.Options.LogBufferLines, current.Options.ClearCompletedManualTransfers, current.Options.Layout);
        var state = new LinuxState(merged.Values.ToList(), mergedSections.Values.ToList(), options);
        return new(state, sites.Count, sections.Count, $"Ready: {sites.Count} sites (including portable passwords), {sections.Count} sections, local slots, transfer timeouts and proxy settings.\nExisting sites with matching IDs will be updated; other sites are preserved.\nWindows local paths are replaced by: {localDirectory}\nVisi, scripts, rules, theme/layout, history and queued jobs are not activated/imported in this preview.");
    }
}
