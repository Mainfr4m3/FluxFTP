using IoFtp.Desktop.Services;

internal static class FxpEstimateChecks
{
    public static void Run()
    {
        var now = TimeSpan.Zero;
        var estimate = new FxpLiveEstimate(1000, 100, 100, () => now);
        now = TimeSpan.FromSeconds(10);
        if (estimate.Read() != (100, 0) || estimate.Observe(300)) throw new Exception("Setup must not count as data transfer");
        estimate.Start();
        now += TimeSpan.FromSeconds(1);
        if (estimate.Read() != (200, 100)) throw new Exception("History interpolation or resume baseline incorrect");
        if (estimate.Observe(1000) || estimate.Observe(1200) || estimate.Read().Speed != 100) throw new Exception("Preallocated sizes became measured speed");
        if (!estimate.Observe(400) || estimate.Read().Speed != 160) throw new Exception("Upward smoothing must use 30 percent of observed average");
        now += TimeSpan.FromSeconds(1);
        if (!estimate.Observe(450) || estimate.Read().Speed != 164) throw new Exception("Observed average must exclude resume bytes");
        var displayed = estimate.Read().Bytes;
        if (estimate.Observe(200) || estimate.Read().Bytes < displayed) throw new Exception("Stale observations moved progress backward");
        now += TimeSpan.FromHours(2);
        if (estimate.Read().Bytes >= 1000) throw new Exception("Estimate must not prove completion");
        var unknown = new FxpLiveEstimate(0, 0, 100, () => now); unknown.Start(); now += TimeSpan.FromSeconds(1);
        if (unknown.Read().Bytes != 0 || unknown.Observe(500)) throw new Exception("Unknown total generated fabricated progress");
        var source = Guid.NewGuid(); var target = Guid.NewGuid();
        var history = new FxpPairSpeedHistory();
        if (history.Get(source, target) != 1_048_576) throw new Exception("Missing pair default differs from cbftp");
        history.Record(source, target, 10_485_760, TimeSpan.FromSeconds(2));
        if (history.Get(source, target) != 1_887_436 || history.Get(target, source) != 1_048_576) throw new Exception("Directional pair history smoothing incorrect");
        history.Record(source, target, 0, TimeSpan.FromSeconds(1));
        if (history.Get(source, target) != 1_887_436) throw new Exception("Invalid final measurement corrupted history");
        Console.WriteLine("PASS: FXP setup exclusion, 50 ms-compatible interpolation, resume offset, preallocation guard, smoothing, bounded progress and pair history.");
    }
}
