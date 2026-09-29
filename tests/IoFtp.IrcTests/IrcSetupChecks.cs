using IoFtp.Core.Models;
using IoFtp.Desktop.Services;

internal static class IrcSetupChecks
{
    public static void Run()
    {
        var count = 0;
        void Check(bool value, string name) { if (!value) throw new Exception("FAIL setup: " + name); count++; }
        var directory = Path.Combine(AppContext.BaseDirectory, "setup-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var authority = new ConnectionProfile(Guid.NewGuid(), "Authority", "auth.example", 21, "operator", TransferProtocol.FtpsExplicit);
            var profiles = new List<ConnectionProfile> { authority };
            var saves = 0;
            var store = new SiteRuleStore(directory);
            var commands = new IrcSetupCommands(() => profiles, items => { profiles = items.ToList(); saves++; }, store, authority.Id);
            string Run(string command) => commands.Execute(command);
            Check(Run("!addsite Test user secret ftp.example:6999").StartsWith("OK:"), "add site");
            Check(profiles[1].Protocol == TransferProtocol.FtpsExplicit && profiles[1].Port == 6999 && profiles[1].Password == "secret", "site profile built correctly");
            Check(!Run("!site Test").Contains("secret"), "site summary does not disclose password");
            Check(Run("!addsite Test other other ftp.other:21").StartsWith("ERROR:") && profiles.Count == 2, "duplicate does not overwrite credentials");
            Check(Run("!addsite \"Space Site\" user \"space password\" [::1]:990 implicit").StartsWith("OK:"), "quoted fields and IPv6");
            Check(profiles[2].Password == "space password" && profiles[2].Protocol == TransferProtocol.FtpsImplicit, "quotes preserved correctly");
            Check(Run("!slots Test 5").StartsWith("OK:") && profiles[1].EffectiveOptions.MaxSlots == 5, "slots saved");
            Check(Run("!maxupdn Test 5 0").StartsWith("OK:") && profiles[1].EffectiveOptions.MaxDownloadSlots == 0, "upload/download limits");
            Check(Run("!slots Test 2").StartsWith("OK:") && profiles[1].EffectiveOptions.MaxUploadSlots == 2, "slot reduction clamps directional limits");
            var previous = saves;
            Check(Run("!maxupdn Test 3 1").StartsWith("ERROR:") && saves == previous, "invalid limits do not save");
            Check(Run("!slots Test -1").StartsWith("ERROR:") && saves == previous, "negative slots denied");
            Check(Run("!maxidle Test 15").StartsWith("OK:") && profiles[1].EffectiveOptions.MaxIdleSeconds == 15, "idle saved");
            Check(Run("!maxidle Test 0 15").StartsWith("ERROR:"), "unsupported two-value slftp syntax explained");
            Check(Run("!tls Test implicit").StartsWith("OK:") && profiles[1].Protocol == TransferProtocol.FtpsImplicit, "TLS setting");
            Check(Run("!tls Authority off").StartsWith("ERROR:") && profiles[0].Protocol == TransferProtocol.FtpsExplicit, "authority TLS protected");
            Check(Run("!setaffils Test GROUP1 GROUP2").StartsWith("OK:") && profiles[1].EffectiveOptions.Affils == "GROUP1 GROUP2", "affiliations saved");
            Check(Run("!ruleadd Test * if group in BLOCKED then drop").StartsWith("OK:"), "global rules can precede setdir");
            Check(Run("!setdir Test MP3 /MP3/").StartsWith("OK:") && store.ResolvePath("Test", "MP3") == "/MP3/", "section path persisted");
            Check(Run(@"!ruleadd Test MP3 if releasename =~ /\.AUDIOBOOK\./i then DROP").StartsWith("OK:"), "regex rule added");
            Check(Run("!ruleadd Test MP3 if default then ALLOW").StartsWith("OK:"), "default rule added");
            Check(!store.Evaluate("Test", "MP3", "Album.AUDIOBOOK.2026-GROUP").Accepted, "saved regex enforced");
            Check(store.Evaluate("Test", "MP3", "Album.2026-GROUP").Accepted, "saved allow enforced");
            Check(!store.Evaluate("Test", "MP3", "Album.2026-BLOCKED").Accepted, "global group deny enforced");
            Check(Run("!setdir Test MP3 /NewMP3/").StartsWith("OK:") && !store.Evaluate("Test", "MP3", "Album.AUDIOBOOK.2026-GROUP").Accepted, "setdir preserves existing rules");
            Check(Directory.GetFiles(directory, "*.rules.json").Length == 1, "one file per site");
            var before = File.ReadAllText(Directory.GetFiles(directory, "*.rules.json")[0]);
            Check(Run("!ruleadd Test MP3 if not imdblookupdone then DROP").StartsWith("ERROR:"), "metadata unsupported explicitly");
            Check(Run("!ruleadd Test MP3 if releasename =~ /(/i then DROP").StartsWith("ERROR:"), "malformed regex rejected");
            Check(Run("!setdir Test MP3 /../invalid").StartsWith("ERROR:"), "invalid path rejected");
            Check(File.ReadAllText(Directory.GetFiles(directory, "*.rules.json")[0]) == before, "failed rule edits leave file unchanged");
            Check(Run("!sslmethod Test 2").StartsWith("ERROR:"), "unknown numeric TLS mapping not guessed");
            Check(Run("!ircchanblow Test #channel secretkey").StartsWith("ERROR:"), "unsupported IRC encryption not silently accepted");
            Check(Run("!slots Missing 2").StartsWith("ERROR:"), "unknown site denied");
            Check(Run("!addsite Invalid user secret ftp.example:invalid").StartsWith("ERROR:"), "invalid endpoint denied");
            Check(Run("!addsite Test2 user \"unterminated ftp.example:21").StartsWith("ERROR:"), "unclosed quote denied");
            Check(Run("!slots Test 2\r\n!slots Test 3").StartsWith("ERROR:"), "multiline injection rejected");
            Check(Run("!siterules Test").StartsWith("OK:") && Run("!sites").Contains("Test"), "readback commands");
            var script = File.ReadAllLines(Path.GetFullPath("../../../../../Rules/_site/ExampleSite.irc-setup.txt.example", AppContext.BaseDirectory));
            foreach (var line in script.Where(line => line.StartsWith('!')))
                Check(Run(line).StartsWith("OK:"), "generated setup example line");
            var exampleSite = store.Load().Single(f => f.Site == "ExampleSite");
            Check(exampleSite.Sections.Length == 2 && exampleSite.Sections.All(s => !s.RequiresMetadata), "entire IRC setup creates two sections without metadata");
            Check(store.Evaluate("ExampleSite", "ARCHIVE", "Example.Release-GROUP").Accepted, "IRC setup permits name-compliant TV without metadata");
            Check(!store.Evaluate("ExampleSite", "RELEASES", "Example.tmp").Accepted, "IRC setup still enforces filename rule");
            Console.WriteLine($"PASS: {count} IRC setup checks.");
        }
        finally { Directory.Delete(directory, true); }
    }
}
