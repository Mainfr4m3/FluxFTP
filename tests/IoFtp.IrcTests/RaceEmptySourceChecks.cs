using IoFtp.Core.Abstractions;
using IoFtp.Desktop.Services;
internal static class RaceEmptySourceChecks
{
    public static void Run()
    {
        var race = new VisionaryRaceState(new());
        RemoteEntry File(string name, long? size) => new(name, "/release/" + name, false, size, null);
        var complete = new RemoteEntry("COMPLETE", "/release/COMPLETE", true, null, null);
        var payload = File("archive.rar", 100);
        race.BeginListing(); race.InspectListing([complete, payload, File("archive.sfv", 0)], true, "/release"); race.EndListing();
        var job = Guid.NewGuid(); race.Track(payload.FullPath, 100, null, job);
        if (!race.HasPendingPayload || race.NeedsTransfer("/release/archive.sfv", 0, null, _ => "Completed") || race.Finished(_ => "Completed")) throw new Exception("Zero SFV transferred or race completed early");
        if (race.NeedsTransfer("/release/archive.sfv", null, null, _ => "Completed")) throw new Exception("Unknown SFV size transferred");
        race.BeginListing(); race.InspectListing([complete, payload, File("archive.sfv", 25)], true, "/release"); race.EndListing();
        if (race.HasPendingPayload || !race.NeedsTransfer("/release/archive.sfv", 25, null, _ => "Completed")) throw new Exception("SFV did not become eligible after growth");
        race.Track("/release/archive.sfv", 25, null, Guid.NewGuid());
        if (!race.Finished(_ => "Completed")) throw new Exception("Completed nonempty payload cannot finish");
        race.BeginListing(); race.InspectListing([complete, payload, File(".ioFTPD.verified", 0)], true, "/release"); race.EndListing();
        if (race.HasPendingPayload) throw new Exception("Verification metadata blocked race");
        Console.WriteLine("PASS: zero/unknown source sizes wait, positive SFV is queued on rescan, pending SFV blocks completion, metadata is excluded.");
    }
}
