using System.Net;

namespace Omawin.Windows;

// Which app is talking to the internet (roadmap 8.20): the established connections to other
// machines, by app, each remote counted once. Pure: the sockets and each pid's app come in.
public sealed record AppConnections(string App, IReadOnlyList<(IPAddress Remote, int Port, int Count)> Remotes)
{
    public int Total => Remotes.Sum(r => r.Count);
}

public static class Connections
{
    public static List<AppConnections> ByApp(IEnumerable<Socket> sockets, Func<uint, string> appOf) =>
        sockets.Where(s => s.External)
            .GroupBy(s => appOf(s.Pid), StringComparer.OrdinalIgnoreCase)
            .Select(g => new AppConnections(g.Key, g
                .GroupBy(s => (Remote: Unmapped(s.Remote!), s.RemotePort))
                .Select(r => (r.Key.Remote, r.Key.RemotePort, r.Count()))
                .OrderByDescending(r => r.Item3).ThenBy(r => r.Item2).ToList()))
            .OrderByDescending(a => a.Total).ThenBy(a => a.App, StringComparer.OrdinalIgnoreCase).ToList();

    // An IPv4 address carried in IPv6 (::ffff:1.2.3.4) is the same remote as 1.2.3.4.
    static IPAddress Unmapped(IPAddress a) => a.IsIPv4MappedToIPv6 ? a.MapToIPv4() : a;
}
