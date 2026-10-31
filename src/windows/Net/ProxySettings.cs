using System.Text.RegularExpressions;

namespace Omawin.Windows;

// The system proxy (roadmap 8.19): the values Settings › Network › Proxy writes for this account
// (WinINet's Internet Settings, read by Edge, Chrome, .NET, winget and most programs), checked and
// described. Pure: Commands/ProxyCommand.cs reads and writes the registry and tells WinINet.
public sealed record ProxyState(bool Enabled, string? Server, string? Bypass, string? AutoConfigUrl, bool AutoDetect);

public static partial class ProxySettings
{
    public const string Key = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    // "host:port", or per scheme as WinINet takes it ("http=host:port;https=host:port").
    [GeneratedRegex(@"^(?<host>[A-Za-z0-9.-]{1,253}|\[[0-9A-Fa-f:.]{2,45}\]):(?<port>\d{1,5})$")]
    private static partial Regex HostPort();
    [GeneratedRegex(@"^(http|https|ftp|socks)=")]
    private static partial Regex Scheme();

    // The server as WinINet stores it, or null with why. "http://host:port/" is taken too (what
    // people paste), its scheme and slash dropped.
    public static string? NormalizeServer(string text, out string? why)
    {
        why = null;
        var parts = text.Trim().Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) { why = "no proxy server given"; return null; }
        var clean = new List<string>();
        foreach (var raw in parts)
        {
            var part = raw;
            string prefix = "";
            if (Scheme().Match(part) is { Success: true } m) { prefix = m.Value; part = part[m.Length..]; }
            else if (parts.Length > 1) { why = $"\"{raw}\": with several, each is scheme=host:port"; return null; }
            foreach (var s in new[] { "http://", "https://", "socks://", "socks5://" })
                if (part.StartsWith(s, StringComparison.OrdinalIgnoreCase)) { part = part[s.Length..]; break; }
            part = part.TrimEnd('/');
            var hp = HostPort().Match(part);
            if (!hp.Success || !int.TryParse(hp.Groups["port"].Value, out var port) || port is < 1 or > 65535)
            {
                why = $"\"{raw}\" isn't host:port (like proxy.example.com:8080)";
                return null;
            }
            clean.Add(prefix + part);
        }
        return string.Join(';', clean);
    }

    // The bypass list as WinINet keeps it: names and wildcards separated by ';', "<local>" (names
    // without a dot) always kept so the local network never goes through the proxy.
    public static string Bypass(IEnumerable<string> names)
    {
        var list = new List<string>();
        foreach (var n in names.SelectMany(n => n.Split([';', ',', ' '], StringSplitOptions.RemoveEmptyEntries)))
            if (!list.Contains(n, StringComparer.OrdinalIgnoreCase) && !n.Equals("<local>", StringComparison.OrdinalIgnoreCase)) list.Add(n);
        list.Add("<local>");
        return string.Join(';', list);
    }

    public static bool IsPacUrl(string text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeFile);

    public static IEnumerable<string> Describe(ProxyState s)
    {
        if (s.AutoConfigUrl is { Length: > 0 } pac) yield return $"Setup script: {pac}";
        if (s.AutoDetect) yield return "Detect settings automatically: on (WPAD)";
        if (s.Enabled && s.Server is { Length: > 0 } server)
        {
            yield return $"Proxy: on, {server}";
            if (s.Bypass is { Length: > 0 } b) yield return $"  not for: {b.Replace(";", ", ")}";
        }
        else if (s.Server is { Length: > 0 } off) yield return $"Proxy: off ({off} is kept for omawin proxy on)";
        else yield return "Proxy: none";
    }
}
