using IoFtp.Desktop.Services;

internal static class RaceSfvBarrierChecks
{
    public static void Run()
    {
        var checks = 0;
        void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
        var gate = new RaceSfvBarrier();
        string[] files = ["/release/r00", "/release/release.sfv", "/release/r01"];
        Check(gate.Select(files, path => path).SequenceEqual(files), "No SFV observation must not hold up a release");
        gate.Observe(files[1], 100);
        Check(gate.PendingCount == 1 && gate.Select(files, path => path).SequenceEqual([files[1]]), "Only the available SFV may be queued first");
        Check(gate.Select(new[] { files[0], files[2] }, path => path).Count == 0, "Another source without the SFV cannot queue payload while it is pending");
        Check(gate.Select(files, path => path).SequenceEqual([files[1]]), "A queued, failed or cancelled SFV without confirmation must keep the barrier closed");
        gate.Confirm(files[1], 0);
        Check(gate.PendingCount == 1, "Zero-byte confirmation is not success");
        gate.Confirm(files[1], 50);
        Check(gate.PendingCount == 1, "A partial SFV must not release payload");
        gate.Confirm(files[1], 100);
        Check(gate.PendingCount == 0 && gate.Select(files, path => path).SequenceEqual(files), "Successful or equal-size SFV confirmation releases payload");
        gate.Observe(files[1], 200);
        Check(gate.PendingCount == 1, "Growing SFV reopens the barrier");
        gate.Observe(files[1], 50);
        gate.Confirm(files[1], 100);
        Check(gate.PendingCount == 1, "Lagging source cannot undo a larger observed SFV");
        gate.Confirm(files[1], 200);
        gate.Observe("/release/CD2/disc.sfv", null);
        Check(gate.PendingCount == 1, "Known SFV with unknown size must wait");
        gate.Confirm("/release/CD2/disc.sfv", 80);
        Check(gate.PendingCount == 0, "Successful transfer can confirm an initially unknown size");
        gate.Confirm("/other/unobserved.sfv", 1000);
        Check(gate.PendingCount == 0, "Unobserved file does not introduce requirements");
        var owner = new object();
        var first = new VisionaryRaceState(new()) { DestinationOwner = owner, WaitForSfv = true };
        var alternative = new VisionaryRaceState(new()) { DestinationOwner = owner, WaitForSfv = true, SourceComplete = true };
        var otherDestination = new VisionaryRaceState(new());
        first.Sfv.Observe("/release/release.sfv", 100);
        alternative.SkipExistingTarget("/source/r00");
        Check(ReferenceEquals(first.Sfv, alternative.Sfv) && alternative.Sfv.PendingCount == 1, "Sources writing the same destination share one SFV barrier");
        Check(otherDestination.Sfv.PendingCount == 0, "Independent destinations do not block each other");
        Check(!alternative.Finished(_ => "Completed"), "Source without SFV must not falsely complete a destination while another source is preparing it");
        first.Sfv.Confirm("/release/release.sfv", 100);
        Check(alternative.Finished(_ => "Completed"), "Shared SFV completion allows the normal completion checks");
        var concurrent = new RaceSfvBarrier();
        Parallel.For(1, 101, size => concurrent.Observe("/parallel.sfv", size));
        concurrent.Confirm("/parallel.sfv", 99);
        Check(concurrent.PendingCount == 1, "Parallel observations keep the largest required size");
        concurrent.Confirm("/parallel.sfv", 100);
        Check(concurrent.PendingCount == 0, "Parallel observation barrier releases after full confirmation");
        Check(TransferFilePriority.Category("sample.sfv", "/release/Sample/sample.sfv") == "Sample" &&
              TransferFilePriority.Category("proof.sfv", "/release/Proof/proof.sfv") == "Proof", "Sample and Proof SFVs retain their own categories");
        Console.WriteLine($"PASS: {checks} SFV-first barrier checks, shared sources, independent destinations, partial/failed/unknown SFVs, growth, completion and no-SFV releases.");
    }
}
