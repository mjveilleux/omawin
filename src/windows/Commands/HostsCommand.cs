using System.Security.Cryptography;
using System.Text;

namespace Omawin.Windows;

// omawin hosts (8.19): the hosts file as entries.
//   omawin hosts                        what's in it, on and off
//   omawin hosts add <address> <name…>  point names at an address (127.0.0.1 myapp.test)
//   omawin hosts off <name>             comment it out (kept, marked, so on brings it back)
//   omawin hosts on <name>              back on
// Reading needs nothing; a change needs admin: the new file is shown as a diff, then one Windows
// prompt writes it, keeping the old one beside it as hosts.backup-<time>.
static class HostsCommand
{
    static string HostsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");

    public static int Run(string[] args)
    {
        var before = File.Exists(HostsPath) ? File.ReadAllText(HostsPath) : "";
        var file = new HostsFile(before);
        if (args.Length == 0 || args[0] == "list") return List(file);

        string? why = args[0] switch
        {
            "add" when args.Length >= 3 => file.Add(args[1], args[2..]),
            "off" when args.Length == 2 => file.Off(args[1]),
            "on" when args.Length == 2 => file.On(args[1]),
            _ => "omawin hosts [add <address> <name…> | off <name> | on <name>]",
        };
        if (why != null) return Program.Fail(why);
        if (file.Text == new HostsFile(before).Text) return Program.Say("Nothing to change: it's already that way.");

        var bad = file.BadLines().ToList();
        if (bad.Count > 0)
        {
            // The elevated side refuses a file with a line it can't read; say which before asking.
            Console.Error.WriteLine("omawin: the hosts file has lines that aren't hosts entries, so it isn't changed here:");
            foreach (var (n, t) in bad.Take(5)) Console.Error.WriteLine($"  line {n}: {t}");
            Console.Error.WriteLine("Fix them in an admin editor first.");
            return 1;
        }

        Console.WriteLine("The hosts file will change:");
        foreach (var line in HostsFile.Diff(new HostsFile(before).Lines, file.Lines)) Console.WriteLine("  " + line);
        if (!Program.Ask("Writing it needs admin: Windows asks once (the old file is kept beside it). Go ahead?")) return 0;

        var pending = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "launcher", "pending.hosts");
        Directory.CreateDirectory(Path.GetDirectoryName(pending)!);
        var bytes = new UTF8Encoding(false).GetBytes(file.Text);
        File.WriteAllBytes(pending, bytes);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var failed = Elevated.Run(status => AdminScripts.HostsWrite(pending, hash, stamp, status));
        try { File.Delete(pending); } catch (IOException) { }
        return failed == null ? Program.Say($"Done (the old one is hosts.backup-{stamp}).") : Program.Fail(failed);
    }

    static int List(HostsFile file)
    {
        var entries = file.Entries();
        if (entries.Count == 0) return Program.Say($"The hosts file has no entries ({HostsPath}). omawin hosts add 127.0.0.1 myapp.test adds one.");
        foreach (var e in entries)
            Console.WriteLine($"  {(e.On ? "on " : "off")}  {e.Address,-16} {string.Join(' ', e.Names)}");
        return 0;
    }
}
