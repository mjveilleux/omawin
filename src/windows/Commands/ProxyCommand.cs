using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Omawin.Windows;

// omawin proxy (8.19): this account's system proxy, as Settings › Network › Proxy sets it. No admin.
//   omawin proxy                          what's set
//   omawin proxy set <host:port> [no-proxy names…]   set it and turn it on
//   omawin proxy on | off                 the switch (the server is kept)
//   omawin proxy script <url> | script off   a setup script (PAC)
// Every change says what it was before, so it can be put back.
static partial class ProxyCommand
{
    const uint INTERNET_OPTION_REFRESH = 37, INTERNET_OPTION_SETTINGS_CHANGED = 39;

    [LibraryImport("wininet.dll", EntryPoint = "InternetSetOptionW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool InternetSetOption(nint handle, uint option, nint buffer, uint length);

    public static int Run(string[] args)
    {
        using var key = Registry.CurrentUser.CreateSubKey(ProxySettings.Key);
        var before = Read(key);
        var verb = args.Length > 0 ? args[0].ToLowerInvariant() : "status";
        switch (verb)
        {
            case "status":
                foreach (var line in ProxySettings.Describe(before)) Console.WriteLine(line);
                Console.WriteLine();
                Console.WriteLine("omawin proxy set <host:port> [names that skip it], on, off, script <url>");
                return 0;
            case "set":
            {
                if (args.Length < 2) return Program.Fail("omawin proxy set <host:port> [names that skip it]");
                var server = ProxySettings.NormalizeServer(args[1], out var why);
                if (server == null) return Program.Fail(why!);
                key.SetValue("ProxyServer", server, RegistryValueKind.String);
                key.SetValue("ProxyOverride", ProxySettings.Bypass(args[2..]), RegistryValueKind.String);
                key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
                return Done(before, key);
            }
            case "on":
                if (before.Server is not { Length: > 0 }) return Program.Fail("no proxy server is set: omawin proxy set <host:port>");
                key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
                return Done(before, key);
            case "off":
                key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                return Done(before, key);
            case "script":
                if (args.Length < 2) return Program.Fail("omawin proxy script <url>, or script off");
                if (args[1] == "off") key.DeleteValue("AutoConfigURL", throwOnMissingValue: false);
                else if (ProxySettings.IsPacUrl(args[1])) key.SetValue("AutoConfigURL", args[1], RegistryValueKind.String);
                else return Program.Fail($"\"{args[1]}\" isn't an http(s) or file address");
                return Done(before, key);
            default:
                return Program.Fail("omawin proxy [set <host:port> [names…] | on | off | script <url>|off]");
        }
    }

    static ProxyState Read(RegistryKey key)
    {
        // Detect settings automatically lives in the binary DefaultConnectionSettings (byte 8,
        // flag 8); read only here.
        using var connections = key.OpenSubKey("Connections");
        bool detect = connections?.GetValue("DefaultConnectionSettings") is byte[] { Length: > 8 } b && (b[8] & 8) != 0;
        return new(key.GetValue("ProxyEnable") is 1, key.GetValue("ProxyServer") as string, key.GetValue("ProxyOverride") as string,
            key.GetValue("AutoConfigURL") as string, detect);
    }

    // Running programs that use WinINet re-read the settings when told; the rest at their next start.
    static int Done(ProxyState before, RegistryKey key)
    {
        InternetSetOption(0, INTERNET_OPTION_SETTINGS_CHANGED, 0, 0);
        InternetSetOption(0, INTERNET_OPTION_REFRESH, 0, 0);
        var now = Read(key);
        return Program.Say(string.Join("; ", ProxySettings.Describe(now)) + $" (was: {string.Join("; ", ProxySettings.Describe(before))}).");
    }
}
