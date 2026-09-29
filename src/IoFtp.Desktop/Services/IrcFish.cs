using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;

namespace IoFtp.Desktop.Services;

// FiSH wire-format implementation using the existing Bouncy Castle primitive.
// Interoperability references are recorded in docs/slftp-compatibility.md.
internal static class IrcFish
{
    private const string Alphabet = "./0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public static void ValidateKey(string key)
    {
        var raw = key.StartsWith("cbc:", StringComparison.OrdinalIgnoreCase) ? key[4..] : key;
        if (Utf8.GetByteCount(raw) is < 4 or > 56 || raw.Any(char.IsControl))
            throw new ArgumentException("FiSH keys must contain 4–56 UTF-8 bytes (excluding cbc:).");
    }
    public static string Encrypt(string text, string key)
    {
        ValidateKey(key);
        var cbc = key.StartsWith("cbc:", StringComparison.OrdinalIgnoreCase);
        var input = Utf8.GetBytes(text);
        var blocks = new byte[((input.Length + 7) / 8) * 8 + (cbc ? 8 : 0)];
        if (cbc) RandomNumberGenerator.Fill(blocks.AsSpan(0, 8));
        input.CopyTo(blocks, cbc ? 8 : 0);
        var output = Transform(blocks, cbc ? key[4..] : key, cbc, true);
        return cbc ? "+OK *" + Convert.ToBase64String(output) : "+OK " + EncodeEcb(output);
    }
    public static bool TryDecrypt(string text, string key, out string clear)
    {
        clear = "";
        try
        {
            ValidateKey(key);
            var cbc = key.StartsWith("cbc:", StringComparison.OrdinalIgnoreCase);
            if (!text.StartsWith(cbc ? "+OK *" : "+OK ", StringComparison.Ordinal) || (!cbc && text.StartsWith("+OK *", StringComparison.Ordinal))) return false;
            var bytes = cbc ? Convert.FromBase64String(text[5..]) : DecodeEcb(text[4..]);
            if (bytes.Length % 8 != 0 || bytes.Length < (cbc ? 16 : 8) || bytes.Length > 8192) return false;
            var decrypted = Transform(bytes, cbc ? key[4..] : key, cbc, false);
            var start = cbc ? 8 : 0;
            var end = decrypted.Length;
            while (end > start && decrypted[end - 1] == 0) end--;
            clear = Utf8.GetString(decrypted, start, end - start);
            return clear.Length > 0 && !clear.Any(c => c is '\r' or '\n' or '\0');
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or CryptographicException) { return false; }
    }
    private static byte[] Transform(byte[] input, string key, bool cbc, bool encrypt)
    {
        var engine = new BlowfishEngine(); engine.Init(encrypt, new KeyParameter(Utf8.GetBytes(key)));
        var output = new byte[input.Length]; var previous = new byte[8]; var block = new byte[8];
        for (var offset = 0; offset < input.Length; offset += 8)
        {
            input.AsSpan(offset, 8).CopyTo(block);
            if (cbc && encrypt) for (var i = 0; i < 8; i++) block[i] ^= previous[i];
            engine.ProcessBlock(block, 0, output, offset);
            if (cbc && !encrypt) for (var i = 0; i < 8; i++) output[offset + i] ^= previous[i];
            if (cbc) (encrypt ? output : input).AsSpan(offset, 8).CopyTo(previous);
        }
        return output;
    }
    private static string EncodeEcb(byte[] bytes)
    {
        var text = new StringBuilder();
        for (var offset = 0; offset < bytes.Length; offset += 8)
        foreach (var half in new[] { 4, 0 })
        {
            var value = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + half, 4));
            for (var i = 0; i < 6; i++) { text.Append(Alphabet[(int)(value & 63)]); value >>= 6; }
        }
        return text.ToString();
    }
    private static byte[] DecodeEcb(string text)
    {
        if (text.Length % 12 != 0 || text.Length > 12288) throw new FormatException();
        var bytes = new byte[text.Length / 12 * 8];
        for (var offset = 0; offset < text.Length; offset += 12)
        for (var half = 0; half < 2; half++)
        {
            uint value = 0;
            for (var i = 0; i < 6; i++)
            {
                var digit = Alphabet.IndexOf(text[offset + half * 6 + i]);
                if (digit < 0 || (i == 5 && digit > 3)) throw new FormatException();
                value |= (uint)digit << (6 * i);
            }
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset / 12 * 8 + (half == 0 ? 4 : 0), 4), value);
        }
        return bytes;
    }
}
