using IoFtp.Desktop.Services;

internal static class FxpSpeedChecks
{
    public static void Run()
    {
        void Check(bool value) { if (!value) throw new Exception("FXP speed regression"); }
        string Row(string file, string rate) {
            var parts = Enumerable.Repeat("", 19).ToArray();
            parts[0] = "cid "; parts[10] = "STOR " + file; parts[17] = "123"; parts[18] = rate;
            return string.Join('|', parts);
        }
        Check(!FxpSpeedParser.TryReadIoFtpdTransfer(Row("other.rar", "100"), "target.rar", true, out _, out _));
        Check(!FxpSpeedParser.TryReadIoFtpdTransfer(Row("target.rar.extra", "100"), "target.rar", true, out _, out _));
        Check(FxpSpeedParser.TryReadIoFtpdTransfer(Row("/release/target.rar", "0"), "target.rar", true, out var bytes, out var speed) && bytes == 123 && speed == 0);
        var row = Row("target.rar", "1024");
        Check(FxpSpeedParser.TryReadIoFtpdTransfer(row, "target.rar", true, out _, out speed) && speed == 1048576);
        Check(!FxpSpeedParser.TryReadIoFtpdTransfer(row + "\n" + row, "target.rar", true, out _, out _));
        Check(!FxpSpeedParser.TryReadDrFtpdTransfer("-> UP 75MB/s from user - other.rar", "target.rar", true, out _));
        Check(FxpSpeedParser.TryReadDrFtpdTransfer("-> UP 0MB/s from user - target.rar", "target.rar", true, out speed) && speed == 0);
        Check(FxpSpeedParser.ParseDrFtpdSpeed("75MB") == 75000000);
        Check(FxpSpeedParser.ParseDrFtpdSpeed("75B") == 75);
        Check(FxpSpeedParser.ParseDrFtpdSpeed("1MiB") == 1048576);
        Check(FxpSpeedParser.IsPreallocatedSize(1000, 1000));
        Check(!FxpSpeedParser.IsPreallocatedSize(100, 1000));
        Check(FxpSpeedParser.ClampProgress(2000, 1000) == 1000);
        Console.WriteLine("PASS: FXP speed matching, ambiguous rows, zero speeds, units and preallocation checks.");
    }
}
