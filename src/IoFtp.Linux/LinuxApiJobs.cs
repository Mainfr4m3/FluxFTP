using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using IoFtp.Core.Models;
using IoFtp.Core.Abstractions;
using IoFtp.Core.Transport;
using IoFtp.Desktop.Services;
using Microsoft.AspNetCore.Http;
namespace IoFtp.Linux;
internal sealed partial class LinuxApiServer
{
    private readonly object _groupGate = new();
    private readonly Dictionary<string, ApiGroup> _groups = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, byte> _failureRecorded = new();
    private readonly ConcurrentDictionary<Guid, string> _jobPaths = new();
    private int _nextId;
    private sealed class ApiGroup
    {
        public required string Name { get; init; }
        public required string Section { get; init; }
        public required string[] Sites { get; init; }
        public required CancellationTokenSource Cancellation { get; init; }
        public int Id { get; init; }
        public bool Spread { get; init; }
        public string Status { get; set; } = "RUNNING";
        public string Error { get; set; } = "";
        public DateTimeOffset Started { get; } = DateTimeOffset.UtcNow;
        public HashSet<Guid> Jobs { get; } = [];
        public SpreadRaceCoordinator? Coordinator { get; set; }
        public LinuxTransferRequest? Request { get; init; }
        public Task? Worker { get; set; }
        public long Size { get; set; }
    }
    private sealed record GroupView(int Id, string Name, string Section, string Status, string[] Sites,
        [property: JsonPropertyName("sites_incomplete")] string[] Incomplete,
        [property: JsonPropertyName("size_estimated_bytes")] long Size,
        [property: JsonPropertyName("time_spent_seconds")] long Seconds,
        string Error, [property: JsonPropertyName("job_ids")] Guid[] Jobs,
        [property: JsonPropertyName("src_site")] string? Source,
        [property: JsonPropertyName("dst_site")] string? Target);
    private GroupView GroupSnapshot(ApiGroup group)
    {
        lock (_groupGate) return new(group.Id, group.Name, group.Section, group.Status,
            group.Sites, group.Status == "DONE" ? [] : group.Coordinator?.Incomplete ?? group.Sites.Skip(1).ToArray(), group.Size,
            Math.Max(0, (long)(DateTimeOffset.UtcNow - group.Started).TotalSeconds), group.Error, group.Jobs.ToArray(), group.Sites.FirstOrDefault(), group.Sites.Skip(1).FirstOrDefault());
    }
    private GroupView[] GroupSnapshots(bool spread) { lock (_groupGate) return _groups.Values.Where(group => group.Spread == spread).Select(GroupSnapshot).ToArray(); }
    private ApiGroup FindGroup(string name, bool spread)
    {
        lock (_groupGate) return _groups.Values.FirstOrDefault(group => group.Spread == spread && (Same(group.Name, name) || group.Id.ToString() == name || group.Jobs.Any(id => Same(id.ToString(), name)))) ?? throw new KeyNotFoundException("Job not found");
    }
    private static string Release(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("name is required");
        VisionaryTransferProfiles.ReleasePath(name);
        if (name.Length > 255 || name.Contains('/')) throw new ArgumentException("name must be a release or filename without directory separators");
        return name;
    }
    private ApiGroup Admit(string name, string section, string[] sites, bool spread, LinuxTransferRequest? request = null)
    {
        lock (_groupGate)
        {
            var key = (spread ? "spread:" : "transfer:") + name;
            if (_groups.TryGetValue(key, out var existing) && existing.Status == "RUNNING") throw new ApiConflictException("Job already running");
            if (_groups.Values.Count(group => group.Status == "RUNNING") >= _options.MaxSpreadJobs) throw new ApiLimitException();
            foreach (var old in _groups.Where(pair => pair.Value.Status != "RUNNING").OrderBy(pair => pair.Value.Started).Take(Math.Max(0, _groups.Count - 249)).ToArray()) { _groups.Remove(old.Key); old.Value.Cancellation.Dispose(); }
            if (existing is not null) existing.Cancellation.Dispose();
            var group = new ApiGroup { Id = Interlocked.Increment(ref _nextId), Name = name, Section = section, Sites = sites, Spread = spread, Request = request, Cancellation = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token) };
            group.Cancellation.CancelAfter(TimeSpan.FromMinutes(_options.TimeoutMinutes)); _groups[key] = group; return group;
        }
    }
    private IResult StartTransfer(LinuxTransferRequest request)
    {
        var source = FindSite(request.SourceSite ?? throw new ArgumentException("src_site is required"));
        var target = FindSite(request.TargetSite ?? throw new ArgumentException("dst_site is required")); CheckRoute(source, target);
        var name = Release(request.Name ?? throw new ArgumentException("name is required"));
        var sourceRoot = Join(Resolve(source, request.SourceSection, request.SourcePath), name);
        var targetRoot = Join(Resolve(target, request.TargetSection, request.TargetPath), name);
        ApiGroup group;
        try { group = Admit(name, request.SourceSection ?? "", [source.Name, target.Name], false, request); }
        catch (ApiConflictException ex) { return Results.Conflict(new { error = ex.Message }); }
        catch (ApiLimitException) { return Results.Json(new { error = "Maximum active API jobs reached" }, statusCode: 429); }
        group.Worker = Task.Run(async () =>
        {
            try
            {
                var token = group.Cancellation.Token;
                var inventory = await Inventory(source, sourceRoot, null, token, true);
                var files = await Candidates(source, target, inventory, targetRoot, null, [], token);
                if (inventory.Files.Count == 0) throw new IOException("Source contains no transferable files");
                foreach (var file in files)
                {
                    token.ThrowIfCancellationRequested(); var id = await _queue(file);
                    if (id == Guid.Empty) throw new IOException("Target is busy with another queue job");
                    lock (_groupGate) group.Jobs.Add(id);
                }
                lock (_groupGate) group.Size = inventory.Files.Sum(file => file.Entry.Size ?? 0);
                await WaitForJobs(group, token); SetStatus(group, "DONE", "");
            }
            catch (OperationCanceledException) { SetStatus(group, "ABORTED", "Job cancelled or timed out"); await CancelJobs(group); }
            catch (Exception ex) { SetStatus(group, "FAILED", ex.Message); await CancelJobs(group); }
        });
        return Results.Json(new { id = group.Id, name, state = "STARTED", status = "RUNNING" }, statusCode: 202);
    }
    private IResult StartSpread(LinuxSpreadRequest request)
    {
        var name = Release(request.Name);
        if (string.IsNullOrWhiteSpace(request.Section) || request.Sites is not { Length: >= 2 and <= 128 }) throw new ArgumentException("section and at least two sites are required");
        if (!string.IsNullOrWhiteSpace(request.Profile) && !Same(request.Profile, "normal") && !Same(request.Profile, "default")) throw new ArgumentException("Named transfer profiles are not supported in Linux yet");
        var sites = request.Sites.Distinct(StringComparer.OrdinalIgnoreCase).Select(FindSite).OrderByDescending(site => site.EffectiveOptions.Priority).ToArray();
        foreach (var site in sites) { _ = Resolve(site, request.Section, null); if (site.Protocol == TransferProtocol.Sftp) throw new ArgumentException("Spread FXP requires FTP/FTPS sites"); }
        if (sites.Length < 2) throw new ArgumentException("Two distinct sites are required");
        var downloadOnly = request.DownloadOnly ?? [];
        if (downloadOnly.Any(name => !sites.Any(site => Same(site.Name, name))) || sites.All(site => downloadOnly.Any(name => Same(site.Name, name)))) throw new ArgumentException("sites_dlonly must be a subset with at least one upload target");
        ApiGroup group;
        try { group = Admit(name, request.Section, sites.Select(site => site.Name).ToArray(), true); }
        catch (ApiConflictException ex) { return Results.Conflict(new { error = ex.Message }); }
        catch (ApiLimitException) { return Results.Json(new { error = "Maximum active API jobs reached" }, statusCode: 429); }
        var coordinator = new SpreadRaceCoordinator(group.Sites, downloadOnly); group.Coordinator = coordinator;
        var options = new VisionaryRaceOptions(TimeoutMinutes: _options.TimeoutMinutes, RecheckSeconds: _options.RecheckSeconds, ImportedProfiles: []);
        group.Worker = Task.Run(async () =>
        {
            await coordinator.RunAsync(async (sourceName, targetName, race, token) =>
            {
                if (token.IsCancellationRequested) { if (coordinator.Status != "DONE") await CancelJobs(group); return false; }
                var source = FindSite(sourceName); var target = FindSite(targetName); CheckRoute(source, target);
                var root = Join(Resolve(source, request.Section, null), name); var destination = Join(Resolve(target, request.Section, null), name);
                InventoryResult inventory;
                try { inventory = await Inventory(source, root, race, token, false); }
                catch (FtpCommandException ex) when (ex.StatusCode == 550) { race.SourceAvailable = false; return false; }
                var snapshot = await _jobs(race.Jobs.ToArray());
                string? Status(Guid id) => snapshot.FirstOrDefault(job => job.Id == id)?.State;
                foreach (var job in snapshot.Where(job => job.State == "Failed"))
                    if (_failureRecorded.TryAdd(job.Id, 0) && !race.RecordFailure(_jobPaths.GetValueOrDefault(job.Id, ""), job.Error))
                        throw new IOException("Transfer failed permanently: " + job.Error);
                var candidates = await Candidates(source, target, inventory, destination, race, snapshot, token);
                foreach (var file in candidates)
                {
                    token.ThrowIfCancellationRequested(); var id = await _queue(file);
                    if (id == Guid.Empty) { race.DeferExistingTarget(file.SourcePath); continue; }
                    var entry = inventory.Files.Single(item => item.Entry.FullPath == file.SourcePath).Entry;
                    _jobPaths[id] = entry.FullPath; race.Track(entry.FullPath, entry.Size, entry.ModifiedAt, id); lock (_groupGate) group.Jobs.Add(id);
                }
                lock (_groupGate) group.Size = Math.Max(group.Size, inventory.Files.Sum(file => file.Entry.Size ?? 0));
                return race.DestinationVerified && race.Finished(Status);
            }, options, group.Cancellation.Token);
            SetStatus(group, coordinator.Status, coordinator.Error);
            if (coordinator.Status != "DONE") await CancelJobs(group);
        });
        return Results.Json(new { id = group.Id, name, state = "STARTED", sites = group.Sites });
    }
    private async Task WaitForJobs(ApiGroup group, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested(); Guid[] ids; lock (_groupGate) ids = group.Jobs.ToArray();
            var jobs = await _jobs(ids);
            if (jobs.Count != ids.Length || jobs.Any(job => job.State is "Failed" or "Missing")) throw new IOException(jobs.FirstOrDefault(job => job.State == "Failed")?.Error ?? "Transfer job was removed");
            if (jobs.All(job => job.State == "Completed")) return;
            await Task.Delay(200, token);
        }
    }
    private void SetStatus(ApiGroup group, string status, string error)
    {
        lock (_groupGate)
        {
            if (group.Cancellation.IsCancellationRequested && status == "DONE") { status = "ABORTED"; error = "Job cancelled or timed out"; }
            group.Status = status; group.Error = error;
        }
        _log($"API {group.Name}: {status}");
    }
    private async Task<IResult> Abort(string name, bool spread)
    { var group = FindGroup(name, spread); group.Cancellation.Cancel(); await CancelJobs(group); SetStatus(group, "ABORTED", "Cancelled by API"); return Results.Ok(new { aborted = name }); }
    private IResult Reset(string name)
    { var group = FindGroup(name, false); if (group.Status == "RUNNING") return Results.Conflict(new { error = "Job still running" }); return StartTransfer(group.Request!); }
    private async Task CancelJobs(ApiGroup group) { Guid[] ids; lock (_groupGate) ids = group.Jobs.ToArray(); foreach (var id in ids) await _cancel(id); }
    private sealed class ApiConflictException(string message) : Exception(message);
    private sealed class ApiLimitException : Exception;
    private static string Join(string root, string relative) => Clean(root.TrimEnd('/') + "/" + relative);
    private static void CheckRoute(ConnectionProfile source, ConnectionProfile target)
    {
        if (source.Id == target.Id || source.Protocol == TransferProtocol.Sftp || target.Protocol == TransferProtocol.Sftp) throw new ArgumentException("FXP requires two different FTP/FTPS sites");
        if (!source.EffectiveOptions.AllowDownload || !target.EffectiveOptions.AllowUpload) throw new ArgumentException("Site direction is disabled");
        bool Blocks(string text, string name) => text.Split([';', ',', '\r', '\n', ' '], StringSplitOptions.RemoveEmptyEntries).Any(value => Same(value, name));
        if (Blocks(source.EffectiveOptions.BlockTransfersTo, target.Name) || Blocks(target.EffectiveOptions.BlockTransfersFrom, source.Name)) throw new ArgumentException("Site routing policy blocks this route");
    }
    private sealed record InventoryFile(RemoteEntry Entry, string Relative);
    private sealed record InventoryResult(List<InventoryFile> Files);
    private async Task<InventoryResult> Inventory(ConnectionProfile source, string root, VisionaryRaceState? race, CancellationToken token, bool allowSingle)
    {
        await using var remote = await Open(source, token); var files = new List<InventoryFile>();
        race?.BeginListing();
        var metadata = race ?? new VisionaryRaceState(new VisionaryRaceOptions(ImportedProfiles: []));
        if (allowSingle && await remote.Session.GetSizeAsync(root, remote.Token) is { } size) return new([new(new RemoteEntry(System.IO.Path.GetFileName(root), root, false, size, null), "")]);
        var pending = new Queue<(string Path, string Relative, int Depth)>(); pending.Enqueue((root, "", 0));
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (pending.TryDequeue(out var directory))
        {
            if (!visited.Add(directory.Path) || directory.Depth > 32 || visited.Count + files.Count > 10000) throw new InvalidDataException("Inventory limit exceeded");
            var children = (await remote.Session.ListAsync(directory.Path, remote.Token)).Where(entry => entry.Name is not "." and not "..").ToArray();
            if (race is not null) { race.SourceAvailable = true; race.InspectListing(children, directory.Depth == 0, directory.Path); }
            foreach (var entry in children)
            {
                if (entry.IsSymbolicLink || VisionaryRaceState.IsVerificationMetadata(entry) || metadata.IsCompletionMetadata(entry)) continue;
                _ = Clean(entry.Name);
                if (entry.Name.Contains('/')) throw new InvalidDataException("Invalid listing filename");
                var relative = directory.Relative.Length == 0 ? entry.Name : directory.Relative + "/" + entry.Name;
                if (entry.IsDirectory) { if (race?.AllowsDirectory(entry) != false) pending.Enqueue((Join(directory.Path, entry.Name), relative, directory.Depth + 1)); }
                else if (entry.Size is > 0 && race?.AllowsFile(entry) != false) files.Add(new(entry with { FullPath = Join(root, relative) }, relative));
            }
        }
        race?.EndListing(); return new(files);
    }
    private async Task<List<LinuxApiFile>> Candidates(ConnectionProfile source, ConnectionProfile target, InventoryResult inventory, string root,
        VisionaryRaceState? race, List<LinuxApiJob> snapshot, CancellationToken token)
    {
        var result = new List<LinuxApiFile>(); var allPresent = inventory.Files.Count > 0;
        string? Status(Guid id) => snapshot.FirstOrDefault(job => job.Id == id)?.State;
        var directories = new HashSet<string>(StringComparer.Ordinal);
        await using var remote = await Open(target, token);
        if (inventory.Files.Any(file => file.Relative.Length > 0)) await EnsureDirectory(remote, root);
        foreach (var file in inventory.Files.OrderBy(file => file.Entry.Name.EndsWith(".sfv", StringComparison.OrdinalIgnoreCase) ? 0 : file.Entry.Name.EndsWith(".nfo", StringComparison.OrdinalIgnoreCase) ? 2 : 1))
        {
            var destination = file.Relative.Length == 0 ? root : Join(root, file.Relative);
            var directory = destination[..Math.Max(1, destination.LastIndexOf('/'))];
            if (directories.Add(directory)) await EnsureDirectory(remote, directory);
            var size = await remote.Session.GetSizeAsync(destination, remote.Token);
            if (size == file.Entry.Size) { race?.SkipExistingTarget(file.Entry.FullPath); continue; }
            allPresent = false;
            if (size > file.Entry.Size)
            { if (race is null || race.SourceComplete) throw new IOException("Destination is larger than source; refusing to overwrite"); race.DeferExistingTarget(file.Entry.FullPath); continue; }
            if (race is not null && !race.NeedsTransfer(file.Entry.FullPath, file.Entry.Size, file.Entry.ModifiedAt, Status)) continue;
            result.Add(new(source, target, file.Entry.FullPath, destination, file.Entry.Size!.Value));
        }
        if (race is not null) race.DestinationVerified = race.SourceComplete && allPresent;
        return result;
    }
    private static async Task EnsureDirectory(ApiRemote remote, string path)
    {
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select((_, index) => "/" + string.Join('/', path.Split('/', StringSplitOptions.RemoveEmptyEntries).Take(index + 1))))
        {
            var probe = await remote.Session.ExecuteCommandAsync("CWD " + part, remote.Token);
            if (probe.StatusCode < 400) continue;
            var create = await remote.Session.ExecuteCommandAsync("MKD " + part, remote.Token);
            if (create.StatusCode >= 400)
            { probe = await remote.Session.ExecuteCommandAsync("CWD " + part, remote.Token); if (probe.StatusCode >= 400) throw new IOException("Target directory is unavailable: " + part); }
        }
    }
    private async Task<ApiRemote> Open(ConnectionProfile profile, CancellationToken token)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var lease = await LinuxSessionLease.Acquire([profile], timeout.Token); var session = new FtpRemoteSession(); session.ProtocolMessage += _log;
        try { await session.ConnectAsync(profile, timeout.Token); return new(session, lease, timeout); }
        catch { try { await session.DisposeAsync(); } finally { lease.Dispose(); timeout.Dispose(); } throw; }
    }
    private sealed class ApiRemote(FtpRemoteSession session, LinuxSessionLease lease, CancellationTokenSource timeout) : IAsyncDisposable
    {
        public FtpRemoteSession Session => session; public CancellationToken Token => timeout.Token;
        public async ValueTask DisposeAsync() { try { await session.DisposeAsync(); } finally { lease.Dispose(); timeout.Dispose(); } }
    }
}
