using System.Net;
using System.Text.Json.Serialization;
using IoFtp.Core.Models;
namespace IoFtp.Linux;
internal sealed record LinuxApiOptions(bool Enabled = false, string Address = "127.0.0.1", int Port = 55477,
    string Password = "", int MaxSpreadJobs = 150, int RecheckSeconds = 3, int TimeoutMinutes = 15)
{
    public void Validate()
    {
        if (!IPAddress.TryParse(Address, out _) || Port is < 1 or > 65535) throw new ArgumentException("Enter a numeric listen address and port 1-65535.");
        if (Enabled && (Password.Length is < 8 or > 128 || Password.Any(c => c is < '!' or > '~'))) throw new ArgumentException("API password must be 8-128 printable ASCII characters without spaces (RaceTrade compatibility).");
        if (MaxSpreadJobs is < 1 or > 2048 || RecheckSeconds is < 1 or > 60 || TimeoutMinutes is < 1 or > 1440) throw new ArgumentException("Invalid API scheduling limits.");
    }
}
internal sealed record LinuxApiFile(ConnectionProfile Source, ConnectionProfile Target, string SourcePath, string DestinationPath, long Size);
internal sealed record LinuxApiJob(Guid Id, string State, long Bytes, string Error);
internal sealed record LinuxTransferRequest(
    [property: JsonPropertyName("src_site")] string? SourceSite = null,
    [property: JsonPropertyName("src_path")] string? SourcePath = null,
    [property: JsonPropertyName("src_section")] string? SourceSection = null,
    [property: JsonPropertyName("dst_site")] string? TargetSite = null,
    [property: JsonPropertyName("dst_path")] string? TargetPath = null,
    [property: JsonPropertyName("dst_section")] string? TargetSection = null, string? Name = null);
internal sealed record LinuxSpreadRequest(string Name, string Section, string[] Sites,
    [property: JsonPropertyName("sites_dlonly")] string[]? DownloadOnly = null, string? Profile = null);
internal sealed record LinuxRawRequest(string Command, string[]? Sites = null,
    [property: JsonPropertyName("sites_all")] bool AllSites = false, string? Path = null,
    [property: JsonPropertyName("path_section")] string? Section = null, int? Timeout = null);
