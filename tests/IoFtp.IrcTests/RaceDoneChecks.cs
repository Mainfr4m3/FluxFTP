using IoFtp.Core.Abstractions;
using IoFtp.Desktop.Services;

internal static class RaceDoneChecks
{
    public static void Run()
    {
        var count = 0;
        void Check(bool value, string name) { if (!value) throw new Exception(name); count++; }
        var now = DateTimeOffset.UtcNow;
        var signals = new RaceCompletionSignals(() => now);
        var routing = new IrcRouting([], [], [new IrcCatch(1, "RC", "Test", "#race", ["RC"], "DONE", ["DONE"], "SPORTS-1080P")]);
        var catcher = new IrcCatcher(onAnnouncement: entry => signals.Record(entry.Site, entry.Section, entry.Release));
        const string release = "MLB.Championship.Series.2026.Game.1.Cleveland.Guardians.vs.Tampa.Bay.Rays.1080p.WEB.h264-NiGHTNiNJAS";
        var message = "DONE: -TV-SPORTS- " + release + " Size 7287/MB - Speed 520.48MB/s - Time 14s";
        Check(catcher.Match(routing, "Test", "#race", "OtherBot", message, true).Count == 0, "unknown bot cannot finalize race");
        Check(catcher.Match(routing, "OtherNetwork", "#race", "RC", message, true).Count == 0, "wrong network ignored");
        Check(catcher.Match(routing, "Test", "#other", "RC", message, true).Count == 0, "wrong channel ignored");
        var matches = catcher.Match(routing, "Test", "#race", "RC", message, true);
        Check(matches.Count == 1 && matches[0].Release == release, "real RC DONE release parsed");
        Check(signals.Has("rc", "sports-1080p", release), "explicit Flux section mapping accepted");
        Check(!signals.Has("LFG", "SPORTS-1080P", release), "source DONE does not imply destination DONE");
        Check(!signals.Has("RC", "TV-SPORTS", release), "unmapped section cannot finalize another watch");
        Check(!signals.Has("RC", "SPORTS-1080P", "Another.Release-GROUP"), "wrong release ignored");
        RemoteEntry F(string name, long size = 100) => new(name, "/release/" + name, false, size, null);
        var expected = new[] { F("test.sfv"), F("test.rar") };
        var race = new VisionaryRaceState(new()) { SourceAnnouncedComplete = true };
        race.BeginListing(); race.InspectListing(expected, true, "/release"); race.EndListing();
        Check(race.SourceComplete, "source DONE substitutes for FTP marker");
        Check(!RaceDestinationVerifier.Accepts(expected, expected, race), "source DONE alone never verifies destination");
        race.DestinationAnnouncedComplete = true;
        Check(RaceDestinationVerifier.Accepts(expected, expected, race), "destination DONE still verifies all expected sizes");
        Check(!RaceDestinationVerifier.Accepts(expected, [F("test.sfv")], race), "destination DONE cannot hide missing files");
        Check(!RaceDestinationVerifier.Accepts(expected, [F("test.sfv"), F("test.rar", 50)], race), "destination DONE cannot hide partial size");
        Check(!RaceDestinationVerifier.Accepts(expected, [.. expected, F("test.rar-MISSING", 0)], race), "DONE cannot hide missing markers");
        var progress = new RemoteEntry("15% Complete", "/release/15% Complete", true, null, null);
        Check(!RaceDestinationVerifier.HasCompleteMarker([.. expected, progress], race), "DONE cannot override progress marker");
        race.BeginListing(); race.InspectListing([], true, "/release"); race.EndListing();
        Check(!race.SourceComplete, "DONE cannot finalize empty source listing");
        race.BeginListing(); race.InspectListing([F("test.rar", 0)], true, "/release"); race.EndListing();
        Check(!race.SourceComplete, "DONE cannot finalize zero-byte source payload");
        var nuked = false;
        try { race.InspectListing([.. expected, F("REASON-bad", 0)], true, "/release"); } catch (InvalidOperationException) { nuked = true; }
        Check(nuked, "NUKE/REASON overrides DONE");
        now = now.AddMinutes(10);
        Check(!signals.Has("RC", "SPORTS-1080P", release), "old DONE announcements expire");
        Console.WriteLine($"PASS: {count} DONE announcement scope, parsing, expiry and payload verification checks.");
    }
}
