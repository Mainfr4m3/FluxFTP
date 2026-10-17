using IoFtp.Desktop.Models;
using IoFtp.Core.Models;
namespace IoFtp.Desktop.Services;
internal static class FxpExistingFilePolicy
{
    public static ExistingTargetAction Resolve(GlobalSettings s, long? source, long? target, bool race = false, bool retryIncompleteRace = false)
    {
        if (source is null || source < 0 || target is null || target < 0) return ExistingTargetAction.Overwrite;
        // Automatic race recovery may resume an incomplete binary target only
        // after the source is final. Explicit user Skip still takes precedence.
        if (race && retryIncompleteRace && target > 0 && target < source && s.FxpSmallerAction is null)
            return ExistingTargetAction.Resume;
        var action = target < source ? s.FxpSmallerAction ?? (s.FxpSkipSmallerTarget ? ExistingTargetAction.Skip : ExistingTargetAction.Overwrite)
            : target == source ? s.FxpSameAction ?? (s.FxpSkipSameTarget ? ExistingTargetAction.Skip : ExistingTargetAction.Overwrite)
            : s.FxpLargerAction ?? (s.FxpSkipLargerTarget ? ExistingTargetAction.Skip : ExistingTargetAction.Overwrite);
        if (action == ExistingTargetAction.Resume && (target == 0 || target >= source))
            return target == source && !race ? ExistingTargetAction.Skip : ExistingTargetAction.Overwrite;
        return action;
    }
    public static bool ShouldSkip(GlobalSettings s, long? source, long? target, bool visionaryRace = false) => Resolve(s, source, target, visionaryRace) == ExistingTargetAction.Skip;
}
