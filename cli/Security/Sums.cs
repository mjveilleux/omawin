using System.Security.Cryptography;
using System.Text;

namespace Omawin.Cli.Security;

// SHA256SUMS.txt as release.yml writes it (sha256sum's format): "<64 hex>  <file name>" per line,
// a "*" before the name meaning binary mode. A download is used only when its hash is listed
// and matches (docs/plan/09-for-everyone.md §1).
public static class Sums
{
    public static Dictionary<string, string> Parse(string text)
    {
        var sums = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length < 66 || line[64] != ' ') continue;
            var hash = line[..64];
            if (!hash.All(Uri.IsHexDigit)) continue;
            var name = line[65..].TrimStart(' ', '*').Trim();
            if (name.Length == 0 || name.Contains('/') || name.Contains('\\') || name.Contains("..")) continue; // names, not paths
            sums[name] = hash.ToLowerInvariant();
        }
        return sums;
    }

    public static string Hash(Stream s) => Convert.ToHexStringLower(SHA256.HashData(s));

    // True only when the file's name is listed and its contents hash to the listed value.
    public static bool Matches(string file, IReadOnlyDictionary<string, string> sums)
    {
        if (!sums.TryGetValue(Path.GetFileName(file), out var expected)) return false;
        using var s = File.OpenRead(file);
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(s)), Encoding.ASCII.GetBytes(expected));
    }
}
