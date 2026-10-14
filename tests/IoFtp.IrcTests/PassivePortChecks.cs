using System.Reflection;
using System.Text;
using IoFtp.Core.Models;
using IoFtp.Core.Transport;
internal static class PassivePortChecks
{
    public static async Task Run()
    {
        var options = new SiteOptions(PassivePortMin: 10000, PassivePortMax: 20000);
        FtpPassivePortRange.Ensure(options, 10000, "test"); FtpPassivePortRange.Ensure(options, 20000, "test");
        FtpPassivePortRange.Ensure(new(), 65535, "default");
        foreach (var port in new[] { 0, 9999, 20001, 65536 })
        { try { FtpPassivePortRange.Ensure(options, port, "test"); throw new Exception("Outside port accepted"); } catch (FtpPassivePortException) { } }
        foreach (var range in new[] { (0, 10000), (20000, 10000), (1, 65536), (-1, 20000) })
        { try { FtpPassivePortRange.Validate(range.Item1, range.Item2); throw new Exception("Invalid range accepted"); } catch (ArgumentException) { } }
        foreach (var epsv in new[] { false, true })
        {
            var session = new FtpRemoteSession(); var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            using var input = new MemoryStream(Encoding.ASCII.GetBytes(epsv ? "229 Entering Extended Passive Mode (|||42480|)\r\n" : "227 Entering Passive Mode (127,0,0,1,165,240)\r\n"));
            using var reader = new StreamReader(input); using var output = new MemoryStream();
            using var writer = new StreamWriter(output, Encoding.ASCII, leaveOpen: true) { AutoFlush = true };
            typeof(FtpRemoteSession).GetField("_reader", flags)!.SetValue(session, reader); typeof(FtpRemoteSession).GetField("_writer", flags)!.SetValue(session, writer);
            typeof(FtpRemoteSession).GetField("_profile", flags)!.SetValue(session, new ConnectionProfile(Guid.NewGuid(), "test", "127.0.0.1", 21, "test", TransferProtocol.Ftp, Options: options));
            typeof(FtpRemoteSession).GetProperty("Capabilities")!.SetValue(session, new HashSet<string>(epsv ? ["EPSV"] : []));
            try { await (Task)typeof(FtpRemoteSession).GetMethod("OpenPassiveEndpointAsync", flags)!.Invoke(session, [CancellationToken.None])!; throw new Exception("Rejected endpoint returned"); }
            catch (FtpPassivePortException exception) { if (!exception.Message.Contains("42480")) throw; }
        }
        ProfileStoreChecks.Run(); await SecureReverseFxpChecks.Run();
        Console.WriteLine("PASS: passive range boundaries, invalid configuration, PASV/EPSV rejection before connection, INI persistence and FXP regression.");
    }
}
