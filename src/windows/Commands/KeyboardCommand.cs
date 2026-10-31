using Microsoft.Win32;
using SystemMenu;

namespace Omawin.Windows;

// omawin keyboard (8.12, 8.13, 8.23): what your keys do differently, in one place.
//   omawin keyboard                   Caps Lock, the Copilot key, your snippets, Windows' own remap
//   omawin keyboard signin <remap>    Windows' own remap (works at the sign-in screen and in admin
//                                     windows): caps-escape, caps-control, swap-caps-control, caps-off;
//                                     off puts back what was there. Admin and a restart.
// The live ones are settings (input.caps-lock, input.copilot-key) the menu app's keyboard hook
// follows at once; snippets are snippets.txt in your account folder (KeyRules.cs reads both).
static class KeyboardCommand
{
    static string StateDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "launcher", "keys");
    static string Account => Environment.GetEnvironmentVariable("OMAWIN_USER_LAYER") is { Length: > 0 } a ? a
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "omawin-account");
    static string Shared => Environment.GetEnvironmentVariable("OMAWIN_SHARED_LAYER") is { Length: > 0 } s ? s
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "omawin", "shared");

    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] == "status") return Status();
        if (args[0] == "signin") return SignIn(args.Length > 1 ? args[1].ToLowerInvariant() : "");
        return Program.Fail("omawin keyboard [signin caps-escape|caps-control|swap-caps-control|caps-off|off]");
    }

    static string Value(string name, string otherwise)
    {
        try { var f = Path.Combine(StateDir, name); return File.Exists(f) ? File.ReadAllText(f).Trim() : otherwise; }
        catch (IOException) { return otherwise; }
    }

    static int Status()
    {
        var caps = Value("caps-lock", "caps");
        Console.WriteLine("Caps Lock: " + caps switch
        {
            "escape" => "Escape",
            "control" => "Control",
            "escape-control" => "Escape when tapped, Control when held",
            _ => "Caps Lock",
        } + "  (omawin setting set input.caps-lock escape | control | escape-control | caps)");
        Console.WriteLine("Copilot key: " + (Value("copilot-key", "windows") == "launcher" ? "opens the launcher" : "Copilot") +
            "  (omawin setting set input.copilot-key launcher | windows)");
        Console.WriteLine("Space in File Explorer: " + (Value("quick-look", "on") == "off" ? "Explorer's" : "Quick Look on the selected file") +
            "  (omawin setting set explorer.quick-look on | off)");

        string[] files = [Path.Combine(Shared, "snippets.txt"), Path.Combine(Account, "snippets.txt")];
        var snippets = Snippets.Parse(files.Where(File.Exists).Select(File.ReadAllLines));
        Console.WriteLine();
        if (snippets.Count == 0) Console.WriteLine($"No snippets: add lines like \";sig | Best,\\nMe\" to {files[1]}.");
        else
        {
            Console.WriteLine($"Snippets ({string.Join(" and ", files.Where(File.Exists))}):");
            foreach (var s in snippets) Console.WriteLine($"  {s.Trigger,-12} {(s.Text.Length > 60 ? s.Text[..57] + "…" : s.Text)}");
            foreach (var (never, because) in Snippets.Shadowed(snippets))
                Console.WriteLine($"  ! {never} never fires: {because} fires first while you type it");
        }

        Console.WriteLine();
        using var key = Registry.LocalMachine.OpenSubKey(ScancodeMap.Key);
        var map = ScancodeMap.Read(key?.GetValue(ScancodeMap.Value) as byte[]);
        Console.WriteLine(map is { Count: > 0 }
            ? $"Windows' own remap (every account, the sign-in screen too): {ScancodeMap.Describe(map)}  (omawin keyboard signin off)"
            : "Windows' own remap: none  (omawin keyboard signin caps-escape | caps-control | swap-caps-control | caps-off)");
        return 0;
    }

    static int SignIn(string name)
    {
        using var key = Registry.LocalMachine.OpenSubKey(ScancodeMap.Key);
        var now = ScancodeMap.Read(key?.GetValue(ScancodeMap.Value) as byte[]);
        bool keptOne = key?.GetValue(AdminScripts.ScancodeBackup) != null;
        byte[]? map = null;
        string says;
        if (name == "off")
        {
            if (now == null && !keptOne) return Program.Say("Windows has no key remap set.");
            bool ours = now != null && ScancodeMap.Presets.Any(p => p.Maps.Length == now.Count && p.Maps.All(now.Contains));
            if (!ours && !keptOne) return Program.Fail($"this remap ({ScancodeMap.Describe(now!)}) wasn't set by omawin; it's left as it is");
            says = keptOne ? "The remap you had before goes back." : "Windows' own remap goes.";
        }
        else
        {
            var preset = ScancodeMap.Presets.FirstOrDefault(p => p.Name == name);
            if (preset.Name == null) return Program.Fail($"omawin keyboard signin {string.Join(" | ", ScancodeMap.Presets.Select(p => p.Name))} | off");
            map = ScancodeMap.Build(preset.Maps);
            says = $"{preset.Says}, for every account, at the sign-in screen and in admin windows too." +
                (now is { Count: > 0 } ? $" (Now: {ScancodeMap.Describe(now)}{(keptOne ? "" : "; kept, and off puts it back")}.)" : "");
        }
        Console.WriteLine(says);
        if (!Program.Ask("That needs admin (Windows asks once) and works from the next restart. Go ahead?")) return 0;
        var failed = Elevated.Run(status => AdminScripts.ScancodeMapWrite(map, status));
        return failed == null ? Program.Say("Done: restart to use it" + (map != null ? " (undo: omawin keyboard signin off)." : ".")) : Program.Fail(failed);
    }
}
