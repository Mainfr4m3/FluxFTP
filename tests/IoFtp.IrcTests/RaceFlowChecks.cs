using IoFtp.Core.Abstractions;
using IoFtp.Core.Transport;
using IoFtp.Desktop.Services;

internal static class RaceFlowChecks
{
    public static async Task Run()
    {
        var checks = 0;
        void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
        var reservations = new HashSet<string>(StringComparer.Ordinal);
        Check(RaceFileWindow.Capacity(10, 10, 5, 0, 0, 0, false) == 2, "first pass offers a fair share to each source");
        Check(RaceFileWindow.Capacity(10, 10, 5, 2, 2, 2, true) == 8, "eligible source borrows all unused target slots after other sources were offered work");
        Check(RaceFileWindow.Capacity(3, 10, 5, 2, 2, 2, true) == 1, "borrow respects remaining source downloads");
        Check(RaceFileWindow.Capacity(10, 10, 5, 0, 0, 9, true) == 1, "other races using the same target reduce available uploads");
        Check(RaceFileWindow.Capacity(10, 10, 5, 0, 0, 10, true) == 0, "full target cannot be overfilled");
        var firstSource = RaceFileWindow.Select(new[] { "a.sfv", "b.rar", "c.rar" }, file => file, reservations, 2);
        var secondSource = RaceFileWindow.Select(new[] { "a.sfv", "b.rar", "c.rar", "d.rar" }, file => file, reservations, 2);
        Check(firstSource.SequenceEqual(new[] { "a.sfv", "b.rar" }), "first source uses a bounded priority window");
        Check(secondSource.SequenceEqual(new[] { "c.rar", "d.rar" }), "second source schedules distinct files while the first remains active");
        Check(RaceFileWindow.Select(new[] { "e.rar" }, file => file, reservations, 0).Count == 0, "zero slots do not reserve a file");
        reservations.Remove("a.sfv");
        Check(RaceFileWindow.Select(new[] { "a.sfv" }, file => file, reservations, 1).Count == 1, "released reservation permits a later retry");
        var poolGates = new SitePoolSynchronization();
        var slowSite = Guid.NewGuid();
        var fastSite = Guid.NewGuid();
        await poolGates.For(slowSite).WaitAsync();
        var sameSiteWaiter = poolGates.For(slowSite).WaitAsync();
        await poolGates.For(fastSite).WaitAsync().WaitAsync(TimeSpan.FromSeconds(1));
        Check(!sameSiteWaiter.IsCompleted, "same-site worker ownership remains serialized");
        Check(poolGates.For(fastSite).CurrentCount == 0, "unrelated site can rent while slow site's maintenance owns its gate");
        poolGates.For(fastSite).Release();
        poolGates.For(slowSite).Release();
        await sameSiteWaiter.WaitAsync(TimeSpan.FromSeconds(1));
        poolGates.For(slowSite).Release();
        var smartRace = new VisionaryRaceState(new());
        Check(smartRace.DeferSmallerSource("/r/f", 50, 100, true, 2), "smart initial wait");
        Check(smartRace.DeferSmallerSource("/r/f", 80, 100, true, 2), "smart first update wait");
        Check(!smartRace.DeferSmallerSource("/r/f", 90, 100, true, 2), "smart second update exhausts checks");
        Check(!smartRace.DeferSmallerSource("/r/g", 100, 100, true, 3), "equal sizes follow target policy");
        Check(!smartRace.DeferSmallerSource("/r/g", 50, 100, false, 3), "disabled smart mode preserves skip");
        var prequeueRace = new VisionaryRaceState(new());
        prequeueRace.SkipExistingTarget("/release/file.rar");
        Check(prequeueRace.NeedsTransfer("/release/file.rar", 100, null, _ => null), "existing target must be checked again before creating a job");
        Check(prequeueRace.NeedsTransfer("/release/file.rar", 200, DateTimeOffset.UtcNow, _ => null), "growing source must not be permanently skipped");
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
        var preparedOwner = new object();
        var preparedA = new VisionaryRaceState(new()) { DestinationOwner = preparedOwner };
        var preparedB = new VisionaryRaceState(new()) { DestinationOwner = preparedOwner };
        var preparedCalls = 0;
        var enteredPreparation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishPreparation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var preparing = preparedA.EnsureDestinationDirectoryAsync("/TV/Release", async () =>
        {
            preparedCalls++;
            enteredPreparation.SetResult();
            await finishPreparation.Task;
        }, default);
        await enteredPreparation.Task;
        var waitingPreparations = Enumerable.Range(0, 12).Select(_ => preparedB.EnsureDestinationDirectoryAsync("/TV/Release",
            () => { preparedCalls++; return Task.CompletedTask; }, default)).ToArray();
        finishPreparation.SetResult();
        await Task.WhenAll(waitingPreparations.Append(preparing));
        Check(preparedCalls == 1, "parallel race sources prepare the same destination path only once");
        foreach (var path in new[] { "/TV/Release/Sample", "/TV/release", "/TV/Release2" })
            await preparedB.EnsureDestinationDirectoryAsync(path, () => { preparedCalls++; return Task.CompletedTask; }, default);
        Check(preparedCalls == 4, "subdirectories and case-sensitive distinct paths have independent preparation");
        await new VisionaryRaceState(new()).EnsureDestinationDirectoryAsync("/TV/Release",
            () => { preparedCalls++; return Task.CompletedTask; }, default);
        Check(preparedCalls == 5, "another race destination does not reuse a previous race's preparation");
        var retryPreparation = new VisionaryRaceState(new());
        var retryCalls = 0;
        try
        {
            await retryPreparation.EnsureDestinationDirectoryAsync("/TV/Retry", () =>
            { retryCalls++; throw new IOException("temporary connection failure"); }, default);
        }
        catch (IOException) { }
        await retryPreparation.EnsureDestinationDirectoryAsync("/TV/Retry", () => { retryCalls++; return Task.CompletedTask; }, default);
        Check(retryCalls == 2, "failed preparation is not cached and can be retried");
        using (var cancelledPreparation = new CancellationTokenSource())
        {
            try
            {
                await retryPreparation.EnsureDestinationDirectoryAsync("/TV/Cancelled", () =>
                { cancelledPreparation.Cancel(); return Task.CompletedTask; }, cancelledPreparation.Token);
            }
            catch (OperationCanceledException) { }
        }
        var cancelledRetried = false;
        await retryPreparation.EnsureDestinationDirectoryAsync("/TV/Cancelled", () => { cancelledRetried = true; return Task.CompletedTask; }, default);
        Check(cancelledRetried, "cancelled preparation is not cached even when the callback returns");
        preparedA.SkipDirectory(new LockedDirectorySkip("GBIT02", true));
        var cachedPathStopped = false;
        try { await preparedB.EnsureDestinationDirectoryAsync("/TV/Release", () => Task.CompletedTask, default); }
        catch (RaceDirectorySkippedException) { cachedPathStopped = true; }
        Check(cachedPathStopped, "a later directory lock stops even a previously prepared path");
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
        var longPath = "/FITV/Unicorn.Academy.Secrets.Revealed.S02E03.FiNNiSH.1080p.WEB.H264-TOOSA";
        var attemptedPaths = new List<string>();
        var directoryOwner = new object();
        var sourceA = new VisionaryRaceState(new()) { DestinationOwner = directoryOwner };
        var sourceB = new VisionaryRaceState(new()) { DestinationOwner = directoryOwner };
        var directoryFailures = 0;
        async Task TryBlocked(VisionaryRaceState owner)
        {
            try
            {
                await owner.EnsureDestinationDirectoryAsync(() => TransferRejection.EnsureDirectoryAsync(longPath, (command, _) =>
                {
                    attemptedPaths.Add(command);
                    return Task.FromResult(new RemoteCommandResult(command == "CWD /FITV" ? 250 : 550,
                        command.StartsWith("MKD") ? "550 System Error- /FITV/Unicorn.Academy.Secrets.Revea: File exists." : "550 Access denied"));
                }, default), default);
            }
            catch (IOException) { directoryFailures++; }
        }
        await Task.WhenAll(Enumerable.Range(0, 20).Select(index => TryBlocked(index % 2 == 0 ? sourceA : sourceB)));
        Check(directoryFailures == 20 && attemptedPaths.Count(command => command.StartsWith("MKD")) == 1,
            "GBIT02 CWD/MKD exists/CWD failure is probed once across all source states");
        Check(sourceA.DirectoryDenied && sourceB.DirectoryDenied && attemptedPaths.All(command => command.EndsWith("/FITV") || command.EndsWith(longPath)),
            "permanent directory failure is shared and no truncated directory is guessed");
        var authCommands = 0;
        try
        {
            await TransferRejection.EnsureDirectoryAsync("/denied", (_, _) =>
            { authCommands++; return Task.FromResult(new RemoteCommandResult(530, "530 Access denied")); }, default, "JBG");
            throw new Exception("530 was ignored");
        }
        catch (FtpCommandException ex) { Check(ex.StatusCode == 530 && authCommands == 1 && ex.Command == "CWD /denied" && ex.Site == "JBG", "530 CWD preserves the site and does not trigger MKD"); }
        try
        {
            await TransferRejection.EnsureDirectoryAsync("/denied", (_, _) =>
                Task.FromResult(new RemoteCommandResult(550, "550 Permission denied")), default, "GBIT02");
            throw new Exception("Directory rejection was ignored");
        }
        catch (DestinationDirectoryException ex)
        {
            Check(ex.Site == "GBIT02" && ex.Message.Contains("site=GBIT02; command=CWD /denied") && ex.IsPermanent,
                "Final directory failure must retain destination site and command");
        }
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
        foreach (var name in new[] { "[SUB]-Polish_Polish-[SUB]", "[AUDIO]-Japanese_AACLC@a_Bit rate_48.0kHz_2ch-[AUDIO]", "[VIDEO]-1920x1080_x264-[VIDEO]" })
        {
            Check(VisionaryRaceState.IsVerificationMetadata(M(name)), "server information directory never traversed: " + name);
            Check(VisionaryRaceState.IsVerificationMetadata(F(name, 0)), "zero-byte information marker never copied: " + name);
            Check(!VisionaryRaceState.IsVerificationMetadata(F(name, 100)), "nonempty file is not information metadata: " + name);
        }
        foreach (var name in new[] { "Subs", "Sample", "CD1", "[SUB]-Actual.Subtitle.Folder", "[AUDIO]-Track-[SUB]" })
            Check(!VisionaryRaceState.IsVerificationMetadata(M(name)), "real folders and unmatched tags remain eligible: " + name);

        race.BeginListing();
        race.InspectListing([M("CD1"), M("CD2")], true, "/release");
        race.InspectListing([F("a.sfv"), M("COMPLETE")], false, "/release/CD1");
        race.InspectListing([F("b.sfv"), M("COMPLETE"), F("b.rar.missing", 0)], false, "/release/CD2");
        race.EndListing();
        Check(!race.SourceComplete, "one incomplete disk blocks release");
        race.InspectListing([F("b.sfv"), M("COMPLETE")], false, "/release/CD2"); race.EndListing();
        Check(race.SourceComplete, "all disks complete");
        var growing = new VisionaryRaceState(new());
        var retryNow = DateTimeOffset.UtcNow;
        var oneSecondOptions = new VisionaryRaceOptions(RefreshSeconds: 3, RecheckSeconds: 1);
        Check(oneSecondOptions.EffectiveRecheckSeconds == 1 && oneSecondOptions.RefreshSeconds == 3, "faster rechecks do not shorten source stability interval");
        var oneSecondRace = new VisionaryRaceState(oneSecondOptions, utcNow: () => retryNow);
        oneSecondRace.Track("/release/one.rar", 100, null, Guid.NewGuid());
        oneSecondRace.RecordFailure("/release/one.rar", "553 File is being uploaded");
        retryNow += TimeSpan.FromSeconds(1);
        Check(oneSecondRace.NeedsTransfer("/release/one.rar", 100, null, _ => "Failed"), "one-second recheck also applies to temporary retry cooldown");
        var lockedRace = new VisionaryRaceState(new(), utcNow: () => retryNow);
        lockedRace.Track("/release/locked.rar", 100, null, Guid.NewGuid());
        Check(lockedRace.RecordFailure("/release/locked.rar", "553 File is being uploaded by another user"), "busy target is temporary");
        Check(!lockedRace.NeedsTransfer("/release/locked.rar", 100, null, _ => "Failed"), "busy retry waits for cooldown");
        retryNow += TimeSpan.FromSeconds(3);
        Check(lockedRace.NeedsTransfer("/release/locked.rar", 100, null, _ => "Failed"), "busy retry works without source size change");
        Check(!lockedRace.NeedsTransfer("/release/locked.rar", 100, null, _ => "Queued"), "busy retry cannot duplicate queued work");
        Check(!lockedRace.RecordFailure("/release/other.rar", "550 MKD Denied by dirscript."), "directory rules never treated as temporary locks");
        Check(TransferFilePriority.RaceRank("release.sfv") < TransferFilePriority.RaceRank("release.rar"), "race SFV precedes payload");
        var defaults = new IoFtp.Desktop.Models.GlobalSettings();
        Check(FxpExistingFilePolicy.Resolve(defaults, 100, 40, true, true) == IoFtp.Core.Models.ExistingTargetAction.Resume, "final binary source can resume partial race target");
        Check(FxpExistingFilePolicy.Resolve(defaults, 100, 40, true, false) == IoFtp.Core.Models.ExistingTargetAction.Skip, "unfinished source cannot enable automatic resume");
        Check(FxpExistingFilePolicy.Resolve(defaults with { FxpSmallerAction = IoFtp.Core.Models.ExistingTargetAction.Skip }, 100, 40, true, true) == IoFtp.Core.Models.ExistingTargetAction.Skip, "explicit skip is preserved");
        lockedRace.SourceComplete = true;
        lockedRace.DeferExistingTarget("/release/partial.rar");
        Check(!lockedRace.Finished(_ => "Skipped"), "deferred target prevents false race completion");
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
