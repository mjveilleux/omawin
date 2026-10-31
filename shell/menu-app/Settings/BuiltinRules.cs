// The rules behind Phase 8's builtins (Builtins.cs; roadmap 8G-8J, plans/07-fixing-windows.md's
// catalog lines): which Win+key letters are omawin's, what a startup entry's "approved" bytes
// say, which keyboard-layout hotkeys are Alt+Shift, a service's start mode, and which dev runtimes
// a firewall rule is for. No registry and no Windows calls here: Builtins.cs reads and writes,
// these decide, and menu-app.Tests runs them anywhere.

using System.Text.RegularExpressions;

namespace SystemMenu;

// input.win-hotkeys: Explorer's DisabledHotkeys (HKCU\…\Explorer\Advanced, REG_SZ), one character
// per Win+<key> Explorer should leave alone. omawin adds only the keys it binds as plain Win+<key>
// in GlazeWM's config, and on the way back removes only those it added.
public static partial class WinHotkeys
{
    // 'lwin+h', "rwin+1": a Win key and one letter or digit, nothing else held. Win+Shift+S and the
    // like aren't Explorer's plain Win+<key>, and DisabledHotkeys can't single them out.
    [GeneratedRegex(@"^[lr]?win\+([a-z0-9])$", RegexOptions.IgnoreCase)]
    private static partial Regex PlainWin();

    [GeneratedRegex(@"'([^']*)'|""([^""]*)""")]
    private static partial Regex Quoted();

    // Windows keeps Win+L (lock) whatever this value says, so it's never asked for.
    const string Never = "L";

    // The letters, upper case, in a stable order, from the config's `bindings: [...]` lines.
    public static string Letters(IEnumerable<string> configLines)
    {
        var found = new SortedSet<char>();
        foreach (var line in configLines)
        {
            var text = line.TrimStart();
            if (text.StartsWith('#') || !text.StartsWith("bindings:", StringComparison.Ordinal)) continue;
            foreach (Match q in Quoted().Matches(text))
            {
                var key = q.Groups[1].Success ? q.Groups[1].Value : q.Groups[2].Value;
                if (PlainWin().Match(key.Trim()) is { Success: true } m)
                {
                    char c = char.ToUpperInvariant(m.Groups[1].Value[0]);
                    if (!Never.Contains(c)) found.Add(c);
                }
            }
        }
        // Digits after letters, as Explorer's own examples write it.
        return new string([.. found.Where(char.IsLetter), .. found.Where(char.IsDigit)]);
    }

    // Every one of ours is already disabled: Windows' own shortcuts on those keys are off.
    public static bool AllDisabled(string? current, string ours) =>
        ours.Length > 0 && ours.All(c => (current ?? "").Contains(c, StringComparison.OrdinalIgnoreCase));

    // Ours added to what's there, keeping what was there first and in its order.
    public static string Disable(string? current, string ours)
    {
        var now = (current ?? "").ToUpperInvariant();
        return now + new string([.. ours.Where(c => !now.Contains(c)).Distinct()]);
    }

    // Back as it was: ours taken out, except any that were already there before omawin added
    // them (before: the value then; null when nothing was remembered, so all of ours go).
    // Null when nothing is left, so the value is deleted rather than left empty.
    public static string? Restore(string? current, string ours, string? before)
    {
        var keep = (before ?? "").ToUpperInvariant();
        var left = new string([.. (current ?? "").ToUpperInvariant().Where(c => !ours.Contains(c) || keep.Contains(c))]);
        return left.Length == 0 ? null : left;
    }
}

// system.startup-*: how Task Manager and Settings › Apps › Startup turn a sign-in program off
// without removing it (the same bytes bin/no-ads.ps1 writes).
public static class StartupApproved
{
    // HKCU\…\Explorer\StartupApproved\Run, a value named like the Run entry: 12 bytes, byte 0 the
    // state (02 / 06 on, 03 / 07 off: the low bit is "off"), bytes 4-11 when it was turned off.
    // No value: on.
    public static bool IsEnabled(byte[]? value) => value is not { Length: > 0 } || (value[0] & 1) == 0;

    public static byte[] Disabled(DateTime utcNow)
    {
        var b = new byte[12];
        b[0] = 3;
        BitConverter.TryWriteBytes(b.AsSpan(4), utcNow.ToFileTimeUtc());
        return b;
    }

    public static byte[] Enabled() { var b = new byte[12]; b[0] = 2; return b; }

    // The Run value an app's name means: exactly that name, else the one that starts with it
    // ("OneDrive" for "OneDrive", "com.squirrel.Teams.Teams" isn't "MSTeams"); more than one
    // starting with it is ambiguous, so none.
    public static string? RunName(IEnumerable<string> names, string app)
    {
        var all = names.ToList();
        if (all.FirstOrDefault(n => n.Equals(app, StringComparison.OrdinalIgnoreCase)) is { } exact) return exact;
        var starts = all.Where(n => n.StartsWith(app, StringComparison.OrdinalIgnoreCase)).ToList();
        return starts.Count == 1 ? starts[0] : null;
    }

    // A Store app's startup task: HKCU\Software\Classes\Local Settings\…\AppModel\SystemAppData\
    // <package family>\<task>\State, the StartupTaskState numbers (0 off, 1 off by you, 2 on,
    // 3 off by policy, 4 on by policy). The package family is "<name>_<publisher hash>".
    public static bool IsPackageOf(string family, string app) =>
        family.StartsWith(app + "_", StringComparison.OrdinalIgnoreCase);

    public static bool? TaskEnabled(int state) => state switch { 2 or 4 => true, 0 or 1 or 3 => false, _ => null };

    // A policy's state can't be changed from here (Settings greys it out too).
    public static bool ByPolicy(int state) => state is 3 or 4;

