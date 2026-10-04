using IoFtp.Core.Abstractions;
using IoFtp.Desktop.Services;

internal static class VisionaryRaceChecks
{
    public static void Run()
    {
        var checks = 0;
        void Check(bool condition, string name) { checks++; if (!condition) throw new Exception("VISIONARY race: " + name); }
        var race = new VisionaryRaceState(new());
        var states = new Dictionary<Guid, string>();
        string? Status(Guid id) => states.GetValueOrDefault(id);
        Guid Queue(long size)
        {
            var id = Guid.NewGuid(); states[id] = "Queued"; race.Track("/release/a.rar", size, null, id); return id;
        }
        Check(race.NeedsTransfer("/release/a.rar", 10, null, Status), "new file is queued immediately");
        var first = Queue(10);
        Check(!race.NeedsTransfer("/release/a.rar", 20, null, Status), "growth cannot overlap an active transfer");
        states[first] = "Completed";
        Check(race.NeedsTransfer("/release/a.rar", 20, null, Status), "growth after completion requires transfer");
        var second = Queue(20); states[second] = "Completed";
        Check(!race.NeedsTransfer("/release/a.rar", 20, null, Status), "stable file is not repeated during race");
        Check(!race.Finished(Status), "empty/missing marker never ends race");
        RemoteEntry Marker(string name) => new(name, "/release/" + name, false, 0, null);
        Check(VisionaryRaceState.IsVerificationMetadata(Marker("release.r00-MISSING")), "hyphen missing marker is not payload");
        Check(!VisionaryRaceState.IsVerificationMetadata(Marker("release.r00")), "zero-byte growing payload remains eligible");
        race.InspectListing([Marker("COMPLETE"), Marker("release.r00-MISSING")], true);
        Check(!race.SourceComplete, "hyphen missing marker prevents completion");
        race.InspectListing([Marker("[F - COMPLETE]")], true);
        Check(race.SourceComplete, "complete marker recognized");
        Check(race.NeedsTransfer("/release/a.rar", 20, null, Status), "preallocated file recopied after marker even at same size");
        var final = Queue(20);
        Check(!race.Finished(Status), "marker does not finish running transfers");
        states[final] = "Completed";
        Check(race.Finished(Status), "final transfer and marker finish race");
        race.InspectListing([Marker("INCOMPLETE")], true);
        Check(!race.SourceComplete, "incomplete is not complete");
        race.InspectListing([Marker("[100%]")], true);
        Check(race.SourceComplete, "percent marker recognized");
        Check(!race.IsCompletionMarker(new("Movie.COMPLETE.BLURAY.rar", "/release/movie.rar", false, 1000, null)), "payload names cannot be completion markers");
        foreach (var name in new[] { "NUKE", "[NUKED]-reason", "REASON-bad" })
        {
            var stopped = false;
            try { race.InspectListing([Marker("COMPLETE"), Marker(name)], true); } catch (InvalidOperationException) { stopped = true; }
            Check(stopped, "nuke overrides completion: " + name);
        }
        states.Remove(final);
        var cancelled = false;
        try { race.NeedsTransfer("/release/a.rar", 20, null, Status); } catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled, "removed job is not recreated");
        foreach (var pattern in new[] { "", "(|COMPLETE)", "[" })
        {
            var invalid = false;
            try { new VisionaryRaceOptions(pattern).Validate(); } catch (ArgumentException) { invalid = true; }
            Check(invalid, "invalid/empty-matching regex rejected");
        }
        var retry = new VisionaryRaceState(new()) { SourceComplete = true };
        for (var i = 0; i < 3; i++)
        {
            Check(retry.NeedsTransfer("x", 10, null, Status), "bounded retry");
            var id = Guid.NewGuid(); states[id] = "Failed"; retry.Track("x", 10, null, id);
        }
        var exhausted = false;
        try { retry.NeedsTransfer("x", 10, null, Status); } catch (IOException) { exhausted = true; }
        Check(exhausted, "failed file does not retry forever");
        Console.WriteLine($"PASS: {checks} VISIONARY growing-release race checks.");
    }
}
