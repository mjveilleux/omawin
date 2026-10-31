using System.Net;
using System.Net.NetworkInformation;
using Microsoft.Win32;

namespace Omawin.Windows;

// omawin dns [preset] [adapter] (8.19): which DNS each connected adapter uses, and a switch to
// Cloudflare, Quad9, Google or back to your router's. Reading needs nothing; a change needs admin:
// it says which adapters change to what, then Windows asks once for all of them.
static class DnsCommand
{
    sealed record Adapter(string Name, int Index, IReadOnlyList<IPAddress> Servers, bool Manual);

    public static int Run(string[] args)
    {
        var adapters = Connected();
        if (args.Length == 0 || args[0] == "status")
        {
            if (adapters.Count == 0) return Program.Say("No network adapter is connected.");
            foreach (var a in adapters) Console.WriteLine($"  {a.Name}: {DnsPresets.Describe(a.Servers, a.Manual)}");
            Console.WriteLine();
            Console.WriteLine("omawin dns cloudflare | quad9 | google | router (the one your network gives) [adapter]");
            return 0;
        }
        var preset = DnsPresets.Find(args[0]);
        if (preset == null) return Program.Fail($"no DNS called \"{args[0]}\": {string.Join(", ", DnsPresets.All.Select(p => p.Name))}");
        var named = string.Join(' ', args[1..]);
        var picked = named.Length == 0 ? adapters : adapters.Where(a => a.Name.Contains(named, StringComparison.OrdinalIgnoreCase)).ToList();
        if (picked.Count == 0) return Program.Fail(named.Length == 0 ? "no network adapter is connected" : $"no connected adapter called \"{named}\"");

        foreach (var a in picked) Console.WriteLine($"  {a.Name}: {DnsPresets.Describe(a.Servers, a.Manual)}  →  {preset.Label}");
        if (!Program.Ask("Changing DNS needs admin: Windows asks once. Go ahead?")) return 0;
        var failed = Elevated.Run(status => AdminScripts.Dns(picked.Select(a => a.Index).ToList(), preset.Servers, status));
        return failed == null ? Program.Say($"Done (undo: omawin dns router).") : Program.Fail(failed);
    }

    // The adapters that are up and reach a network (a gateway): the ones whose DNS is in use; not
    // loopback or tunnels. Naming one (omawin dns quad9 wi-fi) changes only that one.
    static List<Adapter> Connected()
    {
        var list = new List<Adapter>();
        foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (n.OperationalStatus != OperationalStatus.Up || n.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            var ip = n.GetIPProperties();
            if (ip.GatewayAddresses.Count == 0) continue;
            int index;
            try { index = ip.GetIPv4Properties()?.Index ?? ip.GetIPv6Properties().Index; }
            catch (NetworkInformationException) { continue; }
            list.Add(new Adapter(n.Name, index, [.. ip.DnsAddresses], DnsPresets.IsManual(NameServer(n.Id))));
        }
        return list;
    }

    // The servers set by hand for this adapter, IPv4's then IPv6's (readable without admin).
    static string? NameServer(string id)
    {
        string? found = null;
        foreach (var stack in new[] { "Tcpip", "Tcpip6" })
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{stack}\Parameters\Interfaces\{id}");
            if (key?.GetValue("NameServer") is string s && s.Trim().Length > 0) found = s;
        }
        return found;
    }
}
