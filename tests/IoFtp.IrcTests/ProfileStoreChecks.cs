using System.IO;
using IoFtp.Core.Models;
using IoFtp.Desktop.Services;

internal static class ProfileStoreChecks
{
    public static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FluxFTP-profile-test-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "FluxFTP-sites.ini");
        void Check(bool condition, string name) { if (!condition) throw new Exception("Profile: " + name); }
        try
        {
            var store = new ProfileStore(directory);
            var profile = new ConnectionProfile(Guid.NewGuid(), "Min sajt å %20", "localhost", 21,
                " user %20+=; ", TransferProtocol.Ftp, " secret %20+=; ", Description: "line1\nline2",
                Options: new SiteOptions(ImportedSkipRules: "01111111(?i)(TEST)\n00211111^SAMPLE$", ImportedPriorityRules: "1*.sfv", VisionaryRules: "[GLOBAL:SKIP]\nLIST=(TEST)"));
            store.Save([profile]);
            var saved = File.ReadAllText(path);
            Check(saved.Contains("Name=Min sajt å %20"), "readable name");
            Check(saved.Contains("Username= user %20+=; "), "literal username");
            Check(!saved.Contains(profile.Password), "password protected");
            var loaded = store.Load().Single();
            Check(loaded.Username == profile.Username && loaded.Password == profile.Password && loaded.Description == profile.Description, "round trip");
            Check(loaded.EffectiveOptions == profile.EffectiveOptions, "imported rules and slot defaults round trip");
            File.WriteAllLines(path, File.ReadAllLines(path).Select(line => line.StartsWith("Password=") ? "Password= manual %20+=; " : line).Append("Unknown=keep me"));
            Check(store.Load().Single().Password == " manual %20+=; ", "manual literal password");
            Check(File.ReadAllText(path).Contains("Password=dpapi:") && File.ReadAllText(path).Contains("Unknown=keep me"), "migration preserves unknown field");
            Check(store.Load().Single().Password == " manual %20+=; ", "migration survives restart");
            File.WriteAllText(path, $"; old file\n[site:{profile.Id}]\nName=Old%20site\nUsername=user%20name\nPassword=literal%20password\nProxyMode=Custom\nProxyPassword=proxy%20pass\n");
            loaded = store.Load().Single();
            Check(loaded.Name == "Old site" && loaded.Username == "user name", "legacy decode");
            Check(loaded.Password == "literal%20password" && loaded.Proxy?.Password == "proxy%20pass", "legacy manual passwords literal");
            Check(store.Load().Single().Proxy?.Password == "proxy%20pass", "proxy protection");
            saved = File.ReadAllText(path);
            Check(saved.Contains("Name=Old site") && !saved.Contains("Password=proxy%20pass"), "legacy converted");
            File.WriteAllText(path, saved.Replace("Name=Old site", "Name=@json:\"literal\""));
            Check(store.Load().Single().Name == "literal", "escaped field");
            File.WriteAllLines(path, File.ReadAllLines(path).Select(line => line.StartsWith("Password=") ? "Password=dpapi:invalid" : line));
            store.Load();
            Check(File.ReadAllText(path).Contains("Password=dpapi:invalid"), "unreadable secret retained");
            // Exercise the actual INI store, including a new store instance as after restart.
            foreach (var encoding in Enum.GetValues<FtpTextEncoding>())
            foreach (var auth in Enum.GetValues<FtpAuthCommand>())
            foreach (var enabled in new[] { false, true })
            {
                var options = profile.EffectiveOptions with { TextEncoding = encoding, AuthCommand = auth,
                    PassivePortMin = enabled ? 10000 : 0, PassivePortMax = enabled ? 20000 : 0, ForceBinaryMode = enabled, Utf8Login = enabled, PbszBeforeLogin = enabled, Tls12Only = enabled, UseOpenSslTls = enabled };
                store.Save([profile with { Options = options }]);
                Check(new ProfileStore(directory).Load().Single().EffectiveOptions == options, "TLS/encoding INI round trip");
            }
            var newKeys = new[] { "TextEncoding=", "Utf8Login=", "AuthCommand=", "PbszBeforeLogin=", "Tls12Only=" };
            File.WriteAllLines(path, File.ReadAllLines(path).Where(line => !newKeys.Any(key => line.StartsWith(key))));
            var oldOptions = new ProfileStore(directory).Load().Single().EffectiveOptions;
            Check(oldOptions.TextEncoding == FtpTextEncoding.Auto && oldOptions.AuthCommand == FtpAuthCommand.Auto &&
                !oldOptions.Utf8Login && !oldOptions.PbszBeforeLogin && !oldOptions.Tls12Only, "older INI compatibility defaults");
            var beforeFailure = File.ReadAllBytes(path);
            var attributes = File.GetAttributes(path);
            try
            {
                File.SetAttributes(path, attributes | FileAttributes.ReadOnly);
                try { store.Save([profile with { Name = "must not overwrite" }]); throw new Exception("Read-only save incorrectly succeeded"); }
                catch (IOException exception) { Check(exception.Message.Contains(path), "save error includes file path"); }
                Check(File.ReadAllBytes(path).SequenceEqual(beforeFailure), "failed save preserves previous sites");
                Check(!Directory.GetFiles(directory, "*.tmp").Any(), "failed save cleans temporary file");
            }
            finally { File.SetAttributes(path, attributes); }
            store.Save([profile]);
            Check(store.Load().Single().Name == profile.Name, "save recovers after permission fix");
            Console.WriteLine("ProfileStore: 37 checks passed, including read-only save failure, original file preservation and recovery.");
        }
        finally { Directory.Delete(directory, true); }
    }
}
