using System.Text.Json;
using IoFtp.Desktop.Services;

internal static class SiteRuleChecks
{
    public static void Run()
    {
        var count = 0;
        void Check(bool value, string description) { if (!value) throw new Exception("FAIL rules: " + description); count++; }
        var directory = Path.Combine(AppContext.BaseDirectory, "rules-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new SiteRuleStore(directory);
            Check(store.EvaluateDestination("Test", "/anything/file").Accepted, "no files preserves behavior");
            var file = Path.Combine(directory, "Test.rules.json");
            SiteRule[] global = [new("release", "regex", [@"\.DUBBED\."], Reason: "No dubbed"), new("group", "in", ["BlockedGroup"])];
            var rules = new SiteRuleFile(1, "Test", global,
            [
                new("MP3", "/MP3/", [new("release", "regex", ["AUDIOBOOK"])]),
                new("VIDEO", "/VIDEO", [new("release", "regex", ["720p|1080p"], Negate: true), new("tag", "glob", ["*banned*"])]),
                new("TV", "/TV", [], RequiresMetadata: true),
                new("STRICT", "/Shared", [], "drop"),
                new("OPEN", "/Shared", [], "allow"),
                new("ORDER", "/Order", [new(Match: "glob", Values: ["Allowed*"], Action: "allow"), new(Match: "always")]),
                new("NESTED", "/Shared/Nested", [])
            ]);
            void Save(SiteRuleFile value) => File.WriteAllText(file, JsonSerializer.Serialize(value));
            Save(rules);
            Check(store.Load().Count == 1, "load file");
            Check(store.ResolvePath("test", "mp3") == "/MP3/", "case-insensitive site and section mapping");
            Check(store.Evaluate("Test", "MP3", "Album-GROUP").Accepted, "section default allow");
            Check(!store.Evaluate("Test", "RELEASES", "Example.tmp").Accepted, "section regex drop");
            Check(!store.Evaluate("Test", "MP3", "Album.DUBBED.2026-GROUP").Accepted, "global drop precedence");
            Check(!store.Evaluate("Test", "MP3", "Album-BlockedGroup").Accepted, "group match");
            Check(store.Evaluate("Test", "MP3", "BlockedGroup-Other").Accepted, "group extracted after final hyphen");
            Check(!store.Evaluate("Test", "VIDEO", "Movie.480p-GROUP").Accepted, "negated regex");
            Check(store.Evaluate("Test", "VIDEO", "Movie.1080p-GROUP").Accepted, "required regex allow");
            Check(!store.Evaluate("Test", "VIDEO", "Movie.BannedTag.1080p-GROUP").Accepted, "tag wildcard");
            Check(!store.Evaluate("Test", "TV", "Show.S01E01-GROUP").Accepted, "missing metadata fails closed");
            Check(store.Evaluate("Test", "ORDER", "Allowed.Release-GROUP").Accepted, "ordered first match");
            Check(!store.Evaluate("Test", "ORDER", "Other.Release-GROUP").Accepted, "always drop");
            Check(!store.Evaluate("Test", "missing", "Release-GROUP").Accepted, "unknown section blocked");
            Check(store.EvaluateDestination("Test", "/MP3/Album-GROUP/CD1/file.mp3").Accepted, "nested file checks release");
            Check(!store.EvaluateDestination("Test", "/MP3/Album-BlockedGroup/CD1/file.mp3").Accepted, "nested file cannot bypass group rule");
            Check(!store.EvaluateDestination("Test", "/MP3-other/Album/file").Accepted, "path boundary");
            Check(!store.EvaluateDestination("Test", "/MP3/../VIDEO/file").Accepted, "traversal rejected");
            Check(!store.EvaluateDestination("Test", "/Shared/Release/file").Accepted, "ambiguous alias applies all rules");
            Check(store.EvaluateDestination("Test", "/Shared/Release/file", "OPEN", "Release").Accepted, "explicit API alias");
            Check(!store.EvaluateDestination("Test", "/Shared/Other/file", "OPEN", "Release").Accepted, "persisted release mismatch blocked");
            Check(store.EvaluateDestination("Test", "/Shared/Nested/Release/file").Accepted, "longest path wins");
            Check(!store.EvaluateDestination("Test", "/mp3/Album/file").Accepted, "FTP path case preserved");
            Save(rules with { Rules = [new(Match: "always")] });
            Check(!store.Evaluate("Test", "MP3", "Album-GROUP").Accepted, "edited files reloaded");
            Save(rules with { Rules = [new(Match: "regex", Values: ["("])] });
            Check(!store.Evaluate("Test", "MP3", "Album-GROUP").Accepted, "bad regex blocks");
            Save(rules with { Rules = [new(Field: "imdbgenre", Values: ["Sport"])] });
            Check(!store.Evaluate("Test", "MP3", "Album-GROUP").Accepted, "unsupported metadata field blocks");
            File.WriteAllText(file, "{\"version\":1,\"site\":\"Test\",\"typo\":true}");
            Check(!store.Evaluate("Test", "MP3", "Album-GROUP").Accepted, "unknown fields block");
            Save(rules);
            File.Copy(file, Path.Combine(directory, "duplicate.rules.json"));
            Check(!store.Evaluate("Test", "MP3", "Album-GROUP").Accepted, "duplicate site blocks");
            File.Delete(Path.Combine(directory, "duplicate.rules.json"));
            File.Copy(Path.GetFullPath("../../../../../Rules/_site/ExampleSite.rules.json.example", AppContext.BaseDirectory), file, true);
            var converted = store.Load().Single();
            Check(converted.Sections.Length == 2 && converted.Sections.All(s => !s.RequiresMetadata), "complete sanitized sample loads");
            Check(store.Evaluate("ExampleSite", "ARCHIVE", "Example.Release-GROUP").Accepted, "sample FLAC allow");
            Check(!store.Evaluate("ExampleSite", "RELEASES", "Example.tmp").Accepted, "sample audiobook drop");
            Check(store.Evaluate("ExampleSite", "ARCHIVE", "Example.Release-GROUP").Accepted, "sample TV does not need metadata");
            Console.WriteLine($"PASS: {count} site-rule checks.");
        }
        finally { Directory.Delete(directory, true); }
    }
}
