namespace IoFtp.Desktop.Services;

internal static class PassiveFxpAverage
{
    public static long? Calculate(long transferredBytes, TimeSpan elapsed)
    {
        if (transferredBytes <= 0 || elapsed <= TimeSpan.Zero) return null;
        var rate = transferredBytes / elapsed.TotalSeconds;
        return rate >= long.MaxValue ? long.MaxValue : (long)rate;
    }
}
