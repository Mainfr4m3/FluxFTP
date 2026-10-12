namespace IoFtp.Desktop.Services;

internal static class RaceSlotBudget
{
    public static int Transfers(int total, int uploads, int downloads, int auxiliary) =>
        Math.Max(0, Math.Min(Math.Max(0, total - auxiliary), Math.Max(0, uploads) + Math.Max(0, downloads)));
}
