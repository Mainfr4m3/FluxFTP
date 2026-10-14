using System.Reflection;
using IoFtp.Core.Abstractions;
using System.Text;
using IoFtp.Core.Models;
using IoFtp.Core.Transport;
using IoFtp.Desktop.Models;
using IoFtp.Desktop.Services;
internal static class TransferPolicyChecks
{
    public static async Task Run()
    {
        var text = new SiteOptions(ForceBinaryMode: false, AsciiRules: "*.txt\nregex:^README$", NoAsciiResume: true);
        if (!FtpTransferMode.IsAscii("/release/FILE.TXT", text) || !FtpTransferMode.IsAscii("README", text) || FtpTransferMode.IsAscii("file.rar", text)) throw new Exception("ASCII patterns");
        if (FtpTransferMode.IsAscii("file.txt", text with { ForceBinaryMode = true }) || FtpTransferMode.CanResume("file.txt", text) || !FtpTransferMode.CanResume("file.rar", text)) throw new Exception("Binary override/ASCII resume");
        try { FtpTransferMode.Validate("regex:("); throw new Exception("Invalid regex accepted"); } catch (ArgumentException) { }
        var settings = new GlobalSettings(FxpSmallerAction: ExistingTargetAction.Resume, FxpSameAction: ExistingTargetAction.Overwrite, FxpLargerAction: ExistingTargetAction.Skip);
        if (FxpExistingFilePolicy.Resolve(settings, 100, 50, true) != ExistingTargetAction.Resume || FxpExistingFilePolicy.Resolve(settings, 100, 100, true) != ExistingTargetAction.Overwrite || FxpExistingFilePolicy.Resolve(settings, 100, 150, true) != ExistingTargetAction.Skip || FxpExistingFilePolicy.Resolve(settings, 100, 0, true) != ExistingTargetAction.Overwrite) throw new Exception("RACE policy");
        var session = new FtpRemoteSession();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        using var input = new MemoryStream(Encoding.ASCII.GetBytes("200 ASCII\r\n200 Binary\r\n"));
        using var reader = new StreamReader(input);
        using var output = new MemoryStream();
        using var writer = new StreamWriter(output, Encoding.ASCII, leaveOpen: true) { AutoFlush = true, NewLine = "\r\n" };
        typeof(FtpRemoteSession).GetField("_profile", flags)!.SetValue(session, new ConnectionProfile(Guid.NewGuid(), "test", "localhost", 21, "test", TransferProtocol.Ftp, Options: text));
        typeof(FtpRemoteSession).GetField("_reader", flags)!.SetValue(session, reader); typeof(FtpRemoteSession).GetField("_writer", flags)!.SetValue(session, writer);
        var prepare = typeof(FtpRemoteSession).GetMethod("PrepareDataCommandAsync", flags)!;
        await (Task)prepare.Invoke(session, ["RETR file.txt", CancellationToken.None, null])!;
        await (Task)prepare.Invoke(session, ["RETR file.rar", CancellationToken.None, null])!;
        if (Encoding.ASCII.GetString(output.ToArray()) != "TYPE A\r\nTYPE I\r\n") throw new Exception("Pooled transfer mode switching");
        using var restartInput = new MemoryStream(Encoding.ASCII.GetBytes("350 Restart accepted\r\n"));
        using var restartReader = new StreamReader(restartInput);
        typeof(FtpRemoteSession).GetField("_reader", flags)!.SetValue(session, restartReader);
        typeof(FtpRemoteSession).GetField("_profile", flags)!.SetValue(session, new ConnectionProfile(Guid.NewGuid(), "source", "localhost", 21, "test", TransferProtocol.Ftp));
        typeof(FtpRemoteSession).GetField("_fxpResumeOffset", flags)!.SetValue(session, 50L);
        var target = new FtpRemoteSession();
        using var rejectInput = new MemoryStream(Encoding.ASCII.GetBytes("500 REST unsupported\r\n"));
        using var rejectReader = new StreamReader(rejectInput);
        using var rejectOutput = new MemoryStream();
        using var rejectWriter = new StreamWriter(rejectOutput, Encoding.ASCII, leaveOpen: true) { AutoFlush = true, NewLine = "\r\n" };
        typeof(FtpRemoteSession).GetField("_reader", flags)!.SetValue(target, rejectReader);
        typeof(FtpRemoteSession).GetField("_writer", flags)!.SetValue(target, rejectWriter);
        typeof(FtpRemoteSession).GetField("_profile", flags)!.SetValue(target, new ConnectionProfile(Guid.NewGuid(), "target", "localhost", 21, "test", TransferProtocol.Ftp));
        try { await (Task)typeof(FtpRemoteSession).GetMethod("StartAndCompleteFxpAsync", flags)!.Invoke(session, [target, "file.rar", "file.rar", CancellationToken.None])!; throw new Exception("REST rejection accepted"); }
        catch (FtpResumeRejectedException) { }
        if (Encoding.ASCII.GetString(rejectOutput.ToArray()) != "REST 50\r\n" || session.LastFxpTransferElapsed != TimeSpan.Zero) throw new Exception("Rejected REST must not send STOR or report completion");
        var testDirectory = Path.Combine(Path.GetTempPath(), "FluxFTP-ascii-resume-" + Guid.NewGuid());
        Directory.CreateDirectory(testDirectory);
        try
        {
            var file = Path.Combine(testDirectory, "file.txt");
            File.WriteAllText(file + ".FLUXFTP.PART", "old unfinished text");
            var result = await LocalDownload.RunAsync(file, async (stream, offset) => { if (offset != 0) throw new Exception("ASCII resume offset"); await stream.WriteAsync(Encoding.ASCII.GetBytes("new")); }, null, CancellationToken.None, allowResume: false);
            if (result.ResumeOffset != 0 || File.ReadAllText(file) != "new") throw new Exception("ASCII restart retained old data");
        }
        finally { Directory.Delete(testDirectory, true); }
        var race = new VisionaryRaceState(new());
        var expected = new[] { new RemoteEntry("file.txt", "/release/file.txt", false, 4, null) };
        var converted = new[] { new RemoteEntry("file.txt", "/release/file.txt", false, 3, null) };
        if (RaceDestinationVerifier.Accepts(expected, converted, race, _ => true)) throw new Exception("ASCII size difference without COMPLETE must not verify");
        var completed = converted.Append(new RemoteEntry("COMPLETE", "/release/COMPLETE", true, null, null)).ToArray();
        if (!RaceDestinationVerifier.Accepts(expected, completed, race, _ => true) || RaceDestinationVerifier.Accepts(expected, completed, race)) throw new Exception("ASCII COMPLETE verification must preserve binary exact-size checks");
        await SecureReverseFxpChecks.Run(); GlobalSettingsChecks.Run();
        Console.WriteLine("PASS: ASCII wildcard/regex, binary override, ASCII resume policy, mode switching, RACE target actions, REST on both FXP servers and settings persistence.");
    }
}
