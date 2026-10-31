namespace Omawin.Windows;

// omawin open-with [on [types…]|off|settings] (8.25): pictures, Markdown, JSON and logs in
// omawin's viewer. No argument: each type the viewer shows, whether omawin's offered for it and
// what Windows opens it with now.
static class OpenWithCommand
{
    public static int Run(string[] args)
    {
        var what = args.Length > 0 ? args[0].ToLowerInvariant() : "status";
        switch (what)
        {
            case "status":
                Console.WriteLine("type      omawin   opens with now");
                foreach (var t in OpenWith.Supported)
                {
                    var (progId, name) = OpenWithRegistry.Choice(t);
                    Console.WriteLine(OpenWith.Line(t, OpenWithRegistry.Offered(t), progId, name));
                }
                return Program.Say("omawin open-with on [types] offers it; Windows then asks you to pick it (Settings › Default apps), since no program may set itself.");
            case "on":
                List<string> unknown = [];
                var types = args.Length > 1 ? OpenWith.Types(args[1..], out unknown) : [.. OpenWith.Default];
                if (unknown.Count > 0)
                    return Program.Fail($"the viewer doesn't show {string.Join(", ", unknown)}; it shows {string.Join(" ", OpenWith.Supported)} (or: pictures, text, all)");
                string exe = Path.Combine(AppContext.BaseDirectory, OpenWith.ExeName);
                if (!File.Exists(exe)) return Program.Fail($"{exe} isn't there (omawin install -Step helpers builds it)");
                OpenWithRegistry.Apply(OpenWith.Plan(exe, types));
                OpenWithRegistry.OpenSettings();
                return Program.Say($"omawin's viewer is offered for {string.Join(" ", types.Select(t => "." + t))}. In the Settings page that opened, pick \"omawin viewer\" for each type you want (Windows lets only you set a default).");
            case "settings":
                OpenWithRegistry.OpenSettings();
                return 0;
            case "off":
                OpenWithRegistry.Remove();
                return Program.Say("omawin's viewer is no longer offered. Types you'd given it open with Windows' default again (Windows may ask once).");
            default:
                return Program.Fail("omawin open-with [on [types…]|off|settings]");
        }
    }
}
