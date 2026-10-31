namespace Omawin.Windows;

// omawin wifi share [network] (8.21): the Wi-Fi you're on (or a saved one, by name) as a QR code in
// the terminal; a phone's camera joins from it. The password is shown only if you ask (--show).
static class WifiCommand
{
    public static int Run(string[] args)
    {
        bool show = args.Contains("--show");
        var named = string.Join(' ', args.Where(a => a != "--show"));
        string? profile = named.Length > 0 ? named : WlanProfile.Connected();
        if (profile == null)
        {
            var saved = WlanProfile.Saved();
            if (saved.Count == 0) return Program.Fail("no saved Wi-Fi networks on this PC");
            Console.Error.WriteLine("omawin: which network? (not connected, or Windows keeps the current one behind location access)");
            foreach (var s in saved) Console.Error.WriteLine("  omawin wifi share " + s);
            return 1;
        }
        var match = WlanProfile.Saved().FirstOrDefault(s => s.Equals(profile, StringComparison.OrdinalIgnoreCase)) ?? profile;
        var xml = WlanProfile.ProfileXml(match);
        if (xml == null) return Program.Fail($"no saved network called \"{profile}\" (omawin wifi share with no name lists them)");
        var network = WifiShare.FromProfile(xml);
        if (network == null) return Program.Fail($"Windows' profile for \"{profile}\" couldn't be read");
        if (WifiShare.IsEnterprise(network)) return Program.Fail($"{network.Ssid} signs in with an account, not a password: there's nothing to share as a code");
        if (network.Key == null && !WifiShare.IsOpen(network))
            return Program.Fail($"Windows keeps {network.Ssid}'s password from this account (it shows it to admin only); run this in an admin terminal to share it");

        Console.WriteLine();
        foreach (var line in WifiShare.Draw(WifiShare.Modules(WifiShare.QrText(network)))) Console.WriteLine("  " + line);
        Console.WriteLine();
        Console.WriteLine($"  {network.Ssid}: point a phone's camera at it to join.");
        if (network.Key != null) Console.WriteLine(show ? $"  Password: {network.Key}" : "  (omawin wifi share --show also prints the password)");
        return 0;
    }
}
