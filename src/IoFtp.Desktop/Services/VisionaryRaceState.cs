using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using IoFtp.Core.Abstractions;

namespace IoFtp.Desktop.Services;

internal sealed record VisionaryRaceOptions(
    string CompleteFlag = @"(?:^|[\s\[\]()._-])(?:COMPLETE|FINISHED)(?:$|[\s\[\]()._-])|100%",
    int RefreshSeconds = 3, int TimeoutMinutes = 60, string RushOptionsPath = "")
{
    private static string FilePath => Path.Combine(AppContext.BaseDirectory, "FluxFTP-visionary-race.json");
    public static VisionaryRaceOptions Load() => File.Exists(FilePath)
        ? JsonSerializer.Deserialize<VisionaryRaceOptions>(File.ReadAllText(FilePath)) ?? new() : new();
    public Regex Validate()
    {
        if (RefreshSeconds is < 1 or > 60 || TimeoutMinutes is < 1 or > 1440)
            throw new ArgumentException("Refresh must be 1–60 seconds; timeout must be 1–1440 minutes.");
        if (string.IsNullOrWhiteSpace(CompleteFlag)) throw new ArgumentException("A completeflag pattern is required.");
        var regex = new Regex(CompleteFlag, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (regex.IsMatch("")) throw new ArgumentException("completeflag must not match empty text.");
        return regex;
    }
    public void Save()
    {
        Validate();
        VisionaryTransferProfiles.Load(RushOptionsPath);
        File.WriteAllText(FilePath + ".tmp", JsonSerializer.Serialize(this));
        File.Move(FilePath + ".tmp", FilePath, true);
    }
}

internal sealed class VisionaryRaceState(VisionaryRaceOptions options, VisionaryTransferProfiles? profiles = null, Func<DateTimeOffset>? utcNow = null)
{
    private sealed record Tracked(Guid Job, long? Size, DateTimeOffset? Modified, bool Final, int Attempts);
    private readonly Dictionary<string, Tracked> _files = new(StringComparer.Ordinal);
    private sealed record PayloadSnapshot(string Signature, DateTimeOffset Changed, bool Complete);
    private readonly Dictionary<string, PayloadSnapshot> _payloads = new(StringComparer.Ordinal);
    private readonly VisionaryTransferProfiles _profiles = profiles ?? VisionaryTransferProfiles.Load(options.RushOptionsPath);
    private bool IsFinal(string path) => _profiles.ForDirectory(Parent(path)) is null ? SourceComplete :
        _payloads.TryGetValue(Parent(path), out var payload) && payload.Complete;
    private static string Parent(string path) => path[..Math.Max(0, path.LastIndexOf('/'))];
    public bool AllowsFile(RemoteEntry file) => _profiles.ForDirectory(Parent(file.FullPath))?.Allows(file) ?? true;
    public Regex CompleteFlag { get; } = options.Validate();
    public bool SourceComplete { get; set; }
    public bool SourceAvailable { get; set; }
    public bool DestinationVerified { get; set; }
    private readonly Dictionary<string, bool> _directoryCompletion = new(StringComparer.Ordinal);
    private bool _lastComplete;
    public void BeginListing() { SourceComplete = false; SourceAvailable = false; DestinationVerified = false; _directoryCompletion.Clear(); }
    public void EndListing()
    {
        if (_directoryCompletion.Count > 0) SourceComplete = _directoryCompletion.Values.All(value => value);
        if (_lastComplete && !SourceComplete)
            foreach (var key in _files.Keys.ToArray()) _files[key] = _files[key] with { Final = false };
        _lastComplete = SourceComplete;
    }
    public static bool IsVerificationMetadata(RemoteEntry entry) => entry.Name.StartsWith(".ioFTPD", StringComparison.OrdinalIgnoreCase) ||
        entry.Name.EndsWith("-MISSING", StringComparison.OrdinalIgnoreCase) ||
        entry.Name.EndsWith(".missing", StringComparison.OrdinalIgnoreCase) || entry.Name.EndsWith(".bad", StringComparison.OrdinalIgnoreCase) ||
        entry.Name.StartsWith("(iNCOMPLETE)", StringComparison.OrdinalIgnoreCase);
    public IEnumerable<Guid> Jobs => _files.Values.Select(file => file.Job);
    public bool IsCompletionMarker(RemoteEntry entry) => _profiles.ForDirectory(Parent(entry.FullPath)) is null &&
        (entry.IsDirectory || entry.Size == 0) && CompleteFlag.IsMatch(entry.Name);
    public void InspectListing(IEnumerable<RemoteEntry> children, bool releaseRoot, string? directory = null, Func<RemoteEntry, bool>? includeFile = null)
    {
        var entries = children.ToArray();
        if (entries.Any(child => child.Name.StartsWith("REASON-", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(child.Name, @"(?:^|[\s._\[(-])NUKE(?:D)?(?:$|[\s._\])-])", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100))))
            throw new InvalidOperationException("NUKE / REASON marker stopped the VISIONARY race.");
        directory ??= entries.Length > 0 ? Parent(entries[0].FullPath) : "";
        var profile = _profiles.ForDirectory(directory);
        if (profile is null)
        {
            var markerComplete = entries.Any(IsCompletionMarker) && !entries.Any(entry =>
                entry.Name.EndsWith("-MISSING", StringComparison.OrdinalIgnoreCase) ||
                entry.Name.EndsWith(".missing", StringComparison.OrdinalIgnoreCase) || entry.Name.EndsWith(".bad", StringComparison.OrdinalIgnoreCase));
            if (releaseRoot) SourceComplete = markerComplete;
            if (entries.Any(entry => !entry.IsDirectory && !IsVerificationMetadata(entry) &&
                !new[] { ".nfo", ".m3u", ".jpg", ".png", ".txt" }.Contains(Path.GetExtension(entry.Name).ToLowerInvariant())) || entries.Any(IsCompletionMarker))
                _directoryCompletion[directory] = markerComplete;
            return;
        }
        var accepted = entries.Where(entry => profile.Allows(entry) && (includeFile?.Invoke(entry) ?? true)).OrderBy(entry => entry.FullPath, StringComparer.Ordinal).ToArray();
        var signature = JsonSerializer.Serialize(accepted.Select(entry => new { entry.FullPath, entry.Size, entry.ModifiedAt }));
        var now = utcNow?.Invoke() ?? DateTimeOffset.UtcNow;
        var changed = _payloads.TryGetValue(directory, out var previous) && previous.Signature == signature ? previous.Changed : now;
        var complete = accepted.Any(profile.Completes) && !accepted.Any(profile.Pending) && now - changed >= TimeSpan.FromSeconds(Math.Max(2, options.RefreshSeconds * 2));
        _payloads[directory] = new(signature, changed, complete);
        _directoryCompletion[directory] = complete;
        if (releaseRoot) SourceComplete = complete;
    }

    public bool NeedsTransfer(string path, long? size, DateTimeOffset? modified, Func<Guid, string?> status)
    {
        if (!_files.TryGetValue(path, out var prior)) return true;
        var state = status(prior.Job);
        if (state is null or "Cancelled") throw new OperationCanceledException("A race job was removed or cancelled.");
        if (state == "Failed")
        {
            // An unfinished source can be preallocated and unchanged in size.
            // Wait for a new snapshot/final marker rather than exhausting retries on partial data.
            if (!IsFinal(path) && prior.Size == size && prior.Modified == modified) return false;
            var attempts = _profiles.ForDirectory(Parent(path))?.Attempts ?? 3;
            if (prior.Final == IsFinal(path) && prior.Size == size && prior.Modified == modified && prior.Attempts >= attempts)
                throw new IOException($"Race file failed after {attempts} attempts: {path}");
            return true;
        }
        if (state != "Completed") return false;
        return prior.Size != size || prior.Modified != modified || (IsFinal(path) && !prior.Final);
    }

    public void Track(string path, long? size, DateTimeOffset? modified, Guid job)
    {
        var final = IsFinal(path);
        var attempts = _files.TryGetValue(path, out var old) && old.Size == size && old.Modified == modified && old.Final == final
            ? old.Attempts + 1 : 1;
        _files[path] = new(job, size, modified, final, attempts);
    }

    public bool Finished(Func<Guid, string?> status) => SourceComplete && _files.Count > 0 &&
        _files.All(pair => IsFinal(pair.Key) && pair.Value.Final && status(pair.Value.Job) == "Completed");
}
