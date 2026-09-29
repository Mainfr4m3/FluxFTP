using System.IO;
using IoFtp.Desktop.Models;
using IoFtp.Desktop.Services;

internal static class GlobalSettingsChecks
{
    public static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FluxFTP-settings-" + Guid.NewGuid());
        var count = 0;
        void Check(bool condition, string name) { if (!condition) throw new Exception("Global settings: " + name); count++; }
        try
        {
            var irc = new IrcSettings(Enabled: true, Host: "irc.example.test", Port: 6697, UseTls: true,
                Nick: "FluxTest", Password: "dpapi:literal-password %20", UseZnc: true, ZncUsername: "user",
                ZncNetwork: "network", Channel: "#test", NetworkName: "Example", AccountLinks: [new("account", "admin")], AllowInvalidCertificate: false);
            var settings = new GlobalSettings(Irc: irc);
            new GlobalSettingsStore(directory).Save(settings);
            var path = Path.Combine(directory, "settings.json");
            Check(!File.ReadAllText(path).Contains(irc.Password), "secret protected on disk");
            var reloaded = new GlobalSettingsStore(directory).Load().Irc!;
            Check(reloaded.Enabled && reloaded.Host == irc.Host && reloaded.Port == irc.Port && reloaded.Nick == irc.Nick && reloaded.Channel == irc.Channel, "connection survives restart without FTP authority");
            Check(reloaded.Password == irc.Password && reloaded.ZncUsername == irc.ZncUsername && reloaded.ZncNetwork == irc.ZncNetwork && reloaded.UseZnc, "ZNC settings survive restart");
            Check(reloaded.NetworkName == irc.NetworkName && reloaded.AccountLinks!.SequenceEqual(irc.AccountLinks!), "network and mappings persist");
            Check(new IrcSettings().AllowInvalidCertificate, "self-signed accepted by default");
            Check(!reloaded.AllowInvalidCertificate, "explicit strict certificate setting preserved");
            new GlobalSettingsStore(directory).Save(settings with { Irc = irc with { AllowInvalidCertificate = true } });
            Check(new GlobalSettingsStore(directory).Load().Irc!.AllowInvalidCertificate, "certificate exception survives restart");
            Check(!IrcService.AcceptCertificate(irc, System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors), "self-signed rejected by default");
            Check(IrcService.AcceptCertificate(irc with { AllowInvalidCertificate = true }, System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors), "self-signed accepted when selected");
            Check(IrcService.AcceptCertificate(irc, System.Net.Security.SslPolicyErrors.None), "valid certificate accepted");
            new GlobalSettingsStore(directory).Save(settings with { Irc = irc with { Enabled = false } });
            Check(new GlobalSettingsStore(directory).Load().Irc!.Host == irc.Host, "disabled IRC retains details");
            var blocked = Path.Combine(directory, "blocked"); File.WriteAllText(blocked, "x");
            var failed = false;
            try { new GlobalSettingsStore(blocked).Save(settings); } catch (IOException) { failed = true; }
            Check(failed, "save failure reaches dialog");
            Console.WriteLine($"PASS: {count} global settings persistence checks.");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
