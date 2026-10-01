using IoFtp.Core.Abstractions;

namespace IoFtp.Desktop.Services;

internal static class RaceDestinationVerifier
{
    // A source marker never proves that the destination accepted its SFV payload.
    public static bool Accepts(IReadOnlyList<RemoteEntry> expected, IReadOnlyList<RemoteEntry> actual, VisionaryRaceState race)
    {
        if (actual.Any(entry => entry.Name.EndsWith(".missing", StringComparison.OrdinalIgnoreCase) ||
            entry.Name.EndsWith(".bad", StringComparison.OrdinalIgnoreCase) || NukeDetector.DetectName(entry.Name).IsNuked)) return false;
        foreach (var file in expected)
        {
            var found = actual.FirstOrDefault(entry => !entry.IsDirectory && entry.Name.Equals(file.Name, StringComparison.OrdinalIgnoreCase));
            if (found is null || file.Size is null || found.Size != file.Size) return false;
        }
        return !expected.Any(entry => entry.Name.EndsWith(".sfv", StringComparison.OrdinalIgnoreCase) || entry.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) || actual.Any(race.IsCompletionMarker);
    }
}
