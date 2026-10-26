using IoFtp.Core.Models;
using IoFtp.Desktop.Models;
using IoFtp.Desktop.Services;
using System.Text.Json;
internal static class WindowsLinuxBackupFixture
{
    public static void Write(string destination)
    {
        var root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(destination))!, "windows-export-" + Guid.NewGuid().ToString("N"));
        var app = Path.Combine(root, "app"); var user = Path.Combine(root, "user"); Directory.CreateDirectory(app); Directory.CreateDirectory(user);
        var first = new ConnectionProfile(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Windows test site", "127.0.0.1", 21, "!roadtrip", TransferProtocol.FtpsExplicit,
            Password: "p@ss%20\ncoffee-å", Options: new SiteOptions(MaxSlots: 12, MaxUploadSlots: 5, MaxDownloadSlots: 6, Priority: 7, BasePath: "/archive"),
            Proxy: new ProxyConfiguration(ProxyType.Socks5, "proxy.invalid", 1080, "proxy-user", "proxy-secret"));
        new ProfileStore(app).Save([first]);
        new GlobalSettingsStore(user).Save(new GlobalSettings(MaxLocalDownloadSlots: 3, MaxLocalUploadSlots: 2, SerializeLocalTransfers: false, StarterTimeoutSeconds: 22, MaxTransferRuntimeMinutes: 0,
            LocalDownloadPath: @"R:\Downloads", ProxyType: ProxyType.Socks5, ProxyHost: "global-proxy.invalid", ProxyPassword: "global-secret"));
        File.WriteAllText(Path.Combine(app, "FluxFTP-sections.json"), JsonSerializer.Serialize(new[] { new { Name = "TV", SitePaths = new Dictionary<string, string> { [first.Name] = "/TV" } } }));
        new BackupService(app, user).Export(destination, "linux-test-password");
        Console.WriteLine("PASS: Windows exporter produced portable encrypted Linux test fixture.");
    }
}
