using System.Text.RegularExpressions;

namespace IoFtp.Core.Transport;

public static class TransferVerification
{
    public static void EnsureAccepted(string response)
    {
        var plain = Regex.Replace(response, @"\x1B\[[0-9;?]*[ -/]*[@-~]", "");
        if (Regex.IsMatch(plain, @"VERIFICATION\s+FAILED|\bBADCRC\b|\b0SIZE\b|CRC[- ]Check:\s*Failed|0byte\s+file:\s*Deleted", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new TransferVerificationException("Server verification rejected the transfer: " + plain.Trim());
    }
}

public sealed class TransferVerificationException(string message) : IOException(message);
