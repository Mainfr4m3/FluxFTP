using IoFtp.Core.Abstractions;
using IoFtp.Desktop.Services;

internal static class VisionaryRuleChecks
{
    public static void Run(string path)
    {
        var profiles = VisionaryTransferProfiles.Load(path);
        var count = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); count++; }
        RemoteEntry File(string folder, string name) => new(name, folder + "/" + name, false, 100, null);
        Check(profiles.ForDirectory("/R/Release/SaMpLe")!.Allows(File("/R/Release/SaMpLe", "test.MKV")), "RS_EXPR wildcard list must allow MKV ignoring case");
        Check(!profiles.ForDirectory("/R/Release/sample")!.Allows(File("/R/Release/sample", "test.sfv")), "wildcard fileskip excludes SFV");
        Check(profiles.ForDirectory("/R/Release/covers")!.Allows(File("/R/Release/covers", "front.JPG")), "covers wildcard list loads");
        Check(profiles.ForDirectory("/R/Release/proof")!.Name == "proof", "proof profile loaded");
        Check(profiles.ForDirectory("/R/Release/subs")!.MarkerBased, "subs uses COMPLETE markers");
        Check(profiles.ForDirectory("/R/Release/CD1")!.Name == "multicd", "multicd detection regex loaded");
        var flacPath = "/R/Band-Album-WEB-FLAC-2026-GROUP";
        var flac = profiles.ForDirectory(flacPath)!;
        Check(flac.Name == "FLAC", "FLAC takes precedence over generic year MP3 detection");
        Check(flac.Allows(File(flacPath, "track.FLAC")) && !flac.Allows(File(flacPath, "track.MP3")), "FLAC allow/skip applied");
        Check(profiles.ForDirectory("/R/Band-Album-WEB-2026-GROUP")!.Name == "MP3", "MP3 detection loaded");
        Check(profiles.ForDirectory("/R/Video-2026-MVHITS")!.Name == "MVID", "MVID detection precedes MP3");
        var normal = profiles.ForDirectory("/R/Show.S01E01-GROUP")!;
        Check(normal.Name == "normal" && normal.AllowsFolder("Sample") && !normal.AllowsFolder("Other"), "normal folderallow applied");
        var race = new VisionaryRaceState(new(), profiles);
        var marker = new RemoteEntry("[X] - (20F - COMPLETE )", flacPath + "/[X] - (20F - COMPLETE )", true, null, null);
        Check(race.IsCompletionMarker(marker), "imported release completeflag used");
        Check(!race.IsCompletionMarker(marker with { Name = "NUKE", FullPath = flacPath + "/NUKE" }), "NUKE remains a stop signal");
        race.BeginListing(); race.InspectListing([marker, File(flacPath, "track.flac")], true, flacPath); race.EndListing();
        Check(race.SourceComplete && race.IsFinal(flacPath + "/track.flac"), "marker-based FLAC finalized");
        Check(flac.Attempts == 4 && profiles.ForDirectory("/R/Release/sample")!.Attempts == 6, "imported retries applied");
        Console.WriteLine($"PASS: {count} imported VISIONARY rule checks using supplied INI.");
    }
}
