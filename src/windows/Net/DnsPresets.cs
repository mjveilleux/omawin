using System.Net;

namespace Omawin.Windows;

// DNS from the menu (roadmap 8.19): the well-known public resolvers by name, and which one an
// adapter uses now. Pure: Commands/DnsCommand.cs reads the adapters and writes through one prompt.
public sealed record DnsPreset(string Name, string Label, IReadOnlyList<string> Servers);

public static class DnsPresets
{
    // "router": the adapter takes the DNS its network hands out (DHCP), as Windows does by default.
    // The others: both IPv4 servers, then both IPv6 ones. Cloudflare, Google and Quad9 are in
    // Windows 11's own DNS-over-HTTPS list, so Settings can turn encryption on for them.
    public static readonly DnsPreset[] All =
    [
        new("router", "your router (automatic)", []),
        new("cloudflare", "Cloudflare", ["1.1.1.1", "1.0.0.1", "2606:4700:4700::1111", "2606:4700:4700::1001"]),
        new("quad9", "Quad9 (blocks known malware sites)", ["9.9.9.9", "149.112.112.112", "2620:fe::fe", "2620:fe::9"]),
        new("google", "Google", ["8.8.8.8", "8.8.4.4", "2001:4860:4860::8888", "2001:4860:4860::8844"]),
    ];

    public static DnsPreset? Find(string name) =>
        All.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? (name.Equals("dhcp", StringComparison.OrdinalIgnoreCase) || name.Equals("auto", StringComparison.OrdinalIgnoreCase) ? All[0] : null);

    // What an adapter's DNS reads as: set by hand (static) or handed out by its network, and when
    // its servers are one preset's, that preset's name.
    public static string Describe(IReadOnlyList<IPAddress> servers, bool manual)
    {
        if (!manual) return servers.Count == 0 ? "your router (automatic)" : $"your router (automatic): {Join(servers)}";
        var match = All.Skip(1).FirstOrDefault(p => servers.Count > 0 && servers.All(s => p.Servers.Any(x => IPAddress.Parse(x).Equals(s))));
        return match != null ? $"{match.Label}: {Join(servers)}" : $"set by hand: {Join(servers)}";
    }

    static string Join(IReadOnlyList<IPAddress> servers) => string.Join(", ", servers);

    // The registry's NameServer for an adapter (Tcpip\Parameters\Interfaces\{id}): a list set by
    // hand, separated by commas or spaces; empty when the network hands them out.
    public static bool IsManual(string? nameServer) => !string.IsNullOrWhiteSpace(nameServer);
}
