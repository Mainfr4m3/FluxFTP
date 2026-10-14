using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
using IoFtp.Core.Abstractions;

namespace IoFtp.Desktop.Services;

internal sealed record VisionaryRaceOptions(
    string CompleteFlag = VisionaryRaceOptions.DefaultCompleteFlag,
    int RefreshSeconds = 3, int TimeoutMinutes = 5, string RushOptionsPath = "",
    VisionaryTransferProfiles.SavedEntry[]? ImportedProfiles = null)
{
    public const string LegacyCompleteFlag = @"(?:^|[\s\[\]()._-])(?:COMPLETE|FINISHED)(?:$|[\s\[\]()._-])|100%";
    public const string DefaultCompleteFlag = @"(?:^|[\s\[\]()._-])(?:COMPLETED?|FINISHED)(?:$|[\s\[\]()._-])|(?<![\d.,])100(?:[.,]0+)?\s*%";
    // The configured lifetime includes admission wait as well as the active watch.
    public TimeSpan EffectiveTimeout => TimeSpan.FromMinutes(Math.Clamp(TimeoutMinutes, 1, 1440));
    private static string FilePath => Path.Combine(AppContext.BaseDirectory, "FluxFTP-visionary-race.json");
    public static VisionaryRaceOptions Load()
    {
        var options = File.Exists(FilePath)
            ? JsonSerializer.Deserialize<VisionaryRaceOptions>(File.ReadAllText(FilePath)) ?? new() : new();
        if (options.ImportedProfiles is null && !string.IsNullOrWhiteSpace(options.RushOptionsPath))
        {
            options.Save(); // Migrate the previously selected file once while it is still available.
            options = JsonSerializer.Deserialize<VisionaryRaceOptions>(File.ReadAllText(FilePath))!;
        }
        return options;
    }
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
        var entries = ImportedProfiles;
        if (entries is null)
        {
            if (string.IsNullOrWhiteSpace(RushOptionsPath)) entries = [];
            else if (File.Exists(RushOptionsPath)) entries = VisionaryTransferProfiles.Import(RushOptionsPath);
            else
            {
                var previous = File.Exists(FilePath) ? JsonSerializer.Deserialize<VisionaryRaceOptions>(File.ReadAllText(FilePath)) : null;
                if (previous?.ImportedProfiles is null || !string.Equals(previous.RushOptionsPath, RushOptionsPath, StringComparison.OrdinalIgnoreCase))
                    throw new FileNotFoundException("Import the transfer profile file once before removing it.", RushOptionsPath);
                entries = previous.ImportedProfiles;
            }
        }
        VisionaryTransferProfiles.Load(savedEntries: entries);
        File.WriteAllText(FilePath + ".tmp", JsonSerializer.Serialize(this with { ImportedProfiles = entries }));
        File.Move(FilePath + ".tmp", FilePath, true);
    }
}

