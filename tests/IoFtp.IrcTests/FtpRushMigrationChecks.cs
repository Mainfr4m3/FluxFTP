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
                <BOOKMARK><I CAPTION="TV-HD" REMOTE="/incoming/TV-HD/"/><I CAPTION="MP3" REMOTE="/MP3-Today/"/></BOOKMARK>
                </SITE><SITE NAME="SECOND" UID="two"><HOST>second.invalid</HOST>
                <BOOKMARK><I CAPTION="TV-HD" REMOTE="/TV/HD/"/></BOOKMARK></SITE></GROUP></SITES>
                """);
            var package = FtpRushSiteImporter.ImportPackage(xml);
            Check(package.Sites.Count == 2 && package.Bookmarks.Count == 3, "legacy site/bookmark import");
            var rows = FtpRushSectionMigration.Preview(package.Bookmarks);
            Check(rows.Count == 3 && rows.All(row => row.Selected), "site paths automatically selected");
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["PRiME"] = "PRiME", ["SECOND"] = "SECOND" };
            var merged = FtpRushSectionMigration.Merge([], rows, names, false);
            Check(merged.Count == 2 && merged.Single(s => s.Name == "TV-HD").SitePaths.Count == 2, "same section keeps independent site paths");
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
            Console.WriteLine($"PASS: {checks} FTPRush migration checks.");
        }
        finally { Directory.Delete(directory, true); }
    }
}
