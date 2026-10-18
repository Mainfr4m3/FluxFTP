namespace IoFtp.Desktop.Services;

internal static class RaceFileWindow
{
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
