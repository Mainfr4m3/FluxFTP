namespace IoFtp.Desktop.Services;
internal sealed record ApiTransferRequest(string? SrcSite = null, string? SrcPath = null, string? SrcSection = null,
    string? DstSite = null, string? DstPath = null, string? DstSection = null, string? Name = null);
