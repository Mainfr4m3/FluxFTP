using IoFtp.Core.Abstractions;
using IoFtp.Desktop.Services;

internal static class VisionaryPayloadChecks
{
    public static void Run()
    {
        var count = 0;
        void Check(bool condition, string name) { count++; if (!condition) throw new Exception("VISIONARY payload: " + name); }
        var directory = Path.Combine(Path.GetTempPath(), "FluxFTP-payload-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var ini = Path.Combine(directory, "rushopt.ini");
            System.IO.File.WriteAllText(ini, """
                [detections]
                sample=.:/sample
                covers=.:/covers
                [sample]
                completeflag=(MP4|AVI|MPG|MPEG|VOB|MKV|JPG|JPEG|WMV)
                rushopt=RS_LOGIN or RS_EXPR
                fileallow=(JPG|MPG|MPEG|AVI|MKV|JPEG|WMV|VOB|MP4)
                fileskip=(NFO|SFV)
                retrycount=5
                [covers]
                completeflag=(JPG|JPEG|GIF|BMP)
                fileallow=*.jpg|*.jpeg|*.gif|*.bmp
                fileskip=bad*
                rushopt=RS_LOGIN
                retrycount=1
                """);
            var profiles = VisionaryTransferProfiles.Load(ini);
            Check(profiles.ForDirectory("/TV/Release/SaMpLe")?.Name == "sample", "sample case-insensitive detection");
            Check(profiles.ForDirectory("/TV/Release/samples") is null, "exact directory detection");
            var now = DateTimeOffset.UtcNow;
            var race = new VisionaryRaceState(new(RefreshSeconds: 1), profiles, () => now);
            var states = new Dictionary<Guid, string>();
            string? Status(Guid id) => states.GetValueOrDefault(id);
            RemoteEntry File(string name, long size, string folder = "sample") => new(name, "/TV/Release/" + folder + "/" + name, false, size, null);
            var video = File("sample.mkv", 10);
            var excluded = File("sample.mkv.nfo", 50);
            Check(race.AllowsFile(video) && !race.AllowsFile(excluded), "skip wins over allow");
            Check(!race.IsCompletionMarker(video), "payload is transferred, not discarded as a marker");
            race.InspectListing([video, excluded], true, "/TV/Release/sample");
            Check(!race.SourceComplete, "first sighting is not complete");
            var first = Guid.NewGuid(); race.Track(video.FullPath, 10, null, first); states[first] = "Completed";
            now = now.AddSeconds(1);
            video = video with { Size = 20 };
            race.InspectListing([video, excluded], true, "/TV/Release/sample");
            Check(!race.SourceComplete && race.NeedsTransfer(video.FullPath, 20, null, Status), "growth resets quiet period and queues updated file");
            var growing = Guid.NewGuid(); race.Track(video.FullPath, 20, null, growing); states[growing] = "Transferring";
            now = now.AddSeconds(2);
            race.InspectListing([video, excluded], true, "/TV/Release/sample");
            Check(race.SourceComplete && !race.Finished(Status), "stable candidate still waits for transfers");
            Check(!race.NeedsTransfer(video.FullPath, 20, null, Status), "no parallel duplicate of growing sample");
            states[growing] = "Completed";
            Check(race.NeedsTransfer(video.FullPath, 20, null, Status), "final copy required after pre-completion transfer");
            var final = Guid.NewGuid(); race.Track(video.FullPath, 20, null, final); states[final] = "Queued";
            Check(!race.Finished(Status), "queued final copy does not finish job");
            states[final] = "Completed";
            Check(race.Finished(Status), "stable sample and completed final copy finish sample-only job");
            now = now.AddSeconds(1);
            race.InspectListing([video, File("second.mp4", 3)], true, "/TV/Release/sample");
            Check(!race.SourceComplete && !race.Finished(Status), "new file reopens quiet period");
            race.InspectListing([], true, "/TV/Release/sample");
            Check(!race.SourceComplete, "empty sample is never complete");
            race.InspectListing([File("empty.mkv", 0)], true, "/TV/Release/sample"); now = now.AddSeconds(3);
            race.InspectListing([File("empty.mkv", 0)], true, "/TV/Release/sample");
            Check(!race.SourceComplete, "zero-byte sample never completes");
            var covers = new VisionaryRaceState(new(RefreshSeconds: 1), profiles, () => now);
            var jpg = File("front.JPG", 100, "covers");
            Check(covers.AllowsFile(jpg) && !covers.AllowsFile(File("bad-front.jpg", 100, "covers")), "covers wildcard allow/skip without RS_EXPR");
            covers.InspectListing([jpg], true, "/TV/Release/covers"); now = now.AddSeconds(2);
            covers.InspectListing([jpg], true, "/TV/Release/covers");
            Check(covers.SourceComplete, "nonempty image can complete covers");
            covers.InspectListing([jpg, File("back.jpg", 0, "covers")], true, "/TV/Release/covers"); now = now.AddSeconds(3);
            covers.InspectListing([jpg, File("back.jpg", 0, "covers")], true, "/TV/Release/covers");
            Check(!covers.SourceComplete, "zero-byte second image keeps covers open");
            covers.InspectListing([jpg], true, "/TV/Release/covers", _ => false); now = now.AddSeconds(3);
            covers.InspectListing([jpg], true, "/TV/Release/covers", _ => false);
            Check(!covers.SourceComplete, "globally skipped images cannot complete covers");
            var parent = new VisionaryRaceState(new(RefreshSeconds: 1), profiles, () => now);
            parent.InspectListing([], true, "/TV/Release");
            parent.InspectListing([jpg], false, "/TV/Release/covers"); now = now.AddSeconds(2);
            parent.InspectListing([jpg], false, "/TV/Release/covers");
            Check(!parent.SourceComplete, "covers cannot complete the parent release");
            var stopped = false;
            try { covers.InspectListing([jpg, File("REASON-bad", 0, "covers")], true, "/TV/Release/covers"); } catch (InvalidOperationException) { stopped = true; }
            Check(stopped, "nuke overrides payload completion");
            Check(profiles.ForDirectory("/x/sample")!.Attempts == 6 && profiles.ForDirectory("/x/covers")!.Attempts == 2, "profile retry counts loaded");
            foreach (var bad in new[] { "../sample", "Release/../sample", "/Release/sample", "Release//sample", "Release\\sample" })
            {
                var rejected = false;
                try { VisionaryTransferProfiles.ReleasePath(bad); } catch (ArgumentException) { rejected = true; }
                Check(rejected, "reject traversal/invalid release path");
            }
            Check(VisionaryTransferProfiles.ReleasePath("Release/Sample") == "Release/Sample", "subdirectory request accepted");
            System.IO.File.AppendAllText(ini, "\n[sample]\ncompleteflag=[\n");
            var invalid = false;
            try { VisionaryTransferProfiles.Load(ini); } catch (ArgumentException) { invalid = true; }
            Check(invalid, "bad profile regex rejected before racing");
            Console.WriteLine($"PASS: {count} VISIONARY sample/covers checks.");
        }
        finally { Directory.Delete(directory, true); }
    }
}
