using System.Text.Json;

namespace SystemMenu;

// One row of launcher/menu.txt: "<id> | <icon> | <label> | <action>" (see that file's header).
public sealed record MenuEntry(string Id, string Icon, string Label, string? Action, bool Confirm, string? Package)
{
    public bool IsSubmenu => Action == null;
    public string Parent => Id.Contains('.') ? Id[..Id.LastIndexOf('.')] : "";
    public string Kind => Action == null ? "" : Action.Split(' ', 2)[0];
    public string Argument => Action == null ? "" : (Action.Split(' ', 2) is { Length: 2 } p ? p[1].Trim() : "");
}

public sealed class Theme
{
    public string Name { get; set; } = "Tokyo Night";
    public string Mode { get; set; } = "dark";
    public string Background { get; set; } = "#1a1b26";
    public string Surface { get; set; } = "#1f2335";
    public string Selection { get; set; } = "#292e42";
    public string Border { get; set; } = "#3b4261";
    public string Foreground { get; set; } = "#c0caf5";
    public string Muted { get; set; } = "#a9b1d6";
    public string Comment { get; set; } = "#565f89";
    public string Accent { get; set; } = "#7aa2f7";
    public string Purple { get; set; } = "#bb9af7";
    public string Cyan { get; set; } = "#7dcfff";
    public string Red { get; set; } = "#f7768e";
    public string Green { get; set; } = "#9ece6a";
    public string Yellow { get; set; } = "#e0af68";
    public string[] Gradient { get; set; } = ["#bb9af7", "#7aa2f7", "#7dcfff", "#73daca", "#9ece6a"];
    public string[] Backgrounds { get; set; } = []; // URLs, or paths in the repo
}

