namespace Omawin.Windows;

// Who holds a file (roadmap 8.1): what Restart Manager reports, worded. Pure: no Windows calls, so
// test/windows.Tests checks it anywhere. RestartManager.cs asks Windows; Commands/WhoHas.cs prints.

// One process holding a file. Started (a FILETIME) pins it: a pid alone may be reused.
public sealed record Holder(uint Pid, long Started, string Name, string? Service, HolderKind Kind, string? Exe);

// RM_APP_TYPE, as restartmanager.h numbers it.
public enum HolderKind { Unknown = 0, MainWindow = 1, OtherWindow = 2, Service = 3, Explorer = 4, Console = 5, Critical = 1000 }

public static class Holders
{
    // Windows reports one row per service, so one svchost hosting three services holding the file
    // comes back three times: one row per process, its services joined.
    public static List<Holder> Merge(IEnumerable<Holder> rows)
    {
        var byProcess = new Dictionary<(uint, long), Holder>();
        var order = new List<(uint, long)>();
        foreach (var r in rows)
        {
            var key = (r.Pid, r.Started);
            if (!byProcess.TryGetValue(key, out var seen)) { byProcess[key] = r; order.Add(key); continue; }
            var services = new[] { seen.Service, r.Service }.Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.OrdinalIgnoreCase);
            var joined = string.Join(", ", services);
            byProcess[key] = seen with { Service = joined.Length > 0 ? joined : null };
        }
        return order.Select(k => byProcess[k]).ToList();
    }

    // A line for a terminal or a launcher row: "Code (pid 4120, a window) C:\…\Code.exe".
    public static string Describe(Holder h)
    {
        var what = h.Kind switch
        {
            HolderKind.MainWindow or HolderKind.OtherWindow => "a window",
            HolderKind.Service => h.Service != null ? $"the service {h.Service}" : "a service",
            HolderKind.Explorer => "File Explorer",
            HolderKind.Console => "a terminal program",
            HolderKind.Critical => "part of Windows, can't be ended",
            _ => h.Service != null ? $"the service {h.Service}" : null,
        };
        var name = h.Name.Length > 0 ? h.Name : h.Exe != null ? WindowsPath.FileName(h.Exe) : "process";
        var label = what == null ? $"{name} (pid {h.Pid})" : $"{name} (pid {h.Pid}, {what})";
        return h.Exe != null ? $"{label}  {h.Exe}" : label;
    }

    // Close is offered for a process with a window of its own (it may ask to save first). Not for
    // Explorer: its windows include the taskbar and the desktop, and closing the desktop asks to
    // shut the PC down; Explorer usually holds a file for its preview pane, which closing that
    // folder's window releases. End for anything but Windows' own critical processes (csrss,
    // smss…), whose end is a blue screen.
    public static bool CanClose(Holder h) => h.Kind is HolderKind.MainWindow or HolderKind.OtherWindow;
    public static bool CanEnd(Holder h) => h.Kind != HolderKind.Critical;

    // When nobody is reported. Restart Manager sees open files and loaded modules, not a process's
    // current folder, so a terminal sitting in a folder holds it invisibly: say so.
    public static string NoneFound(bool folder) => folder
        ? "Nothing holds a file in it. If the folder still won't go, a terminal or program may be sitting in it (its current folder): cd out of it, or close it."
        : "Nothing holds it open now.";

    // The path in a clipboard's text, as Explorer's "Copy as path" gives it ("C:\a b\c.txt") or
    // typed: the first line, outer quotes and spaces trimmed. Null when it can't be a path.
    public static string? PathFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var line = text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (line == null) return null;
        line = line.Trim().Trim('"').Trim();
        if (line.Length is 0 or > 32767) return null;
        if (line.IndexOfAny(['<', '>', '|', '"', '*', '?', '\0']) >= 0) return null;
        // A drive path (C:\…), a share (\\server\…) or the long-path form (\\?\C:\…).
        bool drive = line.Length >= 3 && char.IsAsciiLetter(line[0]) && line[1] == ':' && line[2] is '\\' or '/';
        bool share = line.StartsWith(@"\\", StringComparison.Ordinal);
        return drive || share ? line : null;
    }
}

// Windows paths taken apart the same way on any OS (System.IO.Path splits on '/' only on Linux, where
// these tests run).
public static class WindowsPath
{
    public static string FileName(string path)
    {
        int cut = path.LastIndexOfAny(['\\', '/']);
        return cut < 0 ? path : path[(cut + 1)..];
    }

    public static string FileNameWithoutExtension(string path)
    {
        var name = FileName(path);
        int dot = name.LastIndexOf('.');
        return dot <= 0 ? name : name[..dot];
    }
}
