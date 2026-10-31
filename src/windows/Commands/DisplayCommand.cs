namespace Omawin.Windows;

// omawin display (8.7): monitor profiles that follow the dock.
//   omawin display          the monitors now, and whether a layout is saved for this set
//   omawin display save     remember this layout for this set of monitors
//   omawin display apply    put this set's saved layout back (the menu app runs it, with --auto,
//                           when monitors come or go)
//   omawin display forget   forget this set's layout
// Kept in %LOCALAPPDATA%\launcher\displays.txt, a text file you can edit.
static class DisplayCommand
{
    static readonly string File_ = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "launcher", "displays.txt");

    public static int Run(string[] args)
    {
        var verb = args.Length > 0 ? args[0].ToLowerInvariant() : "status";
        bool auto = args.Contains("--auto");
        var now = DisplayConfig.Current();
        var lines = File.Exists(File_) ? File.ReadAllLines(File_) : null;
        var saved = DisplayProfiles.Parse(lines ?? []);
        var mine = DisplayProfiles.For(saved, now.Select(d => d.Id));
        switch (verb)
        {
            case "status":
                foreach (var d in now) Console.WriteLine("  " + DisplayProfiles.Describe(d));
                Console.WriteLine();
                Console.WriteLine(mine == null ? "No layout saved for these monitors (omawin display save remembers this one)."
                    : DisplayProfiles.Same(mine, now) ? "This is the layout saved for these monitors."
                    : "A different layout is saved for these monitors: omawin display apply puts it back.");
                if (saved.Count > 0) Console.WriteLine($"{saved.Count} set{(saved.Count == 1 ? "" : "s")} of monitors saved, in {File_}.");
                return 0;
            case "save":
                if (DisplayProfiles.Problem(now) is { } problem) return Program.Fail(problem);
                Directory.CreateDirectory(Path.GetDirectoryName(File_)!);
                File.WriteAllLines(File_, DisplayProfiles.Save(lines, now));
                return Program.Say($"Saved: whenever these {now.Count} monitor{(now.Count == 1 ? "" : "s")} are plugged in, this layout comes back.");
            case "forget":
            {
                if (mine == null) return Program.Say("No layout is saved for these monitors.");
                File.WriteAllLines(File_, DisplayProfiles.Remove(lines!, DisplayProfiles.Key(now.Select(d => d.Id))));
                return Program.Say("Forgotten: these monitors are left as Windows sets them.");
            }
            case "apply":
            {
                if (mine == null) return auto ? 0 : Program.Fail("no layout is saved for these monitors (omawin display save)");
                if (DisplayProfiles.Same(mine, now)) return auto ? 0 : Program.Say("Already as saved.");
                if (DisplayProfiles.Problem(mine) is { } bad) return Program.Fail($"{File_}: {bad}");
                var failed = DisplayConfig.Apply(mine);
                return failed == null ? Program.Say("Monitors put back as saved.") : Program.Fail(failed);
            }
            default:
                return Program.Fail("omawin display [save | apply | forget]");
        }
    }
}
