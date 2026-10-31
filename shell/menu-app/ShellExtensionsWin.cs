// `omawin shell-extensions [off|on <name>] [--now]` (roadmap 8.47): the registry side of
// ShellExtensions.cs. Reads every class's shellex handlers (HKEY_CLASSES_ROOT, yours and the
// PC's merged), each handler's program and its maker, and Windows' blocked list; switches one
// off or on in your account's blocked list. Answered by the menu app, like `omawin setting`.

using System.Diagnostics;
using Microsoft.Win32;

namespace SystemMenu;

static class ShellExtensionsWin
{
    public const string Usage = "omawin shell-extensions [off|on <name>] [--now]";

    public static SettingCommands.Answer Answer(string[] a)
    {
        var all = Read();
        if (a is [] or ["list"]) return new(0, ShellExtensions.Format(all));
        if (a[0] is not ("off" or "on") || a.Length < 2) return new(2, Usage);
        bool off = a[0] == "off", now = a.Contains("--now");
        var typed = string.Join(' ', a.Skip(1).Where(x => x != "--now"));
        var (ext, ambiguous) = ShellExtensions.Match(all, typed);
        if (ext == null)
            return new(1, ambiguous.Count > 0
                ? $"'{typed}' could be: {string.Join("; ", ambiguous.Select(e => $"{e.Name} ({e.Clsid})"))}"
                : $"no shell extension '{typed}' (omawin shell-extensions lists them)");
        if (!off && ext.BlockedForPc && !ext.BlockedByYou)
            return new(1, $"{ext.Name} is turned off for the whole PC (HKLM's blocked list); turning it back on needs admin");
        if (ext.BlockedByYou == off)
        {
            // Asked again with --now: the restart it was waiting for.
            if (now) ExplorerRestart.Now();
            return new(0, $"{ext.Name}: already {(off ? "off" : "on")}{(now ? " (Explorer restarted)" : "")}");
        }

        using (var k = Registry.CurrentUser.CreateSubKey(ShellExtensions.BlockedKey))
        {
            if (off) k.SetValue(ext.Clsid, ext.Name, RegistryValueKind.String);
            else k.DeleteValue(ext.Clsid, throwOnMissingValue: false);
        }
        var said = $"{ext.Name}: {(off ? "off; Explorer leaves it out of its menus" : "on again")}";
        if (now) { ExplorerRestart.Now(); return new(0, said + " (Explorer restarted)"); }
        ExplorerRestart.Request();
        return new(0, said + $" once Explorer restarts (the system menu's Restart Explorer, or run this again with --now). Undo: omawin shell-extensions {(off ? "on" : "off")} {ext.Clsid}");
    }

    // Every handler of every class. Walking all of HKEY_CLASSES_ROOT is some thousands of keys:
    // a fraction of a second, done only when asked.
    static List<ShellExtension> Read()
    {
        var found = new List<ShellExtFound>();
        var hkcr = Registry.ClassesRoot;
        IEnumerable<string> roots = hkcr.GetSubKeyNames().Append(@"Directory\Background");
        using (var sfa = hkcr.OpenSubKey("SystemFileAssociations"))
            if (sfa != null) roots = roots.Concat(sfa.GetSubKeyNames().Select(n => $@"SystemFileAssociations\{n}"));
        foreach (var root in roots)
        {
            using var shellex = OpenOrNull(hkcr, $@"{root}\shellex");
            if (shellex == null) continue;
            foreach (var kind in ShellExtensions.Kinds)
            {
                using var k = OpenOrNull(shellex, kind);
                if (k == null) continue;
                foreach (var name in k.GetSubKeyNames())
                {
                    using var h = OpenOrNull(k, name);
                    // The CLSID is the handler's default value, or its key's own name.
                    var clsid = h?.GetValue("") is string v && Guid.TryParse(v, out _) ? v : Guid.TryParse(name, out _) ? name : null;
                    if (clsid == null) continue;
                    using var cls = OpenOrNull(hkcr, $@"CLSID\{clsid}");
                    using var server = cls == null ? null : OpenOrNull(cls, "InprocServer32");
                    var dll = Program(server?.GetValue("") as string);
                    var display = cls?.GetValue("") as string is { Length: > 0 } d ? d : name;
                    found.Add(new ShellExtFound(clsid, display, root.StartsWith(@"SystemFileAssociations\") ? root[23..] : root, kind, dll, Company(dll)));
                }
            }
        }
        return ShellExtensions.Merge(found, Blocked(Registry.CurrentUser), Blocked(Registry.LocalMachine));
    }

    static RegistryKey? OpenOrNull(RegistryKey parent, string path)
    {
        try { return parent.OpenSubKey(path); }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return null; }
    }

    // A server's path as Windows finds it: variables expanded, a bare name in System32.
    static string? Program(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var path = Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"'));
        return Path.IsPathRooted(path) ? path : Path.Combine(Environment.SystemDirectory, path);
    }

    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string?> companies = new(StringComparer.OrdinalIgnoreCase);

    static string? Company(string? dll)
    {
        if (dll == null) return null;
        if (companies.TryGetValue(dll, out var known)) return known;
        string? company = null;
        try { if (File.Exists(dll)) company = FileVersionInfo.GetVersionInfo(dll).CompanyName is { Length: > 0 } c ? c.Trim() : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return companies[dll] = company;
    }

    static HashSet<string> Blocked(RegistryKey root)
    {
        using var k = root.OpenSubKey(ShellExtensions.BlockedKey);
        return k == null ? [] : k.GetValueNames().Select(ShellExtensions.Normalize).OfType<string>().ToHashSet();
    }
}
