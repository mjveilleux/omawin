// The right-click menu's add-ins (roadmap 8.47): the classic shell extensions programs register,
// which Windows loads into Explorer itself, so a slow or broken one slows or hangs every
// right-click. Listed with their program and maker, and switched off for your account through
// Windows' own "blocked extensions" list (Shell Extensions\Blocked, as ShellExView does), undone
// by switching it back on; Explorer reads the list when it starts.
//
// This file decides (merging what the registry says, which one a typed word means, the words);
// ShellExtensionsWin.cs reads the registry. Pure C#: tested in menu-app.Tests.

namespace SystemMenu;

// One extension, wherever it's registered: Where says for what ("right-click on files", …).
public sealed record ShellExtension(
    string Clsid, string Name, IReadOnlyList<string> Where, string? Dll, string? Company,
    bool BlockedByYou, bool BlockedForPc)
{
    public bool Blocked => BlockedByYou || BlockedForPc;
    public bool Windows => Company?.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase) == true;
}

// What the registry walk found: one handler registration.
public sealed record ShellExtFound(string Clsid, string Name, string Root, string Kind, string? Dll, string? Company);

public static class ShellExtensions
{
    // The kinds Explorer loads into itself, under <class>\shellex\<kind>.
    public static readonly string[] Kinds = ["ContextMenuHandlers", "DragDropHandlers", "PropertySheetHandlers", "CopyHookHandlers"];

    public const string BlockedKey = @"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";

    // "{86ca1aa0-…}" in one spelling (upper case, braces), or null when it isn't a CLSID.
    public static string? Normalize(string text) =>
        Guid.TryParse(text.Trim(), out var g) ? "{" + g.ToString().ToUpperInvariant() + "}" : null;

    public static string WhereLabel(string root, string kind)
    {
        var what = kind switch
        {
            "ContextMenuHandlers" => "right-click", "DragDropHandlers" => "drag and drop",
            "PropertySheetHandlers" => "properties", "CopyHookHandlers" => "copying folders", _ => kind,
        };
        var on = root switch
        {
            "*" => "files", "AllFilesystemObjects" => "files and folders", "Directory" or "Folder" => "folders",
            @"Directory\Background" => "a folder's background", "Drive" => "drives",
            _ when root.StartsWith('.') => $"{root} files",
            _ => root,
        };
        return $"{what} on {on}";
    }

    // One row per extension, its places merged; programs other than Windows' first, then by name.
    public static List<ShellExtension> Merge(IEnumerable<ShellExtFound> found, IReadOnlySet<string> blockedByYou, IReadOnlySet<string> blockedForPc)
    {
        var rows = new List<ShellExtension>();
        foreach (var g in found.Select(f => (f, Id: Normalize(f.Clsid))).Where(x => x.Id != null).GroupBy(x => x.Id!))
        {
            var first = g.First().f;
            var where = g.Select(x => WhereLabel(x.f.Root, x.f.Kind)).Distinct().ToList();
            rows.Add(new ShellExtension(g.Key, first.Name, where, first.Dll, g.Select(x => x.f.Company).FirstOrDefault(c => c != null),
                blockedByYou.Contains(g.Key), blockedForPc.Contains(g.Key)));
        }
        return [.. rows.OrderBy(r => r.Windows).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)];
    }

    // What a typed word means: a CLSID, a name or its program's file name exactly, else the one
    // that contains it (more than one is ambiguous, and said).
    public static (ShellExtension? Ext, List<ShellExtension> Ambiguous) Match(IReadOnlyList<ShellExtension> all, string typed)
    {
        if (Normalize(typed) is { } id) return (all.FirstOrDefault(e => e.Clsid == id), []);
        string File(ShellExtension e) => e.Dll == null ? "" : Path.GetFileNameWithoutExtension(e.Dll.Replace('\\', '/').Split('/')[^1]);
        var exact = all.Where(e => e.Name.Equals(typed, StringComparison.OrdinalIgnoreCase) || File(e).Equals(typed, StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count == 1) return (exact[0], []);
        var near = all.Where(e => e.Name.Contains(typed, StringComparison.OrdinalIgnoreCase) || File(e).Contains(typed, StringComparison.OrdinalIgnoreCase)
            || (e.Company?.Contains(typed, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        return near.Count == 1 ? (near[0], []) : (null, near);
    }

    // `omawin shell-extensions`: every one, programs other than Windows' first.
    public static string Format(IReadOnlyList<ShellExtension> all)
    {
        if (all.Count == 0) return "No shell extensions registered.";
        var lines = new List<string> { $"Shell extensions Explorer loads ({all.Count(e => !e.Windows)} from other programs, {all.Count(e => e.Windows)} Windows' own):" };
        foreach (var e in all)
        {
            var state = e.BlockedForPc ? "off for the PC" : e.BlockedByYou ? "off" : "on";
            lines.Add($"  {state,-14}{e.Name}  ({e.Company ?? "unknown maker"})");
            lines.Add($"                {string.Join(", ", e.Where)}{(e.Dll is { } dll ? $": {dll}" : "")}");
        }
        lines.Add("");
        lines.Add("omawin shell-extensions off <name>: Explorer leaves it out (for you; Explorer restarts once, --now does it). on <name> puts it back.");
        return string.Join('\n', lines);
    }
}
