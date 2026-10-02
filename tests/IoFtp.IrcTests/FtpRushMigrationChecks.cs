using IoFtp.Desktop.Services;

internal static class FtpRushMigrationChecks
{
    public static void Run()
    {
        var checks = 0;
        void Check(bool value, string name) { checks++; if (!value) throw new Exception("Rush migration: " + name); }
        var directory = Path.Combine(Path.GetTempPath(), "FluxFTP-rush-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var xml = Path.Combine(directory, "RushSite.xml");
            File.WriteAllText(xml, """
                <SITES><GROUP NAME="Group"><SITE NAME="PRiME" UID="one"><HOST>example.invalid</HOST><USER>test</USER>
                <SKIP><I>01111111(?i)(TVMAZE|IMDB)</I></SKIP><PRIO><I>1*.sfv</I></PRIO>
                <BOOKMARK><I CAPTION="TV-HD" REMOTE="/incoming/TV-HD/"/><I CAPTION="MP3" REMOTE="/MP3-Today/"/></BOOKMARK>
                </SITE><SITE NAME="SECOND" UID="two"><HOST>second.invalid</HOST>
                <BOOKMARK><I CAPTION="TV-HD" REMOTE="/TV/HD/"/></BOOKMARK></SITE></GROUP></SITES>
                """);
            var package = FtpRushSiteImporter.ImportPackage(xml);
            Check(package.Sites.Count == 2 && package.Bookmarks.Count == 3, "legacy site/bookmark import");
            var tlsXml = Path.Combine(directory, "tls.xml");
            foreach (var (mode, expected) in new[] {
                ("3", IoFtp.Core.Models.TransferProtocol.FtpsExplicit),
                ("2", IoFtp.Core.Models.TransferProtocol.FtpsExplicit),
                ("AUTH SSL", IoFtp.Core.Models.TransferProtocol.FtpsExplicit),
                ("AUTH TLS", IoFtp.Core.Models.TransferProtocol.FtpsExplicit),
                ("1", IoFtp.Core.Models.TransferProtocol.FtpsExplicit),
                ("0", IoFtp.Core.Models.TransferProtocol.FtpsExplicit) })
            {
                File.WriteAllText(tlsXml, $"<SITES><SITE NAME=\"TEST\"><HOST>example.invalid</HOST><PORT>2121</PORT><SSL ONDATA=\"True\" LIST=\"True\">\n {mode}\n</SSL></SITE></SITES>");
                var importedProfile = FtpRushSiteImporter.Import(tlsXml).Single().Profile;
                Check(importedProfile.Protocol == expected && importedProfile.ListingMode == IoFtp.Core.Models.DirectoryListingMode.Auto, "Rush imports as AUTH TLS and Auto");
            }
            var defaults = package.Sites[0].Profile.EffectiveOptions;
            Check(defaults.ImportedSkipRules == "01111111(?i)(TVMAZE|IMDB)" && defaults.ImportedPriorityRules == "1*.sfv", "Rush skip flags and priority rules retained verbatim");
            Check(ImportedTransferSettings.PriorityRank("release.sfv", [package.Sites[0].Profile], 5) == 0 &&
                ImportedTransferSettings.PriorityRank("release.rar", [package.Sites[0].Profile], 5) > 0, "imported SFV priority applied");
            Check(defaults.MaxSlots == 4 && defaults.MaxUploadSlots == 2 && defaults.MaxDownloadSlots == 2, "Rush default slots 4/2/2");
            var oldProfile = package.Sites[0].Profile with { Password = "old", Description = "keep" };
            var newProfile = oldProfile with { Password = "new", Description = "incoming" };
            Check(FtpRushSiteImporter.MergeProfile(oldProfile, newProfile, false, true) is { Password: "new", Description: "keep" }, "explicit password update works with Skip existing");
            Check(FtpRushSiteImporter.MergeProfile(oldProfile, newProfile, false, false).Password == "old", "unchecked password update preserves old secret");
            Check(FtpRushSiteImporter.MergeProfile(oldProfile, newProfile with { Password = "" }, true, true).Password == "old", "empty incoming password never erases secret");
            var passwordFile = Path.Combine(directory, "passwords.txt");
            File.WriteAllText(passwordFile, "# Example\nprime= secret=with=equals \nSECOND=second-secret\n");
            var passwords = FtpRushPasswordFile.Read(passwordFile, package.Sites.Select(site => site.Profile));
            Check(passwords["PRiME"] == " secret=with=equals " && passwords.Count == 2, "password match ignores site case and preserves password exactly");
            foreach (var invalid in new[] { "PRiME=", "UNKNOWN=private-value", "PRiME=one\nprime=two", "private-value-without-separator", "PRiME=secret\tvalue" })
            {
                File.WriteAllText(passwordFile, invalid);
                var rejected = false;
                try { FtpRushPasswordFile.Read(passwordFile, package.Sites.Select(site => site.Profile)); }
                catch (InvalidDataException ex) { rejected = !ex.Message.Contains("private-value") && !ex.Message.Contains("secret"); }
                Check(rejected, "invalid password row rejected without exposing secrets");
            }
            File.WriteAllText(passwordFile, "PRiME=example");
            var duplicateRejected = false;
            try { FtpRushPasswordFile.Read(passwordFile, [package.Sites[0].Profile, package.Sites[0].Profile]); }
            catch (InvalidDataException) { duplicateRejected = true; }
            Check(duplicateRejected, "ambiguous password site rejected");
            File.WriteAllText(passwordFile, "ftp://test:p%40ss%3Aword%25@example.invalid:21\n");
            Check(FtpRushPasswordFile.Read(passwordFile, package.Sites.Select(site => site.Profile))["PRiME"] == "p@ss:word%", "FTP URL matches endpoint and decodes credentials");
            File.WriteAllText(passwordFile, "ftp://test:raw@pass:word=ok@example.invalid\n");
            Check(FtpRushPasswordFile.Read(passwordFile, package.Sites.Select(site => site.Profile))["PRiME"] == "raw@pass:word=ok", "FTP URL supports default port and raw password delimiters");
            foreach (var invalidUrl in new[] { "ftp://test:private-value@example.invalid:2121", "ftp://wrong:private-value@example.invalid:21", "ftp://test:private-value@unknown.invalid:21", "ftp://test:private-value@example.invalid:21/path", "ftp://test@example.invalid:21" })
            {
                File.WriteAllText(passwordFile, invalidUrl);
                var rejected = false;
                try { FtpRushPasswordFile.Read(passwordFile, package.Sites.Select(site => site.Profile)); }
                catch (InvalidDataException ex) { rejected = !ex.Message.Contains("private-value"); }
                Check(rejected, "unmatched or malformed FTP URL rejected without password disclosure");
            }
            var rows = FtpRushSectionMigration.Preview(package.Bookmarks);
            Check(rows.Count == 3 && rows.All(row => row.Selected), "site paths automatically selected");
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["PRiME"] = "PRiME", ["SECOND"] = "SECOND" };
            var merged = FtpRushSectionMigration.Merge([], rows, names, false);
            Check(merged.Count == 2 && merged.Single(s => s.Name == "TV-HD").SitePaths.Count == 2, "same section keeps independent site paths");
            var direct = new SectionStore().Import(xml);
            Check(direct.Count == 2 && direct.Single(s => s.Name == "TV-HD").SitePaths["SECOND"] == "/TV/HD/", "Sections XML import preserves Rush site identities");
            var existing = new SectionDefinition("TV-HD", new() { ["prime"] = "/old" }, 4, "re:HD", "re:BAD", SectionValidationMode.Block);
            var kept = FtpRushSectionMigration.Merge([existing], rows, names, false).Single(s => s.Name == "TV-HD");
            Check(kept.SitePaths["PRiME"] == "/old" && kept.Hotkey == 4 && kept.DenyPatterns == "re:BAD" && kept.ValidationMode == SectionValidationMode.Block, "skip preserves path and rules");
            var replaced = FtpRushSectionMigration.Merge([existing], rows, names, true).Single(s => s.Name == "TV-HD");
            Check(replaced.SitePaths["PRiME"] == "/incoming/TV-HD/" && replaced.AllowPatterns == "re:HD", "replace changes path but preserves precheck");
            Check(existing.SitePaths["prime"] == "/old", "merge does not mutate originals");
            var special = FtpRushSectionMigration.Preview([new("TV", "/one", "A"), new("TV", "/two", "A"), new("Local", "/C:/Desktop"), new("Global", "/MP3")]);
            Check(special.All(row => !row.Selected), "conflicts, local and global bookmarks require review");
            Check(special.Single(row => row.Section == "TV").Path == "", "conflicting path never chosen silently");
            Check(!FtpRushSectionMigration.IsRemotePath("/a/../b") && !FtpRushSectionMigration.IsRemotePath("relative"), "invalid paths rejected");
            var failed = false;
            try { FtpRushSectionMigration.Merge([], rows, new Dictionary<string, string>(), false); } catch (InvalidDataException) { failed = true; }
            Check(failed, "missing site rejected before write");
            var json = Path.Combine(directory, "site.json");
            File.WriteAllText(json, """{"RootItem":{"Name":"root","Children":[{"Server":{"Name":"PRiME","Host":"test.invalid","BookMarks":[{"Name":"TV","Path":"/TV/"}]}}]}}""");
            Check(FtpRushSectionMigration.Preview(FtpRushSiteImporter.ImportPackage(json).Bookmarks).Single().Path == "/TV/", "modern JSON sections supported");
            var config = Path.Combine(directory, "FluxFTP-sections.json"); File.WriteAllText(config, "original");
            failed = false;
            try
            {
                ImportBackup.Commit(directory, ["FluxFTP-sections.json", "new.json"], () =>
                { File.WriteAllText(config, "changed"); File.WriteAllText(Path.Combine(directory, "new.json"), "new"); throw new IOException("injected failure"); });
            }
            catch (IOException) { failed = true; }
            Check(failed && File.ReadAllText(config) == "original" && !File.Exists(Path.Combine(directory, "new.json")), "failed import rolls back exact originals and new files");
            var root = Path.Combine(directory, "User_Files"); Directory.CreateDirectory(Path.Combine(root, "inifiles"));
            File.WriteAllText(Path.Combine(root, "inifiles", "PRiME.ini"), "[TV]\nskiplist=(BAD)\ntradefrom=SECOND\nimdb=1\n");
            File.WriteAllText(Path.Combine(root, "section.cha"), "[GLOBAL]\n(?i)TV=TV\n");
            var inventory = VisionaryImportInventory.ReadFolder(directory);
            Check(inventory.Count == 2 && inventory.Sum(file => file.Settings) == 4, "all rule types inventoried without lossy conversion");
            var rules = Path.Combine(root, "RULES"); Directory.CreateDirectory(rules);
            File.WriteAllText(Path.Combine(rules, "PRiME.ini"), "[TV]\nsiteaffils=GROUP1 GROUP2\nskiplist=(BAD)\nslotsup=3\nslotsdn=3\n");
            File.WriteAllText(Path.Combine(root, "options.ini"), "[SKIP]\nLIST=(TEST)\nGROUP=(-BAD)$\n");
            File.WriteAllText(Path.Combine(root, "rushopt.ini"), "[sample]\nfileskip=*.nfo;*.sfv\n");
            var fullInventory = VisionaryImportInventory.ReadFolder(root);
            Check(fullInventory.Any(file => file.Path == Path.Combine(rules, "PRiME.ini")), "RULES site INI discovered");
            var migrated = VisionarySiteMigration.Apply(package.Sites[0].Profile, fullInventory.Select(file => file.Path));
            Check(migrated.EffectiveOptions.Affils == "GROUP1 GROUP2" && migrated.EffectiveOptions.VisionaryRules.Contains("GROUP=(-BAD)$") && migrated.EffectiveOptions.VisionaryRules.Contains("fileskip=*.nfo;*.sfv"), "site affils, global skips and rushopt preserved");
            Check(VisionarySiteMigration.Apply(package.Sites[1].Profile, fullInventory.Select(file => file.Path)).EffectiveOptions.Affils == "", "affils do not leak to other sites");
            var rulePath = Path.Combine(rules, "TEST.txt");
            var original = new byte[] { 0x52, 0xe4, 0x64, 13, 10 };
            File.WriteAllBytes(rulePath, original);
            Check(VisionaryRuleText.ResolveFolder(directory) == rules && VisionaryRuleText.ResolveFolder(root) == rules && VisionaryRuleText.ResolveFolder(rules) == rules, "rule folder resolution");
            var text = new VisionaryRuleText(rulePath);
            Check(text.Text == "Räd\r\n", "legacy rule text encoding");
            text.Save("Räd edited\r\n");
            Check(File.ReadAllBytes(Directory.GetFiles(rules, "*.bak").Single()).SequenceEqual(original), "rule edit creates exact backup");
            Check(File.ReadAllBytes(rulePath)[1] == 0xe4, "rule edit preserves encoding");
            File.WriteAllText(rulePath, "external edit");
            failed = false;
            try { text.Save("overwrite"); } catch (IOException) { failed = true; }
            Check(failed && File.ReadAllText(rulePath) == "external edit", "external edits protected");
            Console.WriteLine($"PASS: {checks} FTPRush migration checks.");
        }
        finally { Directory.Delete(directory, true); }
    }
}
