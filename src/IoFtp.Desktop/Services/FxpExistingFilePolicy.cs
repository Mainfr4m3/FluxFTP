using IoFtp.Desktop.Models;

namespace IoFtp.Desktop.Services;

internal static class FxpExistingFilePolicy
{
    public static bool ShouldSkip(GlobalSettings settings, long? sourceSize, long? targetSize, bool visionaryRace = false)
    {
        if (visionaryRace || sourceSize is null || sourceSize < 0 || targetSize is null || targetSize < 0) return false;
        return targetSize < sourceSize ? settings.FxpSkipSmallerTarget
            : targetSize == sourceSize ? settings.FxpSkipSameTarget : settings.FxpSkipLargerTarget;
    }
}
