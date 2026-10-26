using System.Security.Cryptography;
namespace IoFtp.Linux;
internal static class SmokeTests
{
    public static async Task<int> Run(string[] args)
    {
        if (args.FirstOrDefault() == "--ftp-check") return await FtpCheck(args);
        var directory = Path.Combine(Path.GetTempPath(), "fluxftp-linux-check-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var payload = RandomNumberGenerator.GetBytes(256 * 1024 + 7);
        var source = Path.Combine(directory, "source.bin"); await File.WriteAllBytesAsync(source, payload);
        var target = Path.Combine(directory, "target.bin");
        var job = new Job { File = new FileItem("source.bin", source, false, payload.Length), Destination = target };
        var partial = target + ".fluxftp-" + job.Id.ToString("N") + ".part";
        await File.WriteAllBytesAsync(partial, payload[..12345]);
        await Transfers.Run(job, new InlineProgress(_ => { }), _ => { }, CancellationToken.None);
        if (!(await File.ReadAllBytesAsync(target)).SequenceEqual(payload) || File.Exists(partial)) throw new Exception("Copy/resume failed");
        try { await Transfers.Run(job, new InlineProgress(_ => { }), _ => { }, CancellationToken.None); throw new Exception("Existing destination overwritten"); }
        catch (IOException) { }
        if (!(await File.ReadAllBytesAsync(target)).SequenceEqual(payload)) throw new Exception("Existing destination changed");
        Console.WriteLine("PASS: local transfer, partial resume, final placement and existing-file protection");
        return 0;
    }
    public static async Task<int> FtpCheck(string[] args)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = timeout.Token;
        var profile = new IoFtp.Core.Models.ConnectionProfile(Guid.NewGuid(), "Linux fixture", "127.0.0.1", int.Parse(args[1]), "test",
            args[2] == "ftp" ? IoFtp.Core.Models.TransferProtocol.Ftp : IoFtp.Core.Models.TransferProtocol.FtpsExplicit,
            Password: "test", AllowInvalidCertificate: true,
            Options: new IoFtp.Core.Models.SiteOptions(UseOpenSslTls: args[2] == "openssl"));
        await using var session = new IoFtp.Core.Transport.FtpRemoteSession();
        await session.ConnectAsync(profile, token);
        var listing = await session.ListAsync("/", token);
        var source = listing.Single(entry => entry.Name == "fixture.bin");
        var payload = await File.ReadAllBytesAsync(args[3], token);
        using var downloaded = new MemoryStream();
        long bytes = 0;
        await session.DownloadAsync(source.FullPath, downloaded, 0, new InlineProgress(value => bytes = value), token);
        if (!downloaded.ToArray().SequenceEqual(payload) || bytes != payload.Length) throw new Exception("FTP download/progress mismatch");
        using var resumed = new MemoryStream(); resumed.Write(payload.AsSpan(0, 123));
        await session.DownloadAsync(source.FullPath, resumed, 123, null, token);
        if (!resumed.ToArray().SequenceEqual(payload)) throw new Exception("FTP resume mismatch");
        using var upload = new MemoryStream(payload);
        await session.UploadAsync("/uploaded-" + args[2] + ".bin", upload, 0, null, token);
        using var verify = new MemoryStream();
        await session.DownloadAsync("/uploaded-" + args[2] + ".bin", verify, 0, null, token);
        if (!verify.ToArray().SequenceEqual(payload)) throw new Exception("FTP upload mismatch");
        if (args[2] == "ftp")
        {
            await using var destination = new IoFtp.Core.Transport.FtpRemoteSession();
            await destination.ConnectAsync(profile with { Id = Guid.NewGuid() }, token);
            await session.FxpToAsync(destination, "/fixture.bin", "/fxp-result.bin", token);
            using var result = new MemoryStream();
            await destination.DownloadAsync("/fxp-result.bin", result, 0, null, token);
            if (!result.ToArray().SequenceEqual(payload)) throw new Exception("FXP mismatch");
            Console.WriteLine("PASS: direct FTP FXP byte-for-byte verification");
        }
        Console.WriteLine("PASS: " + args[2] + " login, listing, download, progress, resume and upload on " + System.Runtime.InteropServices.RuntimeInformation.OSDescription);
        return 0;
    }
}
