using IoFtp.Core.Abstractions;
using IoFtp.Core.Transport;
using IoFtp.Desktop.Services;

internal static class RaceFlowChecks
{
    public static async Task Run()
    {
        var checks = 0;
        void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
        var smartRace = new VisionaryRaceState(new());
        Check(smartRace.DeferSmallerSource("/r/f", 50, 100, true, 2), "smart initial wait");
        Check(smartRace.DeferSmallerSource("/r/f", 80, 100, true, 2), "smart first update wait");
        Check(!smartRace.DeferSmallerSource("/r/f", 90, 100, true, 2), "smart second update exhausts checks");
        Check(!smartRace.DeferSmallerSource("/r/g", 100, 100, true, 3), "equal sizes follow target policy");
        Check(!smartRace.DeferSmallerSource("/r/g", 50, 100, false, 3), "disabled smart mode preserves skip");
        var prequeueRace = new VisionaryRaceState(new());
        prequeueRace.SkipExistingTarget("/release/file.rar");
        Check(!prequeueRace.NeedsTransfer("/release/file.rar", 100, null, _ => null), "existing target excluded before creating job");
        Check(!prequeueRace.NeedsTransfer("/release/file.rar", 200, DateTimeOffset.UtcNow, _ => null), "existing target remains skipped as source grows");
        Check(prequeueRace.NeedsTransfer("/release/missing.rar", 100, null, _ => null), "missing target still eligible");
        var profileFolder = Path.Combine(Path.GetTempPath(), "Flux-profile-import-" + Guid.NewGuid());
        Directory.CreateDirectory(profileFolder);
        try
        {
            var original = Path.Combine(profileFolder, "rushopt.ini");
            File.WriteAllText(original, "[normal]\nfileallow=\\.rar$\nfileskip=blocked\nrushopt=RS_EXPR\ncompleteflag=COMPLETE\nretrycount=4\n");
            var imported = new VisionaryRaceOptions(RushOptionsPath: original, ImportedProfiles: VisionaryTransferProfiles.Import(original));
            var restored = System.Text.Json.JsonSerializer.Deserialize<VisionaryRaceOptions>(System.Text.Json.JsonSerializer.Serialize(imported))!;
            File.Delete(original);
            var profile = VisionaryTransferProfiles.Load(restored.RushOptionsPath, restored.ImportedProfiles).ForDirectory("/release")!;
            Check(profile.Attempts == 5, "imported retry count survives removal of original");
            Check(profile.Allows(new("file.rar", "/release/file.rar", false, 100, null)), "imported allow regex retained");
            Check(!profile.Allows(new("blocked.rar", "/release/blocked.rar", false, 100, null)), "imported skip regex retained");
            _ = new VisionaryRaceState(restored);
        }
        finally { Directory.Delete(profileFolder, true); }
        foreach (var pattern in new[] { VisionaryRaceOptions.DefaultCompleteFlag, VisionaryRaceOptions.LegacyCompleteFlag })
        {
            var markerRace = new VisionaryRaceState(new(CompleteFlag: pattern));
            RemoteEntry Mark(string name) => new(name, "/release/" + name, true, null, null);
            foreach (var name in new[] { "[##::::::::::::] - 15% Complete - [CR]", "99.9% Complete", "99,9% Complete", "0% COMPLETE", "1100% Complete", "NOT COMPLETE", "INCOMPLETE" })
            {
                Check(markerRace.IsCompletionMetadata(Mark(name)), "progress/incomplete marker excluded from transfer: " + name);
                Check(!markerRace.IsCompletionMarker(Mark(name)), "progress never proves completion: " + name);
                markerRace.BeginListing(); markerRace.InspectListing([Mark(name)], true, "/release"); markerRace.EndListing();
                Check(!markerRace.SourceComplete, "partial marker cannot finalize source: " + name);
            }
            foreach (var name in new[] { "[COMPLETED @ 2026-10-13][ OKAY ][ 42278791kB]", "100% Complete", "100,0% Complete", "[CR] - ( 1788M 13F - COMPLETE ) - [CR]" })
                Check(markerRace.IsCompletionMarker(Mark(name)), "Rush completion marker accepted: " + name);
            Check(!RaceDestinationVerifier.HasCompleteMarker([Mark("COMPLETE"), Mark("15% Complete")], markerRace), "contradictory progress marker blocks destination acceptance");
        }
        Check(TransferRejection.Category("550 file.rar: File locking failed.") == "FILE-LOCKED", "ioFTPD locking failure blocks relay fallback");
        Check(new VisionaryRaceOptions().EffectiveTimeout == TimeSpan.FromMinutes(5), "race default lifetime is five minutes");
        Check(new VisionaryRaceOptions(TimeoutMinutes: 60).EffectiveTimeout == TimeSpan.FromMinutes(60), "configured race lifetime above five minutes retained");
        Check(new VisionaryRaceOptions(TimeoutMinutes: 1440).EffectiveTimeout == TimeSpan.FromDays(1), "maximum race lifetime retained");
        Check(new VisionaryRaceOptions(TimeoutMinutes: 0).EffectiveTimeout == TimeSpan.FromMinutes(1), "invalid saved lifetime bounded");
        Check(new VisionaryRaceOptions(TimeoutMinutes: 2).EffectiveTimeout == TimeSpan.FromMinutes(2), "shorter race timeout retained");
        using (var slots = new RaceWatchSlots(1))
        using (await slots.EnterAsync(default))
        using (var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(50)))
        {
            var expired = false;
            try { using var waiting = await slots.EnterAsync(deadline.Token); }
            catch (OperationCanceledException) { expired = true; }
            Check(expired, "waiting watch expires without acquiring a slot");
        }
        var sampleReply = "553- .----== ioNiNJA ==----.\r\n553- | + SAMPLE: Only 1 sample per SAMPLE DIR |\r\n553 `--------------------'";
        Check(TransferRejection.Category(sampleReply) == "SAMPLE", "sample limit blocks relay fallback and XDUPE completion");
        var sampleLog = TransferRejection.FormatFailure("sample.mkv", sampleReply);
        Check(sampleLog == "Transfer blocked [SAMPLE] (sample.mkv):" + Environment.NewLine + sampleReply, "server box and 553 preserved on separate lines");
        Check(TransferRejection.Category("553 sample.mkv: Permission denied") is null, "filename alone is not sample limit");
        Check(TransferRejection.FormatFailure("a.rar", "550 Denied").Contains("550 Denied"), "generic failure code preserved");
        var missingReply = "< 550 /FLAC/New.Release: No such file or directory.";
        Check(TransferRejection.FormatDirectoryProbeReply("/FLAC/New.Release", missingReply).Contains("directory check"), "expected probe is informational");
        Check(TransferRejection.FormatDirectoryProbeReply(null, missingReply) == missingReply, "normal CWD error remains visible");
        foreach (var failure in new[] { "< 550 Permission denied.", "< 553 [DupeCheck] Dupe: release", "< 550 [NUKED]: No such file or directory.", "< 550-Intermediate reply" })
            Check(TransferRejection.FormatDirectoryProbeReply("/FLAC/release", failure) == failure, "real failure remains visible");
        Check(TransferRejection.Category("553-[DupeCheck]\n553-| Dupe: /FLAC/release") == "DUPE", "nxTools DUPE classified");
        Check(TransferRejection.Category("550 \u001b[31m[NUKED]\u001b[39m incomplete") == "NUKE", "colored NUKE classified");
        Check(TransferRejection.Category("NUKE / REASON marker stopped the VISIONARY race.") == "NUKE", "race NUKE classified");
        Check(TransferRejection.Category("550 /Music/Dupe-Album: No such file or directory") is null, "missing directory not guessed DUPE");
        Check(TransferRejection.Category("553 XDUPE existing file") is null, "XDUPE remains distinct");
        Check(TransferRejection.Category("553- track.mp3: This file looks like a dupe!\n553 It was uploaded by user") == "DUPE", "ioFTPD dupe blocks relay");
        Check(TransferRejection.Category("553 File is being uploaded by Farmor") == "UPLOAD-IN-PROGRESS", "active upload blocks relay and false completion");
        Check(TransferRejection.Category("553 Requested action not taken. File exists.") == "DUPE", "existing file blocks relay");
        Check(TransferRejection.Category("550- Release blocked by section deny filter.\n550 MKD Denied by dirscript.") == "DIRECTORY-DENIED", "release-level directory denial identified");
        var blockedRace = new VisionaryRaceState(new());
        blockedRace.Stop("550 MKD Denied by dirscript.");
        var stopped = false;
        try { blockedRace.NeedsTransfer("/release/next.rar", 100, null, _ => null); }
        catch (IOException ex) { stopped = ex.Message.Contains("550 MKD Denied"); }
        Check(stopped, "directory denial prevents scheduling subsequent race files");
        var deniedRace = new VisionaryRaceState(new());
        var directoryAttempts = 0;
        var firstProbe = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProbe = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task AttemptDeniedDirectory()
        {
            try
            {
                await deniedRace.EnsureDestinationDirectoryAsync(async () =>
                {
                    Interlocked.Increment(ref directoryAttempts);
                    firstProbe.TrySetResult();
                    await releaseProbe.Task;
                    throw new IOException("550 MKD Denied by dirscript.");
                }, default);
            }
            catch (IOException) { }
        }
        var firstAttempt = AttemptDeniedDirectory();
        await firstProbe.Task;
        var remainingAttempts = Enumerable.Range(0, 8).Select(_ => AttemptDeniedDirectory()).ToArray();
        releaseProbe.SetResult();
        await Task.WhenAll(remainingAttempts.Append(firstAttempt));
        Check(directoryAttempts == 1 && deniedRace.DirectoryDenied, "parallel files only attempt denied directory once");
        var otherDestination = new VisionaryRaceState(new());
        var otherContinued = false;
        await otherDestination.EnsureDestinationDirectoryAsync(() => { otherContinued = true; return Task.CompletedTask; }, default);
        Check(otherContinued && !otherDestination.DirectoryDenied, "directory denial does not block other destinations or a new race");
        var commands = new List<string>();
        try
        {
            await TransferRejection.EnsureDirectoryAsync("/FLAC/release", (cmd, token) =>
            {
                commands.Add(cmd);
                return Task.FromResult(new RemoteCommandResult(cmd == "CWD /FLAC" ? 250 : cmd.StartsWith("MKD") ? 553 : 550,
                    cmd.StartsWith("MKD") ? "553 [DupeCheck] Dupe: /FLAC/release" : "550 not found"));
            }, default);
            throw new Exception("DUPE was ignored");
        }
        catch (IOException ex) { Check(TransferRejection.Category(ex.Message) == "DUPE", "original MKD reason preserved"); }
        Check(!commands.Contains("MKD /FLAC"), "existing parent not recreated");
        var callsToCreate = 0;
        await TransferRejection.EnsureDirectoryAsync("/release", (cmd, token) =>
        {
            callsToCreate++;
            return Task.FromResult(new RemoteCommandResult(callsToCreate == 3 ? 250 : 550, "exists"));
        }, default);
        Check(callsToCreate == 3, "concurrent directory creation accepted");
        var cancelled = false;
        try { await TransferRejection.EnsureDirectoryAsync("/release", (_, _) => throw new OperationCanceledException(), default); }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled, "directory cancellation propagated");
        foreach (var response in new[] { "226- CRC-Check: FaileD!", "226-0byte file: DeleTeD!", "226 VERIFICATION FAILED: upload interrupted", "226 BADCRC" })
        {
            var rejected = false;
            try { TransferVerification.EnsureAccepted(response); } catch (IOException) { rejected = true; }
            Check(rejected, "verification failure must not be Completed");
        }
        TransferVerification.EnsureAccepted("226 Transfer complete, CRC-Check: OK"); checks++;
        RemoteEntry F(string name, long size = 100) => new(name, "/release/" + name, false, size, null);
        RemoteEntry M(string name) => new(name, "/release/" + name, true, null, null);
        var race = new VisionaryRaceState(new());
        var expected = new[] { F("test.sfv"), F("test.rar") };
        Check(!RaceDestinationVerifier.Accepts(expected, expected, race), "source complete is not target complete");
        Check(!RaceDestinationVerifier.Accepts(expected, [.. expected, M("COMPLETE"), F("test.rar.missing", 0)], race), "missing overrides target complete");
        Check(!RaceDestinationVerifier.Accepts(expected, [F("test.sfv"), F("test.rar", 50), M("COMPLETE")], race), "partial size rejected");
        Check(RaceDestinationVerifier.Accepts(expected, [.. expected, M("COMPLETE")], race), "verified target accepted");
        var ninjaMarker = M("[ioNiNJA]-(2.7GB 20F-COMPLETE)-[ioNiNJA]");
        Check(race.IsCompletionMarker(ninjaMarker), "ioNiNJA metadata COMPLETE recognized before transfer filters");
        Check(RaceDestinationVerifier.HasCompleteMarker([ninjaMarker], race), "ioNiNJA root marker permits payload verification");
        Check(!RaceDestinationVerifier.HasCompleteMarker([ninjaMarker, F("test.rar-MISSING", 0)], race), "root missing marker blocks precheck even for nested payloads");
        Check(RaceDestinationVerifier.Accepts(expected, [.. expected, ninjaMarker], race), "existing ioNiNJA complete payload can skip before queueing");
        Check(!RaceDestinationVerifier.Accepts(expected, [F("test.sfv"), ninjaMarker], race), "complete marker with absent payload cannot skip");
        Check(!RaceDestinationVerifier.Accepts(expected, [.. expected, ninjaMarker, F("test.rar-MISSING", 0)], race), "hyphen missing marker prevents existing-complete skip");
        var lateComplete = new VisionaryRaceState(new());
        lateComplete.Track("/release/test.rar", 100, null, Guid.NewGuid());
        Check(!lateComplete.Finished(_ => "Failed"), "dupe failure alone does not finish an ongoing race");
        Check(RaceDestinationVerifier.Accepts(expected, [.. expected, ninjaMarker], lateComplete), "destination can verify after jobs have already been tracked");
        lateComplete.ConfirmDestinationComplete();
        Check(lateComplete.Finished(_ => "Failed"), "verified destination completes race despite earlier dupe failures");
        Check(!lateComplete.NeedsTransfer("/release/test.rar", 100, null, _ => "Failed"), "verified complete race never requeues earlier failed files");
        Check(VisionaryRaceState.IsVerificationMetadata(F("test.rar.missing", 0)), "missing markers never copied");
        Check(VisionaryRaceState.IsVerificationMetadata(F(".ioFTPD.verified")), "ledger never copied");
        race.BeginListing();
        race.InspectListing([M("CD1"), M("CD2")], true, "/release");
        race.InspectListing([F("a.sfv"), M("COMPLETE")], false, "/release/CD1");
        race.InspectListing([F("b.sfv"), M("COMPLETE"), F("b.rar.missing", 0)], false, "/release/CD2");
        race.EndListing();
        Check(!race.SourceComplete, "one incomplete disk blocks release");
        race.InspectListing([F("b.sfv"), M("COMPLETE")], false, "/release/CD2"); race.EndListing();
        Check(race.SourceComplete, "all disks complete");
        var growing = new VisionaryRaceState(new());
        var growingJob = Guid.NewGuid();
        growing.Track("/release/growing.rar", 100, null, growingJob);
        Check(!growing.NeedsTransfer("/release/growing.rar", 100, null, _ => "Failed"), "partial upload waits without exhausting retry budget");
        growing.SourceComplete = true;
        Check(growing.NeedsTransfer("/release/growing.rar", 100, null, _ => "Failed"), "final marker retries same-size preallocated file");
        var owner = new object();
        RaceDestinationLease.Acquire("target", "TV", "Release", owner);
        var conflict = false;
        try { RaceDestinationLease.Acquire("target", "TV", "Release/CD1", new object()); } catch (InvalidOperationException) { conflict = true; }
        Check(conflict, "RaceTrade and VISIONARY cannot overlap");
        RaceDestinationLease.Release(owner);
        var spread = new SpreadRaceCoordinator(["source", "target1", "target2"], ["source"]);
        var calls = new Dictionary<string, int>();
        await spread.RunAsync((source, target, state, token) =>
        {
            if (token.IsCancellationRequested) return Task.FromCanceled<bool>(token);
            Check(source == "source" && target != "source", "dlonly never receives uploads");
            state.SourceAvailable = true;
            calls[target] = calls.GetValueOrDefault(target) + 1;
            return Task.FromResult(calls[target] >= 2);
        }, new(RefreshSeconds: 1), CancellationToken.None);
        Check(spread.Status == "DONE" && calls.Values.All(count => count == 2), "all destinations polled until verified");
        var failed = new SpreadRaceCoordinator(["source", "target"], ["source"]);
        await failed.RunAsync((_, _, _, _) => throw new IOException("CRC failure"), new(), CancellationToken.None);
        Check(failed.Status == "FAILED" && failed.Incomplete.Length == 1, "target error cannot become DONE");
        var empty = new SpreadRaceCoordinator(["source", "target"], ["source"]);
        using var cancel = new CancellationTokenSource(100);
        await empty.RunAsync((_, _, _, _) => Task.FromResult(false), new(RefreshSeconds: 1), cancel.Token);
        Check(empty.Status == "ABORTED", "empty/missing release cannot become DONE");
        Console.WriteLine($"PASS: {checks} transfer verification / shared race checks.");
    }
}
