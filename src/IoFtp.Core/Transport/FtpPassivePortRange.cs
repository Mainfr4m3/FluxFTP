using IoFtp.Core.Models;
namespace IoFtp.Core.Transport;
public static class FtpPassivePortRange
{
    public static void Validate(int min, int max)
    {
        if (min == 0 && max == 0) return;
        if (min < 1 || max > 65535 || max < min) throw new ArgumentException("Passive port range must have both endpoints between 1 and 65535, with start <= end.");
    }
    public static void Ensure(SiteOptions options, int port, string site)
    {
        Validate(options.PassivePortMin, options.PassivePortMax);
        if (port < 1 || port > 65535 || options.PassivePortMin != 0 && (port < options.PassivePortMin || port > options.PassivePortMax))
            throw new FtpPassivePortException($"{site}: server offered passive port {port}, outside the allowed range {(options.PassivePortMin == 0 ? "1-65535" : options.PassivePortMin + "-" + options.PassivePortMax)}. Check the site's passive port settings.");
    }
}
public sealed class FtpPassivePortException(string message) : IOException(message);
