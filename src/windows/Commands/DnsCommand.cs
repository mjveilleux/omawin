using System.Net;
using System.Net.NetworkInformation;
using Microsoft.Win32;

namespace Omawin.Windows;

// omawin dns [preset] [adapter] (8.19): which DNS each connected adapter uses, and a switch to
// Cloudflare, Quad9, Google or back to your router's; omawin dns encrypt [on|off] [adapter] turns
// DNS over HTTPS on for the servers Windows knows an encrypted address for. Reading needs nothing;
// a change needs admin: it says which adapters change to what, then Windows asks once for all.
static class DnsCommand
{
    sealed record Adapter(string Name, int Index, Guid Id, IReadOnlyList<IPAddress> Servers, bool Manual);

    public static int Run(string[] args)
    {
        var adapters = Connected();
        if (args.Length == 0 || args[0] == "status")
        {
            if (adapters.Count == 0) return Program.Say("No network adapter is connected.");
            foreach (var a in adapters)
            {
                Console.WriteLine($"  {a.Name}: {DnsPresets.Describe(a.Servers, a.Manual)}");
                if (a.Servers.Any(s => DnsPresets.Encryptable(s) != null))
                    Console.WriteLine($"    {DnsPresets.DescribeEncryption(a.Servers, s => Encrypted(a.Id, s))}");
            }
            Console.WriteLine();
            Console.WriteLine("omawin dns cloudflare | quad9 | google | router (the one your network gives) [adapter]; omawin dns encrypt [on|off]");
            return 0;
        }
        if (args[0] == "encrypt") return Encrypt(adapters, args[1..]);

        var preset = DnsPresets.Find(args[0]);
        if (preset == null) return Program.Fail($"no DNS called \"{args[0]}\": {string.Join(", ", DnsPresets.All.Select(p => p.Name))}");
        var picked = Pick(adapters, args[1..], out var error);
        if (picked == null) return Program.Fail(error!);

        foreach (var a in picked) Console.WriteLine($"  {a.Name}: {DnsPresets.Describe(a.Servers, a.Manual)}  →  {preset.Label}");
        if (!Program.Ask("Changing DNS needs admin: Windows asks once. Go ahead?")) return 0;
        var failed = Elevated.Run(status => AdminScripts.Dns(picked.Select(a => a.Index).ToList(), preset.Servers, status));
        if (failed != null) return Program.Fail(failed);
        return Program.Say(preset.DohTemplate != null
            ? "Done (undo: omawin dns router). omawin dns encrypt turns DNS over HTTPS on for it."
            : "Done (undo: omawin dns router).");
    }

    // omawin dns encrypt [on|off] [adapter]: DNS over HTTPS for the servers in use that Windows has
    // an encrypted address for (Cloudflare, Google, Quad9); others are left as they are, and said.
    static int Encrypt(List<Adapter> adapters, string[] args)
    {
        bool on = true;
        if (args.Length > 0 && args[0] is "on" or "off") { on = args[0] == "on"; args = args[1..]; }
        var picked = Pick(adapters, args, out var error);
        if (picked == null) return Program.Fail(error!);
        var servers = new List<(Guid, string)>();
        foreach (var a in picked)
        {
            var known = a.Servers.Where(s => DnsPresets.Encryptable(s) != null).ToList();
            if (known.Count == 0)
            {
                Console.WriteLine($"  {a.Name}: {DnsPresets.Describe(a.Servers, a.Manual)}: Windows has no encrypted address for these (omawin dns cloudflare, quad9 or google first)");
                continue;
            }
            var change = known.Where(s => Encrypted(a.Id, s) != on).ToList();
            Console.WriteLine($"  {a.Name}: {DnsPresets.DescribeEncryption(a.Servers, s => Encrypted(a.Id, s))}" +
                (change.Count == 0 ? ", nothing to change" : $"  →  {(on ? "encrypted (DNS over HTTPS, nothing sent in the clear)" : "not encrypted")}"));
            servers.AddRange(change.Select(s => (a.Id, s.ToString())));
        }
        if (servers.Count == 0) return 0;
        if (!Program.Ask("That needs admin: Windows asks once. Go ahead?")) return 0;
        var failed = Elevated.Run(status => AdminScripts.DnsEncryption(servers, on, status));
        return failed == null ? Program.Say(on ? "Done (undo: omawin dns encrypt off)." : "Done.") : Program.Fail(failed);
    }

    static List<Adapter>? Pick(List<Adapter> adapters, string[] words, out string? error)
    {
        var named = string.Join(' ', words);
        var picked = named.Length == 0 ? adapters : adapters.Where(a => a.Name.Contains(named, StringComparison.OrdinalIgnoreCase)).ToList();
        error = picked.Count > 0 ? null : named.Length == 0 ? "no network adapter is connected" : $"no connected adapter called \"{named}\"";
        return picked.Count > 0 ? picked : null;
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
            if (ip.GatewayAddresses.Count == 0 || !Guid.TryParse(n.Id, out var id)) continue;
            int index;
            try { index = ip.GetIPv4Properties()?.Index ?? ip.GetIPv6Properties().Index; }
            catch (NetworkInformationException) { continue; }
            list.Add(new Adapter(n.Name, index, id, [.. ip.DnsAddresses], DnsPresets.IsManual(NameServer(n.Id))));
        }
        return list;
    }

    // Whether this server has DNS over HTTPS turned on for this adapter (readable without admin).
    static bool Encrypted(Guid adapter, IPAddress server)
    {
        using var key = Registry.LocalMachine.OpenSubKey(DnsPresets.DohKey(adapter, server));
        return key?.GetValue("DohFlags") is long flags && flags != 0;
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
