using System.Reflection;
using System.Text;
using IoFtp.Core.Transport;

internal static class BinaryModeChecks
{
    public static async Task Run()
    {
        await CheckListingLog();
        await CheckSscnReplies();
        var session = new FtpRemoteSession();
        using var output = new MemoryStream();
        using var writer = new StreamWriter(output, Encoding.ASCII, leaveOpen: true) { AutoFlush = true, NewLine = "\r\n" };
        using var input = new MemoryStream(Encoding.ASCII.GetBytes("200 Binary\r\n200 Binary\r\n500 Rejected\r\n"));
        using var reader = new StreamReader(input);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(FtpRemoteSession).GetField("_writer", flags)!.SetValue(session, writer);
        typeof(FtpRemoteSession).GetField("_reader", flags)!.SetValue(session, reader);
        var prepare = typeof(FtpRemoteSession).GetMethod("PrepareDataCommandAsync", flags)!;
        Task Prepare(string command) => (Task)prepare.Invoke(session, [command, CancellationToken.None])!;
        await Prepare("RETR archive.rar");
        await Prepare("STOR archive.rar");
        await Prepare("LIST /");
        if (Encoding.ASCII.GetString(output.ToArray()) != "TYPE I\r\nTYPE I\r\n")
            throw new Exception("Binary mode must be reaffirmed for each file, not directory listing.");
        try { await Prepare("STOR rejected.rar"); }
        catch (FtpCommandException)
        {
            Console.WriteLine("PASS: repeated binary negotiation for RETR/STOR; listing unchanged; rejection stops transfer.");
            return;
        }
        throw new Exception("Rejected TYPE I must stop transfer.");
    }

    private static async Task CheckSscnReplies()
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        foreach (var command in new[] { "CPSV", "PORT 127,0,0,1,1,1", "STOR file.rar", "RETR file.rar" })
        {
            var expected = command == "CPSV" ? 227 : command.StartsWith("PORT") ? 200 : 150;
            using var input = new MemoryStream(Encoding.ASCII.GetBytes($"220 SSCN:SERVER METHOD\r\n{expected} Actual command reply\r\n"));
            using var reader = new StreamReader(input);
            using var output = new MemoryStream();
            using var writer = new StreamWriter(output, leaveOpen: true) { AutoFlush = true };
            var session = new FtpRemoteSession();
            typeof(FtpRemoteSession).GetField("_reader", flags)!.SetValue(session, reader);
            typeof(FtpRemoteSession).GetField("_writer", flags)!.SetValue(session, writer);
            var task = (Task)typeof(FtpRemoteSession).GetMethod("FxpCommandAsync", flags)!.Invoke(session, [command, CancellationToken.None])!;
            await task;
            var response = task.GetType().GetProperty("Result")!.GetValue(task)!;
            if ((int)response.GetType().GetProperty("Code")!.GetValue(response)! != expected)
                throw new Exception("Delayed SSCN reply displaced " + command);
        }
        var responseType = typeof(FtpRemoteSession).GetNestedType("FtpResponse", BindingFlags.NonPublic)!;
        var classifier = typeof(FtpRemoteSession).GetMethod("IsSscnRoleReply", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var (code, message, accepted) in new[] { (220, "220 SSCN:SERVER METHOD", true), (200, "200 SSCN:CLIENT METHOD", true), (220, "220 Welcome", false), (550, "550 SSCN:SERVER METHOD", false) })
        {
            var response = Activator.CreateInstance(responseType, code, message)!;
            if ((bool)classifier.Invoke(null, [response])! != accepted) throw new Exception("Incorrect SSCN reply classification");
        }
        Console.WriteLine("PASS: delayed SSCN 220 replies preserve CPSV/PORT/STOR/RETR responses; greetings and rejections remain distinct.");
    }

    private static async Task CheckListingLog()
    {
        var row = "drwxrwxrwx   2 ioFTPD ioftpd 0 Oct 11 15:21 0DAY";
        using var input = new MemoryStream(Encoding.ASCII.GetBytes($"212-Status of /:\r\n{row}\r\n212 End of Status\r\n550-Listing failed\r\nPermission denied\r\n550 End\r\n"));
        using var reader = new StreamReader(input);
        var session = new FtpRemoteSession();
        var log = new List<string>();
        var details = new List<string>();
        session.ProtocolMessage += log.Add;
        session.ProtocolDetailMessage += details.Add;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(FtpRemoteSession).GetField("_reader", flags)!.SetValue(session, reader);
        var read = typeof(FtpRemoteSession).GetMethod("ReadResponseAsync", flags)!;
        var responseTask = (Task)read.Invoke(session, [CancellationToken.None])!;
        await responseTask;
        var response = responseTask.GetType().GetProperty("Result")!.GetValue(responseTask)!;
        var message = (string)response.GetType().GetProperty("Message")!.GetValue(response)!;
        if (!message.Contains(row) || log.Any(line => line.Contains(row)) || !details.Contains("< " + row) || log.Count != 2)
            throw new Exception("STAT rows must remain available for parsing/details, with only header/footer in the connection log.");
        await (Task)read.Invoke(session, [CancellationToken.None])!;
        if (!log.Contains("< Permission denied")) throw new Exception("Multiline errors must remain visible.");
        Console.WriteLine("PASS: STAT listing log suppression preserves directory data and errors.");
    }
}
