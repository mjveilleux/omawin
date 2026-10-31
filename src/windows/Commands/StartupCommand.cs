namespace Omawin.Windows;

// omawin startup (8.16): everything that starts with Windows, in one list, from the places Windows
// scatters it (your Run key and Startup folder, the PC's, programs' services).
//   omawin startup              the list, on and off
//   omawin startup off <name>   don't start it (Task Manager's own switch; nothing is deleted)
//   omawin startup on <name>    start it again (the undo)
// Your own entries switch at once; the PC's (every account's) and services need admin: one Windows
// prompt, after saying what will change.
static class StartupCommand
{
    public static int Run(string[] args)
    {
        var items = StartupList.Read();
        if (args.Length == 0 || args[0] is "list") return List(items);
        if (args[0] is not ("off" or "on")) return Program.Fail("omawin startup [off|on <name>]");
        if (args.Length < 2) return Program.Fail($"which one? omawin startup {args[0]} <name> (omawin startup lists them)");
        bool on = args[0] == "on";
        var (item, ambiguous) = StartupItems.Match(items, string.Join(' ', args[1..]));
        if (item == null)
        {
            if (ambiguous.Count == 0) return Program.Fail($"nothing called \"{string.Join(' ', args[1..])}\" starts with Windows (omawin startup lists them)");
            Console.Error.WriteLine("omawin: more than one matches; say which:");
            foreach (var a in ambiguous) Console.Error.WriteLine($"  {a.Name}  ({StartupItems.Where(a.Source)})");
            return 1;
        }
        if (item.Enabled == on) return Program.Say($"{item.Name} is already {(on ? "on" : "off")}.");

        if (!item.ForEveryAccount)
        {
            StartupList.SetMine(item, on);
            return Program.Say(on ? $"{item.Name} starts with Windows again." : $"{item.Name} won't start with Windows (undo: omawin startup on {item.Name}).");
        }

        // Every account's: one prompt, said first.
        var what = item.Source == StartupSource.Service
            ? $"The service {item.Name} will {(on ? "start automatically again" : "start only when something asks for it (Manual), not at every start")}."
            : $"{item.Name} will {(on ? "start again" : "no longer start")} for every account on this PC.";
        Console.WriteLine(what);
        if (!Program.Ask("That needs admin: Windows asks once. Go ahead?")) return 0;
        string? failed = item.Source == StartupSource.Service
            ? Elevated.Run(status => AdminScripts.Service(item.Name, on, status))
            : Elevated.Run(status => AdminScripts.StartupMachine(StartupItems.ApprovedKey(item.Source)!, item.Name, on, status));
        if (failed != null) return Program.Fail(failed);
        if (item.Source == StartupSource.Service) StartupList.RememberSwitched(item.Name, off: !on);
        return Program.Say(on ? "Done." : $"Done (undo: omawin startup on {item.Name}).");
    }

    static int List(List<StartupItem> items)
    {
        if (items.Count == 0) return Program.Say("Nothing starts with Windows but Windows.");
        foreach (var group in items.GroupBy(i => i.Source))
        {
            Console.WriteLine(Capital(StartupItems.Where(group.Key)) + (group.First().ForEveryAccount ? " (admin to switch)" : "") + ":");
            foreach (var i in group.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
                Console.WriteLine($"  {(i.Enabled ? "on " : "off")}  {i.Name,-32} {StartupItems.Program(i.Command)}");
            Console.WriteLine();
        }
        Console.WriteLine("omawin startup off <name> stops one; on <name> brings it back.");
        return 0;
    }

    static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