    public const int TaskOn = 2, TaskOffByUser = 1;
}

// input.layout-hotkey: HKCU\Keyboard Layout\Toggle, three REG_SZ values: "Language Hotkey" (switch
// input language), "Layout Hotkey" (switch layout within one), and "Hotkey" (the older name for
// the first). "1" Left Alt+Shift, "2" Ctrl+Shift, "3" none, "4" the grave accent. Missing values
// are Windows' defaults: Alt+Shift for languages, Ctrl+Shift for layouts.
public static class LayoutHotkeys
{
    public static readonly string[] Names = ["Hotkey", "Language Hotkey", "Layout Hotkey"];
    const string AltShift = "1", None = "3";

    static string Effective(IReadOnlyDictionary<string, string?> values, string name) =>
        values.GetValueOrDefault(name) ?? (name == "Layout Hotkey" ? "2" : AltShift);

    // On: Alt+Shift does something (switches the language or the layout).
    public static string Read(IReadOnlyDictionary<string, string?> values) =>
        Names.Any(n => Effective(values, n) == AltShift) ? "on" : "off";

    // Off: only what's on Alt+Shift becomes "none" (Ctrl+Shift for layouts stays). The values to
    // write, by name.
    public static Dictionary<string, string> Off(IReadOnlyDictionary<string, string?> values) =>
        Names.Where(n => Effective(values, n) == AltShift).ToDictionary(n => n, _ => None);

    // On: what was there before omawin turned it off (null: a value that wasn't set, deleted
    // again), or Windows' defaults when nothing was remembered.
    public static Dictionary<string, string?> On(IReadOnlyDictionary<string, string?>? remembered) =>
        remembered is { Count: > 0 } r ? new(r) : new() { ["Hotkey"] = AltShift, ["Language Hotkey"] = AltShift };

    // The remembered values as a small file ("<name>=<value>", "-" for not set), and back.
    public static IEnumerable<string> Save(IReadOnlyDictionary<string, string?> values) =>
        Names.Select(n => $"{n}={values.GetValueOrDefault(n) ?? "-"}");

    public static Dictionary<string, string?> Load(IEnumerable<string> lines)
    {
        var d = new Dictionary<string, string?>();
        foreach (var line in lines)
            if (line.Split('=', 2) is [var n, var v] && Names.Contains(n)) d[n] = v == "-" ? null : v;
        return d;
    }
}

// builtin service <name> on=<mode> off=<mode>: a Windows service's start mode, read from
// HKLM\SYSTEM\CurrentControlSet\Services\<name> (Start 2 automatic, 3 manual, 4 disabled;
// DelayedAutostart 1 with 2 is "delayed"). Changing it needs admin (bin/settings-admin.ps1).
public static class ServiceStart
{
    public static readonly string[] Modes = ["auto", "delayed", "manual", "disabled"];

    public static string? Mode(int? start, int? delayed) => start switch
    {
        2 => delayed == 1 ? "delayed" : "auto",
        3 => "manual",
        4 => "disabled",
        _ => null, // 0 / 1 are drivers' boot and system starts; missing: no such service
    };

    // The switch: off is exactly the line's off mode; anything else counts as on.
    public static string? Switch(SettingEntry e, string? mode)
    {
        if (mode == null) return null;
        var off = e.How.Off ?? "disabled";
        return mode == off ? "off" : "on";
    }

    // The mode the elevated run sets for on / off.
    public static string? ModeFor(SettingEntry e, string value)
    {
        var mode = value == "on" ? e.How.On ?? "auto" : value == "off" ? e.How.Off ?? "disabled" : null;
        return mode != null && Modes.Contains(mode) ? mode : null;
    }
}

// developer.firewall-dev: which dev runtimes may accept connections on private networks. Found on
// this account's PATH (and mise's installs), resolved here, and handed to the elevated run as
// "<name>=<path>|<name>=<path>", where each is checked again before a rule is made.
public static class DevRuntimes
{
    public static readonly string[] Names = ["node", "bun", "python", "dotnet", "go"];

    // A real program, not a launcher that hands over to it: the Store's python alias
    // (WindowsApps) and mise's shims can't carry a firewall rule for the program they start.
    public static bool Usable(string path) =>
        path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        && !path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)
        && !path.Contains(@"\shims\", StringComparison.OrdinalIgnoreCase)
        && !path.Contains('|') && !path.Contains('\t');

    // Each runtime's programs, from candidate paths (PATH order, then mise's installs), each path
    // once; a runtime with none found is left out.
    public static List<(string Name, string Path)> Pick(IEnumerable<string> candidates)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var picked = new List<(string, string)>();
        foreach (var path in candidates)
        {
            if (!Usable(path) || !seen.Add(path)) continue;
            var name = System.IO.Path.GetFileNameWithoutExtension(path.Replace('\\', '/').Split('/')[^1]).ToLowerInvariant();
            if (Names.Contains(name)) picked.Add((name, path));
        }
        return picked.OrderBy(p => Array.IndexOf(Names, p.Item1)).ToList();
    }

    public static string Encode(IEnumerable<(string Name, string Path)> runtimes) =>
        string.Join('|', runtimes.Select(r => $"{r.Name}={r.Path}"));

    public static List<(string Name, string Path)> Decode(string value) =>
        value.Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2)).Where(p => p.Length == 2).Select(p => (p[0], p[1])).ToList();

    // "node, bun (2 versions)" for the list shown before the prompt.
    public static string Describe(string value)
    {
        var groups = Decode(value).GroupBy(r => r.Name).Select(g => g.Count() > 1 ? $"{g.Key} ({g.Count()} versions)" : g.Key).ToList();
        return groups.Count == 0 ? "none found" : string.Join(", ", groups);
    }
}
