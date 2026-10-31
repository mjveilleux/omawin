namespace Omawin.Windows;

// Everything that starts with Windows, in one list (roadmap 8.16). Pure: the switch's bytes, the
// places, how an entry is matched by name and how a command line is shown. StartupList.cs reads the
// registry and folders; Commands/Startup.cs prints and switches.

// Where an entry lives. Machine-wide ones need admin to switch.
public enum StartupSource { UserRun, MachineRun, MachineRun32, UserFolder, MachineFolder, Service }

public sealed record StartupItem(StartupSource Source, string Name, string Command, bool Enabled)
{
    public bool ForEveryAccount => Source is StartupSource.MachineRun or StartupSource.MachineRun32 or StartupSource.MachineFolder or StartupSource.Service;
}

public static class StartupItems
{
    // The value Task Manager keeps beside each entry under Explorer\StartupApproved\<Run|Run32|
    // StartupFolder>, named like the entry: 12 bytes, byte 0 the state, bytes 4-11 a FILETIME of when
    // it was turned off. Seen: 02 / 06 on, 03 / 07 off, so the low bit means off; no value means on.
    public static bool IsEnabled(byte[]? value) => value is not { Length: > 0 } || (value[0] & 1) == 0;

    public static byte[] Disabled(DateTime utcNow)
    {
        var b = new byte[12];
        b[0] = 3;
        BitConverter.TryWriteBytes(b.AsSpan(4), utcNow.ToFileTimeUtc());
        return b;
    }

    public static byte[] Enabled() { var b = new byte[12]; b[0] = 2; return b; }

    // The StartupApproved key's last part for a source (the same under HKCU and HKLM).
    public static string? ApprovedKey(StartupSource s) => s switch
    {
        StartupSource.UserRun or StartupSource.MachineRun => "Run",
        StartupSource.MachineRun32 => "Run32",
        StartupSource.UserFolder or StartupSource.MachineFolder => "StartupFolder",
        _ => null,
    };

    public static string Where(StartupSource s) => s switch
    {
        StartupSource.UserRun => "you (Run key)",
        StartupSource.MachineRun => "every account (Run key)",
        StartupSource.MachineRun32 => "every account (Run key, 32-bit)",
        StartupSource.UserFolder => "you (Startup folder)",
        StartupSource.MachineFolder => "every account (Startup folder)",
        StartupSource.Service => "a service, set to start automatically",
        _ => "",
    };

    // The entry a typed name means: exact first (ignoring case), then the one that starts with it,
    // then the one containing it, else nothing; more than one at the best level is ambiguous.
    public static (StartupItem? Item, List<StartupItem> Ambiguous) Match(IReadOnlyList<StartupItem> items, string typed)
    {
        typed = typed.Trim();
        foreach (var test in new Func<string, bool>[]
        {
            n => n.Equals(typed, StringComparison.OrdinalIgnoreCase),
            n => n.StartsWith(typed, StringComparison.OrdinalIgnoreCase),
            n => n.Contains(typed, StringComparison.OrdinalIgnoreCase),
        })
        {
            var hits = items.Where(i => test(i.Name) || test(StripLnk(i.Name))).ToList();
            if (hits.Count == 1) return (hits[0], []);
            if (hits.Count > 1) return (null, hits);
        }
        return (null, []);
    }

    static string StripLnk(string name) => name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;

    // The program a command line starts, for a short listing: "C:\…\Discord\Update.exe" from
    // "\"C:\…\Update.exe\" --processStart Discord.exe".
    public static string Program(string command)
    {
        var c = command.Trim();
        if (c.StartsWith('"'))
        {
            int end = c.IndexOf('"', 1);
            return end > 0 ? c[1..end] : c.Trim('"');
        }
        // Unquoted: up to the first ".exe" (a path may have spaces), else the first word.
        int exe = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exe > 0) return c[..(exe + 4)];
        int space = c.IndexOf(' ');
        return space > 0 ? c[..space] : c;
    }
}
