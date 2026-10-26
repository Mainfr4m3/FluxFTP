using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IoFtp.Core.Models;
namespace IoFtp.Linux;
internal static class SecretStore
{
    public static string DirectoryPath => Environment.GetEnvironmentVariable("FLUXFTP_CONFIG_HOME") ?? Path.Combine(
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "fluxftp-linux");
    public static void EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
    private static byte[] Key()
    {
        EnsureDirectory(DirectoryPath);
        var path = Path.Combine(DirectoryPath, "secret.key");
        if (!File.Exists(path))
        {
            if (File.Exists(Path.Combine(DirectoryPath, "state.enc"))) throw new InvalidDataException("Linux secret.key is missing; restore it together with state.enc.");
            var key = RandomNumberGenerator.GetBytes(32);
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (OperatingSystem.IsLinux()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using var file = new FileStream(path, options);
            file.Write(key); file.Flush(true);
        }
        var data = File.ReadAllBytes(path);
        if (data.Length != 32) throw new InvalidDataException("Invalid Linux key; restore secret.key together with state.enc.");
        return data;
    }
    public static string Protect(string text)
    {
        if (text.StartsWith("linux-secret:v1:", StringComparison.Ordinal)) text = Unprotect(text);
        if (text.StartsWith("flux-secret:", StringComparison.Ordinal)) text = Encoding.UTF8.GetString(Convert.FromBase64String(text[12..]));
        var data = Encoding.UTF8.GetBytes(text); var key = Key();
        var nonce = RandomNumberGenerator.GetBytes(12); var tag = new byte[16]; var cipher = new byte[data.Length];
        try
        {
            using var aes = new AesGcm(key, 16); aes.Encrypt(nonce, data, cipher, tag);
            return "linux-secret:v1:" + Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(data); }
    }
    public static string Unprotect(string text)
    {
        if (text.StartsWith("dpapi:", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Windows DPAPI data cannot be imported directly. Export a password-protected .fluxbackup from Windows first.");
        if (text.StartsWith("flux-secret:", StringComparison.Ordinal)) return Encoding.UTF8.GetString(Convert.FromBase64String(text[12..]));
        if (!text.StartsWith("linux-secret:v1:", StringComparison.Ordinal)) return text;
        var data = Convert.FromBase64String(text[16..]);
        if (data.Length < 28) throw new InvalidDataException("Invalid Linux secret.");
        var plain = new byte[data.Length - 28]; var key = Key();
        try { using var aes = new AesGcm(key, 16); aes.Decrypt(data.AsSpan(0, 12), data.AsSpan(28), data.AsSpan(12, 16), plain); return Encoding.UTF8.GetString(plain); }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
    }
    public static void WritePrivate(string path, string content)
    {
        EnsureDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (OperatingSystem.IsLinux()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var file = new FileStream(temporary, options))
            { var bytes = Encoding.UTF8.GetBytes(content); try { file.Write(bytes); file.Flush(true); } finally { CryptographicOperations.ZeroMemory(bytes); } }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
internal sealed record SavedSite(ConnectionProfile Profile, string Password)
{
    public ConnectionProfile Restore() => Profile with { Password = Password };
    public static SavedSite Capture(ConnectionProfile profile) => new(profile with { Password = "" }, profile.Password);
}
internal sealed record LinuxSection(string Name, Dictionary<string, string> SitePaths)
{
    public override string ToString() => Name;
}
internal sealed record LinuxOptions(string DownloadPath, int MaxLocalDownloads = 1, int MaxLocalUploads = 1,
    bool SerializeLocalTransfers = true, int StarterTimeoutSeconds = 15, int MaxTransferRuntimeMinutes = 15, ProxyConfiguration? Proxy = null);
internal sealed record LinuxState(List<SavedSite> Sites, List<LinuxSection> Sections, LinuxOptions Options);
internal sealed class LinuxConfiguration
{
    private static string StatePath => Path.Combine(SecretStore.DirectoryPath, "state.enc");
    public LinuxState Load()
    {
        if (!File.Exists(StatePath)) return new([], [], new(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
        return JsonSerializer.Deserialize<LinuxState>(SecretStore.Unprotect(File.ReadAllText(StatePath))) ?? throw new InvalidDataException("Invalid saved Linux configuration.");
    }
    public void Save(LinuxState state) => SecretStore.WritePrivate(StatePath, SecretStore.Protect(JsonSerializer.Serialize(state)));
    public void SaveSite(ConnectionProfile profile)
    {
        var state = Load(); var sites = state.Sites.Where(site => site.Profile.Id != profile.Id).Append(SavedSite.Capture(profile)).ToList();
        Save(state with { Sites = sites });
    }
}
