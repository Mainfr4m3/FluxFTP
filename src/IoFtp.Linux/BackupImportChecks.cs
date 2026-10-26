using System.Security.Cryptography;
using System.Text.Json;
using IoFtp.Core.Models;
namespace IoFtp.Linux;
internal static class BackupImportChecks
{
    public static int Run(string backup)
    {
        var root = Path.Combine(Path.GetTempPath(), "fluxftp-backup-check-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("FLUXFTP_CONFIG_HOME", root);
        var store = new LinuxConfiguration(); var original = store.Load(); var count = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); count++; }
        void Rejected(Action action) { try { action(); } catch (Exception ex) when (ex is InvalidDataException or CryptographicException or JsonException) { count++; return; } throw new Exception("Invalid import accepted"); }
        Rejected(() => WindowsBackupImport.Read(backup, "wrong-password", root, original));
        Check(!File.Exists(Path.Combine(root, "state.enc")), "Failed read wrote settings");
        var plan = WindowsBackupImport.Read(backup, "linux-test-password", root, original);
        Check(plan.Sites == 1 && plan.Sections == 1, "Site/section import failed");
        var site = plan.State.Sites.Single().Restore();
        Check(site.Username == "!roadtrip" && site.Password == "p@ss%20\ncoffee-å", "Windows password/login did not roundtrip");
        Check(site.Proxy?.Password == "proxy-secret", "Per-site proxy secret failed");
        Check(site.EffectiveOptions.MaxSlots == 12 && site.EffectiveOptions.MaxUploadSlots == 5 && site.EffectiveOptions.MaxDownloadSlots == 6 && site.EffectiveOptions.Priority == 7, "Site options failed");
        Check(plan.State.Sections.Single().SitePaths[site.Name] == "/TV", "Section path failed");
        Check(plan.State.Options.DownloadPath == root && plan.State.Options.MaxLocalDownloads == 3 && plan.State.Options.MaxLocalUploads == 2 && !plan.State.Options.SerializeLocalTransfers && plan.State.Options.StarterTimeoutSeconds == 22 && plan.State.Options.MaxTransferRuntimeMinutes == 0, "Compatible settings or path mapping failed");
        Check(plan.State.Options.Proxy?.Password == "global-secret", "Global proxy secret failed");
        store.Save(plan.State);
        var disk = File.ReadAllBytes(Path.Combine(root, "state.enc"));
        Check(!System.Text.Encoding.UTF8.GetString(disk).Contains("proxy-secret", StringComparison.Ordinal), "Secret persisted in plain text");
        Check(new LinuxConfiguration().Load().Sites.Single().Restore().Password == site.Password, "Saved site did not survive restart");
        var extra = new ConnectionProfile(Guid.NewGuid(), "Existing Linux site", "existing.invalid", 21, "user", TransferProtocol.Ftp, Password: "existing-secret");
        store.SaveSite(extra);
        var api = new LinuxApiOptions(Port: 55479, Password: "keep-api-secret");
        store.Update(state => state with { Options = state.Options with { Api = api, Layout = new(Metrics: false), LogBufferLines = 1234, ClearCompletedManualTransfers = true } });
        var merge = WindowsBackupImport.Read(backup, "linux-test-password", root, store.Load());
        Check(merge.State.Sites.Count == 2 && merge.State.Sites.Any(saved => saved.Restore().Password == "existing-secret"), "Merge lost existing Linux site or duplicated imported ID");
        Check(merge.State.Options.Api == api && merge.State.Options.Layout?.Metrics == false && merge.State.Options.LogBufferLines == 1234 && merge.State.Options.ClearCompletedManualTransfers, "Windows import overwrote Linux API, layout or log settings");
        Rejected(() => WindowsBackupImport.Read(backup, "wrong-password", root, store.Load()));
        Check(new LinuxConfiguration().Load().Sites.Count == 2, "Failed import changed settings");
        var fixture = Path.Combine(root, "bad.fluxbackup");
        void WritePackage(List<PortableFile> files)
        {
            var plain = JsonSerializer.SerializeToUtf8Bytes(new PortablePackage(1, DateTimeOffset.UtcNow, files));
            var salt = RandomNumberGenerator.GetBytes(16); var nonce = RandomNumberGenerator.GetBytes(12); var tag = new byte[16]; var cipher = new byte[plain.Length];
            var key = Rfc2898DeriveBytes.Pbkdf2("test-password", salt, 210000, HashAlgorithmName.SHA256, 32);
            using var aes = new AesGcm(key, 16); aes.Encrypt(nonce, plain, cipher, tag);
            File.WriteAllBytes(fixture, JsonSerializer.SerializeToUtf8Bytes(new PortableEnvelope("FluxFTP-backup-v1", salt, nonce, tag, cipher))); CryptographicOperations.ZeroMemory(key);
        }
        WritePackage([new("app/../../escape.ini", [])]); Rejected(() => WindowsBackupImport.Read(fixture, "test-password", root, store.Load()));
        WritePackage([new("app/file.ini", []), new("app/FILE.ini", [])]); Rejected(() => WindowsBackupImport.Read(fixture, "test-password", root, store.Load()));
        WritePackage([new("app/FluxFTP-sites.ini", System.Text.Encoding.UTF8.GetBytes("; FluxFTP INI format: 2 (literal values; @json: for escaped values)\n[site:22222222-2222-2222-2222-222222222222]\nName=blocked\nHost=host.invalid\nPassword=dpapi:AAAA\n"))]);
        Rejected(() => WindowsBackupImport.Read(fixture, "test-password", root, store.Load()));
        if (OperatingSystem.IsLinux())
        {
            var forbidden = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
            Check((File.GetUnixFileMode(Path.Combine(root, "state.enc")) & forbidden) == 0 && (File.GetUnixFileMode(Path.Combine(root, "secret.key")) & forbidden) == 0, "Linux secrets are not private");
        }
        Console.WriteLine($"PASS: {count} Windows-to-Linux backup, password, merge, settings, integrity and private storage checks."); return 0;
    }
}