internal sealed class VisionaryRaceState(VisionaryRaceOptions options, VisionaryTransferProfiles? profiles = null, Func<DateTimeOffset>? utcNow = null)
{
    public RaceStartTrace? StartTrace { get; set; }
    private sealed record Tracked(Guid Job, long? Size, DateTimeOffset? Modified, bool Final, int Attempts);
    private readonly Dictionary<string, Tracked> _files = new(StringComparer.Ordinal);
    private sealed record PayloadSnapshot(string Signature, DateTimeOffset Changed, bool Complete);
    private readonly Dictionary<string, PayloadSnapshot> _payloads = new(StringComparer.Ordinal);
    private readonly VisionaryTransferProfiles _profiles = profiles ?? VisionaryTransferProfiles.Load(options.RushOptionsPath, options.ImportedProfiles);
    public bool IsFinal(string path) => _profiles.ForDirectory(Parent(path)) is null or { MarkerBased: true } ? SourceComplete :
        _payloads.TryGetValue(Parent(path), out var payload) && payload.Complete;
    private static string Parent(string path) => path[..Math.Max(0, path.LastIndexOf('/'))];
    public string FileFilterDetail(RemoteEntry file) => _profiles.ForDirectory(Parent(file.FullPath))?.FileFilterDetail ?? "no profile";
    public string FolderFilterDetail(RemoteEntry directory) => _profiles.ForDirectory(Parent(directory.FullPath))?.FolderFilterDetail ?? "no profile";
    public bool AllowsFile(RemoteEntry file) => _profiles.ForDirectory(Parent(file.FullPath))?.Allows(file) ?? true;
    public bool AllowsDirectory(RemoteEntry directory) => _profiles.ForDirectory(Parent(directory.FullPath))?.AllowsFolder(directory.Name) ?? true;
    private bool MatchesComplete(RemoteEntry entry) => CompleteFlag.IsMatch(entry.Name) ||
        _profiles.ForDirectory(Parent(entry.FullPath)) is { MarkerBased: true } profile && profile.MatchesComplete(entry.Name);
    public Regex CompleteFlag { get; } = options.Validate();
    public bool SourceComplete { get; set; }
    public bool SourceAvailable { get; set; }
    public bool SourceAnnouncedComplete { get; set; }
    public bool DestinationAnnouncedComplete { get; set; }
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
    private readonly HashSet<string> _pendingPayloads = new(StringComparer.Ordinal);
    public bool HasPendingPayload => _pendingPayloads.Count > 0;
    public void BeginListing() { _pendingPayloads.Clear(); SourceComplete = false; SourceAvailable = false; DestinationVerified = false; _directoryCompletion.Clear(); }
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
        (MatchesComplete(entry) || Regex.IsMatch(entry.Name, @"\b(?:INCOMPLETE|UNFINISHED)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)));
    public bool IsCompletionMarker(RemoteEntry entry)
    {
        if (!IsCompletionMetadata(entry) || !MatchesComplete(entry)) return false;
        if (NukeDetector.DetectName(entry.Name).IsNuked || entry.Name.StartsWith("REASON-", StringComparison.OrdinalIgnoreCase)) return false;
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
        foreach (var file in entries.Where(entry => !entry.IsDirectory && entry.Size is null or <= 0 &&
            !IsVerificationMetadata(entry) && !IsCompletionMetadata(entry) && AllowsFile(entry) && (includeFile?.Invoke(entry) ?? true)))
            _pendingPayloads.Add(file.FullPath);
        if (entries.Any(child => child.Name.StartsWith("REASON-", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(child.Name, @"(?:^|[\s._\[(-])NUKE(?:D)?(?:$|[\s._\])-])", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100))))
            throw new InvalidOperationException("NUKE / REASON marker stopped the VISIONARY race.");
        directory ??= entries.Length > 0 ? Parent(entries[0].FullPath) : "";
        var profile = _profiles.ForDirectory(directory);
        if (profile is null || profile.MarkerBased)
        {
            var markerComplete = (entries.Any(IsCompletionMarker) || SourceAnnouncedComplete &&
                entries.Any(entry => !entry.IsDirectory && !IsVerificationMetadata(entry) && !IsCompletionMetadata(entry))) && !entries.Any(entry =>
                IsCompletionMetadata(entry) && !IsCompletionMarker(entry) ||
                entry.Name.EndsWith("-MISSING", StringComparison.OrdinalIgnoreCase) ||
                entry.Name.EndsWith(".missing", StringComparison.OrdinalIgnoreCase) || entry.Name.EndsWith(".bad", StringComparison.OrdinalIgnoreCase) ||
                SourceAnnouncedComplete && !entry.IsDirectory && !IsVerificationMetadata(entry) && !IsCompletionMetadata(entry) && entry.Size == 0);
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
        if (_existingTargets.Contains(path)) return false;
        if (DestinationAlreadyComplete || size is null or <= 0) return false;
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

    private readonly HashSet<string> _existingTargets = new(StringComparer.OrdinalIgnoreCase);
    public void SkipExistingTarget(string path) => _existingTargets.Add(path);
    public bool IsSkipped(string path, Func<Guid, string?> status) => _existingTargets.Contains(path) || _files.TryGetValue(path, out var file) && status(file.Job) == "Skipped";
    public bool Finished(Func<Guid, string?> status) => DestinationAlreadyComplete || !HasPendingPayload && SourceComplete && (_files.Count + _existingTargets.Count) > 0 &&
        _files.All(pair => status(pair.Value.Job) == "Skipped" || IsFinal(pair.Key) && pair.Value.Final && status(pair.Value.Job) == "Completed");
}
