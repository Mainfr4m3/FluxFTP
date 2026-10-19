using System.Text;
using System.Reflection;
using IoFtp.Desktop.Services;

internal static class ChainBatchChecks
{
    public static async Task Run()
    {
        var accepted = new List<string>();
        await using var server = new VisionaryPipeServer(request => { accepted.Add(request.SrcSite + "->" + request.DstSite); return Task.FromResult<object>(new { ok = true }); });
        async Task<string> Send(string line, VisionaryPipeServer? receiver = null)
        {
            using var stream = new MemoryStream();
            var input = Encoding.UTF8.GetBytes(line + "\n"); stream.Write(input); stream.Position = 0;
            var method = typeof(VisionaryPipeServer).GetMethod("HandleAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
            await (Task)method.Invoke(receiver ?? server, [stream, CancellationToken.None, 0L])!;
            return Encoding.UTF8.GetString(stream.ToArray(), input.Length, (int)stream.Length - input.Length);
        }
        var response = await Send("CHAINS\tTV\tRelease-GROUP\tA;B,A;C,B;D\t1\t2");
        if (!response.StartsWith("OK RECEIVED 3") || !accepted.SequenceEqual(new[] { "A->B", "A->C", "B->D" })) throw new Exception("Chain batch lost a pair or acknowledgement");
        response = await Send("CHAINS\tTV\tRelease-GROUP\tA;B,invalid\t1\t2");
        if (!response.StartsWith("ERROR") || accepted.Count != 3) throw new Exception("Invalid batch must not partially dispatch");
        response = await Send("TRANSFER\tTV\tRelease-GROUP\tA\tE");
        if (!response.StartsWith("OK") || accepted.Last() != "A->E") throw new Exception("Legacy single transfer broken");
        Console.WriteLine("PASS: complete chain batch, invalid batch rejection, acknowledgement and legacy single transfer.");
        string? resultEvent = null;
        await using var results = new VisionaryPipeServer(_ => throw new Exception("Result must not start a transfer"),
            raceResult: (site, release, winner) => resultEvent = site + ":" + release + ":" + winner);
        var resultAck = await Send("TRANSFER\tTV\tRelease-GROUP\tTIS\t@RESULT:Me\t0\t0", results);
        if (!resultAck.StartsWith("OK RACE RESULT") || resultEvent != "TIS:Release-GROUP:Me") throw new Exception("Ranking result forwarding failed");
        var started = 0;
        var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var watches = new VisionaryPipeServer(_ => Task.FromResult<object>(new { ok = true }), raceStep: (_, _, token) =>
        {
            token.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref started) == 40) allStarted.TrySetResult();
            return Task.FromResult(false);
        }, concurrentScheduling: 1);
        var acknowledgement = await watches.SubmitChainsAsync("TV", "Many.Destinations-GROUP",
            string.Join(',', Enumerable.Range(0, 40).Select(index => "SOURCE;DEST" + index)));
        if (!acknowledgement.StartsWith("OK RECEIVED 40")) throw new Exception("Large race batch rejected");
        await allStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Console.WriteLine("PASS: all 40 destinations start with one scheduling slot while earlier watches wait for updates.");
        var periodic = System.Threading.Channels.Channel.CreateUnbounded<bool>();
        await using var unattended = new VisionaryPipeServer(_ => Task.FromResult<object>(new { ok = true }), raceStep: (_, state, token) =>
        {
            token.ThrowIfCancellationRequested();
            if (state.RecheckSeconds != 1) throw new Exception("Configured recheck interval did not reach watch");
            periodic.Writer.TryWrite(true);
            return Task.FromResult(false);
        }, recheckSeconds: 1);
        await unattended.SubmitChainsAsync("TV", "Periodic.Check-GROUP", "A;B");
        await periodic.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await periodic.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(65));
        Console.WriteLine("PASS: watch rechecks without a second incoming Visi announcement.");
        var snapshots = System.Threading.Channels.Channel.CreateUnbounded<ApiTransferRequest>();
        var alternateWorked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var resilient = new VisionaryPipeServer(_ => Task.FromResult<object>(new { ok = true }), raceStep: (request, _, token) =>
        {
            token.ThrowIfCancellationRequested();
            if (request.SrcSite == "A") throw new IOException("530 Access denied");
            alternateWorked.TrySetResult();
            return Task.FromResult(true);
        });
        await resilient.SubmitChainsAsync("TV", "Source.Failure-GROUP", "A;B,C;B");
        await alternateWorked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Console.WriteLine("PASS: denied first source does not terminate a healthy alternative source.");
        var priorityPasses = System.Threading.Channels.Channel.CreateUnbounded<string>();
        var updates = new System.Collections.Concurrent.ConcurrentQueue<ApiTransferRequest>();
        VisionaryRaceState? liveState = null;
        await using var live = new VisionaryPipeServer(_ => Task.FromResult<object>(new { ok = true }), raceStep: (request, state, token) =>
        {
            token.ThrowIfCancellationRequested();
            liveState = state;
            if (state.SourceCount == 2 && !state.BorrowIdleSlots) priorityPasses.Writer.TryWrite(request.SrcSite!);
            snapshots.Writer.TryWrite(request);
            return Task.FromResult(false);
        }, liveSlots: request => { updates.Enqueue(request); return Task.CompletedTask; }, sourcePriority: site => site == "C" ? 100 : 0);
        async Task<ApiTransferRequest> Next() => await snapshots.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        async Task<ApiTransferRequest> NextSource(string source, int? downloads = null, int? uploads = null)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (true)
            {
                var item = await snapshots.Reader.ReadAsync(deadline.Token);
                if (item.SrcSite == source && (downloads is null || item.SourceDownloadSlots == downloads) &&
                    (uploads is null || item.DestinationUploadSlots == uploads)) return item;
            }
        }
        await live.SubmitChainsAsync("TV", "Live-GROUP", "A;B;8;3");
        var first = await Next();
        if (first.SourceDownloadSlots != 8 || first.DestinationUploadSlots != 3) throw new Exception("Live slots lost");
        await live.SubmitChainsAsync("TV", "Live-GROUP", "A;B;4;0");
        var updated = await NextSource("A", 4, 0);
        if (updated.SourceDownloadSlots != 4 || updated.DestinationUploadSlots != 0) throw new Exception("Active watch did not receive changed/zero slots");
        var firstOwner = liveState!.DestinationOwner;
        liveState.HasActiveTransfers = true;
        await live.SubmitChainsAsync("TV", "Live-GROUP", "C;B;9;2");
        updated = await NextSource("C", 9, 2);
        if (await priorityPasses.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)) != "C")
            throw new Exception("Higher priority source must reserve files before the lower priority source when both are available");
        if (updated.SrcSite != "C" || updated.SourceDownloadSlots != 9 || !ReferenceEquals(firstOwner, liveState!.DestinationOwner))
            throw new Exception("Alternate source must be admitted while the first has active work, with its own slots and shared destination ownership");
        await live.SubmitChainsAsync("TV", "Live-GROUP", "A;B");
        updated = await NextSource("A", 4, 2);
        if (updated.SourceDownloadSlots != 4 || updated.DestinationUploadSlots != 2) throw new Exception("Legacy update erased live limits");
        liveState!.NotifyTransferCompleted();
        await Next();
        if (updates.Count != 4 || updates.ElementAt(2).SrcSite != "C" || updates.ElementAt(2).SourceDownloadSlots != 9)
            throw new Exception("Slot changes must apply on receipt, including alternate sources, without stale replay on transfer wakeups");
        var invalid = await live.SubmitChainsAsync("TV", "Invalid-GROUP", "A;B;8;3,A;C;-1;4");
        if (!invalid.StartsWith("ERROR") || updates.Count != 4) throw new Exception("Invalid slot batch partially dispatched");
        Console.WriteLine("PASS: live slots, parallel sources while first source is active, shared destination ownership, legacy compatibility and atomic invalid batch rejection.");
    }
}
