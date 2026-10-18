namespace IoFtp.Desktop.Services;

internal static class RaceFileWindow
{
    public static int Capacity(int downloads, int uploads, int sources, int owned, int sourceBusy, int targetBusy, bool borrow)
    {
        var free = Math.Max(0, Math.Min(downloads - sourceBusy, uploads - targetBusy));
        var share = (uploads + Math.Max(1, sources) - 1) / Math.Max(1, sources);
        return borrow ? free : Math.Max(0, Math.Min(free, share - owned));
    }

    public static IReadOnlyList<T> Select<T>(IEnumerable<T> files, Func<T, string> destination,
        ISet<string> reserved, int capacity)
    {
        var result = new List<T>();
        foreach (var file in files)
        {
            if (result.Count >= capacity) break;
            if (reserved.Add(destination(file))) result.Add(file);
        }
        return result;
    }
}
