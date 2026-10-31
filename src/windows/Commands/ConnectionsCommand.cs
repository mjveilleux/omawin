using System.Net;

namespace Omawin.Windows;

// omawin connections [name] (8.20): which apps are talking to other machines now, and to whom
// (names looked up, a second at most; an address that doesn't answer in time stays a number).
static class ConnectionsCommand
{
    public static int Run(string[] args)
    {
        var typed = args.Length > 0 ? string.Join(' ', args) : null;
        var processes = Snapshot.Take();
        var byPid = processes.GroupBy(p => p.Pid).ToDictionary(g => g.Key, g => g.First());
        string AppOf(uint pid) => byPid.TryGetValue(pid, out var p) ? ProcessGroups.AppOf(p, byPid) : $"pid {pid} (ended)";

        var apps = Connections.ByApp(Sockets.All(), AppOf);
        if (typed != null) apps = apps.Where(a => a.App.Contains(typed, StringComparison.OrdinalIgnoreCase)).ToList();
        if (apps.Count == 0) return Program.Say(typed == null ? "No app is connected to another machine right now." : $"Nothing matching \"{typed}\" is connected right now.");

        var names = Lookup(apps.SelectMany(a => a.Remotes.Select(r => r.Remote)).Distinct().ToList(), TimeSpan.FromSeconds(1));
        foreach (var a in apps)
        {
            Console.WriteLine($"{a.App}  ({a.Total} connection{(a.Total == 1 ? "" : "s")})");
            foreach (var (remote, port, count) in a.Remotes.Take(typed == null ? 6 : 50))
            {
                var name = names.TryGetValue(remote, out var n) ? n : remote.ToString();
                Console.WriteLine($"    {name}  {SocketTable.Service(port)}{(count > 1 ? $"  ×{count}" : "")}");
            }
            if (typed == null && a.Remotes.Count > 6) Console.WriteLine($"    … {a.Remotes.Count - 6} more (omawin connections {a.App})");
        }
        return 0;
    }

    // Reverse lookups, all at once, for at most `wait`: the answers in by then are used.
    static Dictionary<IPAddress, string> Lookup(List<IPAddress> addresses, TimeSpan wait)
    {
        var tasks = addresses.ToDictionary(a => a, a => Dns.GetHostEntryAsync(a));
        try { Task.WaitAll([.. tasks.Values], wait); } catch (AggregateException) { }
        return tasks.Where(t => t.Value.IsCompletedSuccessfully && !string.IsNullOrEmpty(t.Value.Result.HostName))
            .ToDictionary(t => t.Key, t => t.Value.Result.HostName);
    }
}
