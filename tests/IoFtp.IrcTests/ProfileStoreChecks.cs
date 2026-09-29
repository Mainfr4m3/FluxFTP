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
                " user %20+=; ", TransferProtocol.Ftp, " secret %20+=; ", Description: "line1\nline2");
            store.Save([profile]);
            var saved = File.ReadAllText(path);
            Check(saved.Contains("Name=Min sajt å %20"), "readable name");
            Check(saved.Contains("Username= user %20+=; "), "literal username");
            Check(!saved.Contains(profile.Password), "password protected");
            var loaded = store.Load().Single();
            Check(loaded.Username == profile.Username && loaded.Password == profile.Password && loaded.Description == profile.Description, "round trip");
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
            Console.WriteLine("ProfileStore: 13 checks passed.");
        }
        finally { Directory.Delete(directory, true); }
    }
}
