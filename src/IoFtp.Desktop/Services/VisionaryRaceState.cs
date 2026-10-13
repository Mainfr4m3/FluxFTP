using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
using IoFtp.Core.Abstractions;

namespace IoFtp.Desktop.Services;

internal sealed record VisionaryRaceOptions(
    string CompleteFlag = VisionaryRaceOptions.DefaultCompleteFlag,
    int RefreshSeconds = 3, int TimeoutMinutes = 5, string RushOptionsPath = "")
{
    public const string LegacyCompleteFlag = @"(?:^|[\s\[\]()._-])(?:COMPLETE|FINISHED)(?:$|[\s\[\]()._-])|100%";
    public const string DefaultCompleteFlag = @"(?:^|[\s\[\]()._-])(?:COMPLETED?|FINISHED)(?:$|[\s\[\]()._-])|(?<![\d.,])100(?:[.,]0+)?\s*%";
    // Bound the entire watch, including admission wait, even with older saved settings.
    public TimeSpan EffectiveTimeout => TimeSpan.FromMinutes(Math.Clamp(TimeoutMinutes, 1, 5));
    private static string FilePath => Path.Combine(AppContext.BaseDirectory, "FluxFTP-visionary-race.json");
    public static VisionaryRaceOptions Load() => File.Exists(FilePath)
        ? JsonSerializer.Deserialize<VisionaryRaceOptions>(File.ReadAllText(FilePath)) ?? new() : new();
    public Regex Validate()
    {
        if (RefreshSeconds is < 1 or > 60 || TimeoutMinutes is < 1 or > 1440)
            throw new ArgumentException("Refresh must be 1–60 seconds; timeout must be 1–1440 minutes.");
        if (string.IsNullOrWhiteSpace(CompleteFlag)) throw new ArgumentException("A completeflag pattern is required.");
        var regex = new Regex(CompleteFlag == LegacyCompleteFlag ? DefaultCompleteFlag : CompleteFlag,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
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
    public bool IsFinal(string path) => _profiles.ForDirectory(Parent(path)) is null ? SourceComplete :
        _payloads.TryGetValue(Parent(path), out var payload) && payload.Complete;
    private static string Parent(string path) => path[..Math.Max(0, path.LastIndexOf('/'))];
    public bool AllowsFile(RemoteEntry file) => _profiles.ForDirectory(Parent(file.FullPath))?.Allows(file) ?? true;
    public Regex CompleteFlag { get; } = options.Validate();
    public bool SourceComplete { get; set; }
    public bool SourceAvailable { get; set; }
    public bool DestinationVerified { get; set; }
    public bool DestinationAlreadyComplete { get; private set; }
    public void ConfirmDestinationComplete() { DestinationAlreadyComplete = true; DestinationVerified = true; }
    private string? _stopReason;
    public void Stop(string reason) => _stopReason ??= reason;
    public void ThrowIfStopped()
    {
        if (_stopReason is not null) throw new IOException("RACE destination blocked: " + _stopReason);
    }
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
    public bool IsCompletionMetadata(RemoteEntry entry) => (entry.IsDirectory || entry.Size == 0) &&
        (CompleteFlag.IsMatch(entry.Name) || Regex.IsMatch(entry.Name, @"\b(?:INCOMPLETE|UNFINISHED)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)));
    public bool IsCompletionMarker(RemoteEntry entry)
    {
        if (!IsCompletionMetadata(entry) || !CompleteFlag.IsMatch(entry.Name)) return false;
        if (Regex.IsMatch(entry.Name, @"\b(?:INCOMPLETE|UNFINISHED)\b|\bNOT[\s_-]+(?:COMPLETED?|FINISHED)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) return false;
        foreach (Match percent in Regex.Matches(entry.Name, @"(?<![\d.,])(?<value>\d+(?:[.,]\d+)?)\s*%",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            if (!decimal.TryParse(percent.Groups["value"].Value.Replace(',', '.'), NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var value) || value != 100) return false;
        return true;
    }
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
                IsCompletionMetadata(entry) && !IsCompletionMarker(entry) ||
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
        ThrowIfStopped();
        if (DestinationAlreadyComplete) return false;
        if (!_files.TryGetValue(path, out var prior)) return true;
        var state = status(prior.Job);
        if (state is null or "Cancelled") throw new OperationCanceledException("A race job was removed or cancelled.");
        if (state == "Skipped") return false;
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

    public bool IsSkipped(string path, Func<Guid, string?> status) => _files.TryGetValue(path, out var file) && status(file.Job) == "Skipped";
    public bool Finished(Func<Guid, string?> status) => DestinationAlreadyComplete || SourceComplete && _files.Count > 0 &&
        _files.All(pair => status(pair.Value.Job) == "Skipped" || IsFinal(pair.Key) && pair.Value.Final && status(pair.Value.Job) == "Completed");
}
