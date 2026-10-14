using System.Net;
using System.Net.Sockets;
using IoFtp.Core.Transport;
internal static class GlobalPortChecks
{
    public static void Run()
    {
        var reserved = new TcpListener(IPAddress.Loopback, 0); reserved.Start();
        var busy = ((IPEndPoint)reserved.LocalEndpoint).Port;
        try
        {
            try { var unexpected = FtpActivePortRange.Start(IPAddress.Loopback, busy, busy); unexpected.Stop(); throw new Exception("Occupied active port accepted"); }
            catch (IOException exception) { if (!exception.Message.Contains($"{busy}-{busy}")) throw; }
        }
        finally { reserved.Stop(); }
        var selected = FtpActivePortRange.Start(IPAddress.Loopback, busy, busy);
        try { if (((IPEndPoint)selected.LocalEndpoint).Port != busy) throw new Exception("Active listener outside range"); }
        finally { selected.Stop(); }
        try { var invalid = FtpActivePortRange.Start(IPAddress.Loopback, 20000, 10000); invalid.Stop(); throw new Exception("Invalid active range accepted"); }
        catch (ArgumentException) { }
        var automatic = FtpActivePortRange.Start(IPAddress.Loopback, 0, 0); automatic.Stop();
        GlobalSettingsChecks.Run();
        Console.WriteLine("PASS: active range real socket binding, occupied range rejection, invalid bounds, optional automatic ports and global active/passive persistence.");
    }
}