public static class MenuModel
{
    public static readonly string Repo =
        Environment.GetEnvironmentVariable("DOTFILES") is { Length: > 0 } d ? d
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "omawin");
    // Your own folders (docs/work.md), both optional. Work: this account's (its employer's or
    // client's launcher items and sites, shares, a VPN, reports), ~/omawin-account. Shared: yours
    // in every account on the PC, C:\ProgramData\omawin\shared. Their launcher items, sites and
    // shortcuts are added to omawin's (Layered), and the Reports page runs the account's reports
    // script. OMAWIN_USER_LAYER / OMAWIN_SHARED_LAYER override where they are, as for omawin.exe
    // and bin/user-layer.ps1; until the move (migrations/2026-10-17-shared-folder.ps1) the old
    // ~/omawin-work and ~/omawin-me are used.
    public static readonly string Work = Folder("OMAWIN_USER_LAYER",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "omawin-account"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "omawin-work"));
    public static readonly string Shared = Folder("OMAWIN_SHARED_LAYER",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "omawin", "shared"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "omawin-me"));
    static string Folder(string variable, string now, string before) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } set ? set
        : !Directory.Exists(now) && Directory.Exists(before) ? before : now;
    // A launcher file (launcher/<name>): omawin's, then the shared folder's, then this account's.
    public static string[] Layered(string name) =>
        [Path.Combine(Repo, "launcher", name), Path.Combine(Shared, "launcher", name), Path.Combine(Work, "launcher", name)];
    // Their lines, one after the other; a file that isn't there adds nothing.
    public static IEnumerable<string> LayeredLines(string name) =>
        Layered(name).Where(File.Exists).SelectMany(File.ReadAllLines);
    public static readonly string StateDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "launcher");
    public static string MenuFile => Path.Combine(Repo, "launcher", "menu.txt");

    // One of omawin's commands (bin/omawin.cs), hidden: opening terminals and editors goes
    // through it, so the default terminal / editor (omawin default) is honored everywhere.
    public static void Omawin(params string[] args)
    {
        var info = new System.Diagnostics.ProcessStartInfo("omawin.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        try { System.Diagnostics.Process.Start(info); } catch (Exception ex) { App.Log(ex); }
    }
    public static string InstalledFile => Path.Combine(StateDir, "installed.txt");
    public static string ThemesDir => Path.Combine(Repo, "themes");
    // The chosen theme's file name (without .json); written by themes/set-theme.ps1.
    public static string CurrentThemeFile => Path.Combine(StateDir, "theme");

    // installed.txt and menu.txt are read again only when they've changed (every showing asks),
    // like History.Load. What's returned is shared: read it, don't change it.
    static HashSet<string>? installedCache;
    static DateTime installedStamp;
    static Dictionary<string, MenuEntry>? entriesCache;
    static DateTime entriesStamp;
    static HashSet<string>? entriesInstalled;

    public static HashSet<string> Installed()
    {
        var stamp = File.GetLastWriteTimeUtc(InstalledFile); // a missing file: 1601, also a stamp
        if (installedCache != null && stamp == installedStamp) return installedCache;
        try { installedCache = File.ReadAllLines(InstalledFile).Select(l => l.Trim()).ToHashSet(); }
        catch { installedCache = []; }
        installedStamp = stamp;
        return installedCache;
    }

    // All rows, plus Remove's: every installed package from Install, under the same submenus.
    public static Dictionary<string, MenuEntry> Load(HashSet<string> installed)
    {
        var stamp = File.GetLastWriteTimeUtc(MenuFile);
        if (entriesCache != null && stamp == entriesStamp && ReferenceEquals(installed, entriesInstalled)) return entriesCache;
        var entries = new Dictionary<string, MenuEntry>();
        var order = new List<string>();
        foreach (var raw in File.ReadAllLines(MenuFile))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var parts = line.Split('|', 4).Select(p => p.Trim()).ToArray();
            if (parts.Length < 3) continue;
            string? action = parts.Length == 4 && parts[3].Length > 0 ? parts[3] : null;
            bool confirm = action?.StartsWith('!') == true;
            if (confirm) action = action![1..].Trim();
            string? package = null;
            if (action != null && action.Split(' ', 2) is [var kind, var id] && (kind == "winget" || kind == "choco"))
                package = $"{kind}:{id.Trim()}".ToLowerInvariant();
            entries[parts[0]] = new MenuEntry(parts[0], parts[1], parts[2], action, confirm, package);
        }
        foreach (var e in entries.Values.ToList())
        {
            if (!e.Id.StartsWith("install.") || e.Package == null || !installed.Contains(e.Package)) continue;
            var parts = e.Id.Split('.');
            for (int i = 2; i < parts.Length; i++)
            {
                var source = entries[string.Join('.', parts[..i])];
                var to = "remove." + string.Join('.', parts[1..i]);
                entries.TryAdd(to, source with { Id = to, Action = null, Confirm = false, Package = null });
            }
            var removeId = "remove." + string.Join('.', parts[1..]);
            entries[removeId] = e with { Id = removeId, Action = $"un{e.Kind} {e.Argument}", Confirm = true, Package = null };
        }
        (entriesCache, entriesStamp, entriesInstalled) = (entries, stamp, installed);
        return entries;
    }

    public static List<MenuEntry> Children(Dictionary<string, MenuEntry> entries, string parent) =>
        entries.Values.Where(e => e.Parent == parent).ToList();

    // "Install › Development" for install.development.rust's parent.
    public static string PathLabel(Dictionary<string, MenuEntry> entries, string id)
    {
        var labels = new List<string>();
        var parts = id.Split('.', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 1; i <= parts.Length; i++)
            if (entries.TryGetValue(string.Join('.', parts[..i]), out var e)) labels.Add(e.Label);
        return string.Join(" › ", labels);
    }

    // Typing searches every row in the menu: all words must appear in the row's label or in
    // the labels of the submenus it sits in. Rows whose own label starts with the text come first.
    public static List<MenuEntry> Search(Dictionary<string, MenuEntry> entries, string query)
    {
        var words = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return entries.Values
            .Select(e => (e, text: (PathLabel(entries, e.Id) + " " + e.Id.Split('.')[^1]).ToLowerInvariant()))
            .Where(x => words.All(w => x.text.Contains(w)))
            .OrderBy(x => x.e.Label.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 : x.e.Label.Contains(query, StringComparison.OrdinalIgnoreCase) ? 1 : 2)
            .ThenBy(x => x.e.IsSubmenu ? 1 : 0)
            .Select(x => x.e).Take(60).ToList();
    }

    public static string CurrentThemeName()
    {
        try { var n = File.ReadAllText(CurrentThemeFile).Trim(); if (n.Length > 0) return n; } catch { }
        return "starship";
    }

    public static Theme LoadTheme(string name)
    {
        try
        {
            var json = File.ReadAllText(Path.Combine(ThemesDir, name + ".json"));
            return JsonSerializer.Deserialize<Theme>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new Theme();
        }
        catch { return name != "starship" ? LoadTheme("starship") : new Theme(); } // omawin's default theme
    }

    public static List<(string File, Theme Theme)> Themes() =>
        Directory.Exists(ThemesDir)
            ? Directory.GetFiles(ThemesDir, "*.json").Select(f => (Path.GetFileNameWithoutExtension(f), LoadTheme(Path.GetFileNameWithoutExtension(f))))
                .OrderBy(t => t.Item2.Name).ToList()
            : [];
}
