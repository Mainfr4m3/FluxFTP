namespace IoFtp.Desktop.Services;

internal static class QueueOrder
{
    public static List<T> Move<T>(IEnumerable<T> items, ISet<T> selected, string direction)
    {
        var result = items.ToList();
        if (direction == "Top") return result.Where(selected.Contains).Concat(result.Where(item => !selected.Contains(item))).ToList();
        if (direction == "Bottom") return result.Where(item => !selected.Contains(item)).Concat(result.Where(selected.Contains)).ToList();
        if (direction == "Up")
            for (var i = 1; i < result.Count; i++)
                if (selected.Contains(result[i]) && !selected.Contains(result[i - 1]))
                    (result[i - 1], result[i]) = (result[i], result[i - 1]);
        if (direction == "Down")
            for (var i = result.Count - 2; i >= 0; i--)
                if (selected.Contains(result[i]) && !selected.Contains(result[i + 1]))
                    (result[i + 1], result[i]) = (result[i], result[i + 1]);
        return result;
    }
}
