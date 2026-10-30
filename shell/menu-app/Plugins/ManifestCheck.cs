using System.Text.RegularExpressions;

namespace SystemMenu.Plugins;

// What the manifest check needs to know about the omawin it runs in, passed in so the check stays
// pure: the registry, `omawin plugin check` and CI all use the same rules.
public sealed record CheckContext(Version OmawinVersion, bool FirstParty, IReadOnlySet<string> AdminVerbs);

// Checks a manifest (plans/05-plugins.md §2.2): errors stop the plugin, warnings are shown.
// Omarchy's own rules first, then omawin's.
public static partial class ManifestCheck
{
    public static readonly string[] Sections = ["left", "centerBegin", "center", "centerEnd", "right"];
    public static readonly HashSet<string> KnownKinds =
        ["bar-widget", "bar", "panel", "overlay", "menu", "service", "page", "launcher-provider", "finder-source", "calendar-provider"];
    static readonly HashSet<string> Runners = ["bun", "pwsh", "uv", "bash", "sh", "python3"]; // bash, sh, python3: side linux only
    static readonly HashSet<string> LinuxRunners = ["bash", "sh", "python3"];
    static readonly HashSet<string> Permissions = ["network", "processes", "clipboard", "notifications", "admin"]; // + files:<folder>
    static readonly HashSet<string> Events =
        ["glazewm.focus_changed", "glazewm.workspace_activated", "glazewm.window_managed", "glazewm.window_unmanaged", "glazewm.monitor_updated", "theme", "power", "audio"];
    static readonly HashSet<string> SettingTypes = ["string", "integer", "number", "boolean", "enum", "color", "path", "secret"];

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$")] private static partial Regex IdShape();
    // GlazeWM's bindings: keys joined by "+" ("lwin+shift+w"). (check) against GlazeWM's full key list.
    [GeneratedRegex(@"^(lwin|rwin|alt|ctrl|shift|[a-z0-9]|f[0-9]{1,2}|space|enter|tab|escape|left|right|up|down)(\+(lwin|rwin|alt|ctrl|shift|[a-z0-9]|f[0-9]{1,2}|space|enter|tab|escape|left|right|up|down))*$")]
    private static partial Regex KeyShape();

    public static (List<string> Errors, List<string> Warnings) Check(Manifest m, string dir, CheckContext ctx)
    {
        var e = new List<string>();
        var w = new List<string>();
        if (m.SchemaVersion != 1) e.Add($"schemaVersion {m.SchemaVersion}: only 1 is known");
        if (string.IsNullOrEmpty(m.Id) || !IdShape().IsMatch(m.Id) || m.Id.Contains("..")) e.Add($"id '{m.Id}': letters, digits, . _ -; no '..' or '/'");
        if (string.IsNullOrEmpty(m.Name)) e.Add("name is missing");
        if (string.IsNullOrEmpty(m.Version)) e.Add("version is missing");
        if (m.Kinds.Count == 0) e.Add("kinds must be a non-empty list");
        foreach (var k in m.Kinds.Where(k => !KnownKinds.Contains(k))) w.Add($"kind '{k}' is unknown here (ignored)");
        if (m.Has("bar")) w.Add("kind 'bar' (a whole bar) isn't supported on omawin");
        // Omarchy requires entryPoints; an omawin-only plugin may leave them out.
        if (m.EntryPoints == null && m.Omawin == null) e.Add("entryPoints (Omarchy) or an omawin block is needed");
        var folder = Path.GetFileName(dir.TrimEnd('\\', '/'));
        if (!string.Equals(folder, m.Id, StringComparison.OrdinalIgnoreCase))
            w.Add($"folder '{folder}' isn't named after the id '{m.Id}'");
        if (m.BarWidget is { } bw && !Sections.Contains(bw.DefaultSection)) e.Add($"barWidget.defaultSection '{bw.DefaultSection}'");
        foreach (var f in m.BarWidget?.Schema ?? [])
        {
            if (!SettingTypes.Contains(f.Type)) e.Add($"setting '{f.Key}': type '{f.Type}'");
            else if (f.Type == "enum" && (f.Options == null || f.Options.Count == 0)) e.Add($"setting '{f.Key}': enum without options");
        }
        foreach (var (key, file) in m.EntryPoints ?? []) if (!Inside(dir, file)) e.Add($"entryPoints.{key} '{file}' leaves the plugin's folder");

        if (m.Omawin is not { } o)
        {
            w.Add("no omawin block: only its menu, items and scripts can be used (or it runs in the QML host)");
            return (e, w);
        }
        if (o.MinVersion != null && Version.TryParse(o.MinVersion, out var min) && min > ctx.OmawinVersion) e.Add($"needs omawin {o.MinVersion}");
        if (o.Side is not ("windows" or "linux")) e.Add($"side '{o.Side}': windows or linux");
        if (o.Interval is < 1) e.Add("interval: at least 1 second");
        if (o.Command is { Count: > 0 } cmd)
        {
            var first = cmd[0];
            bool runner = Runners.Contains(first);
            if (Path.IsPathRooted(first) || first.StartsWith('/') || first.StartsWith('\\'))
                e.Add("command: an absolute path; use a file in the plugin or a runner (bun, pwsh, uv)");
            else if (!runner && !first.Contains('.'))
                e.Add($"command '{first}': a file in the plugin, or bun, pwsh, uv");
            else if (!runner && !Inside(dir, first))
                e.Add($"command '{first}' isn't a file in the plugin");
            else if (o.Side == "windows" && (LinuxRunners.Contains(first) || first.EndsWith(".sh", StringComparison.OrdinalIgnoreCase)))
                e.Add("a bash or Python command needs \"side\": \"linux\"");
            foreach (var arg in cmd.Skip(1).Where(a => a.Contains('/') || a.Contains('\\')))
                if (!Inside(dir, arg)) w.Add($"command argument '{arg}' points outside the plugin");
        }
        else if (m.Has("bar-widget") || m.Has("service") || m.Has("launcher-provider") || m.Has("finder-source") || m.Has("calendar-provider"))
        {
            e.Add("these kinds need omawin.command");
        }
        foreach (var p in new[] { o.Panel, o.Menu, o.Items }.Concat(o.Panels.Select(x => x.File)).Concat(o.Pages.Select(x => x.File)))
            if (p != null && !(Inside(dir, p) && File.Exists(Path.Combine(dir, p)))) e.Add($"'{p}' is missing or outside the plugin");
        if (o.Widget.Style is not ("text" or "meter" or "ring" or "slot")) e.Add($"widget.style '{o.Widget.Style}'");
        foreach (var p in o.Permissions.Where(p => !Permissions.Contains(p) && !p.StartsWith("files:"))) w.Add($"permission '{p}' is unknown");
        foreach (var ev in o.Events.Where(x => !Events.Contains(x))) w.Add($"event '{ev}' is unknown");
        foreach (var k in o.Keys.Where(k => !KeyShape().IsMatch(k.Binding))) e.Add($"key '{k.Binding}'");
        if (o.AdminVerbs.Count > 0 && !ctx.FirstParty) e.Add("adminVerbs: first-party plugins only");
        foreach (var v in o.AdminVerbs.Where(v => !ctx.AdminVerbs.Contains(v))) e.Add($"admin verb '{v}' isn't one of omawin's");
        if (o.MemoryMb is < 16 or > 2048) e.Add("memoryMb: 16 to 2048");
        return (e, w);
    }

    // A relative path that stays inside dir once resolved: no "..", no drive, no root, no UNC.
    public static bool Inside(string dir, string relative)
    {
        if (string.IsNullOrEmpty(relative) || Path.IsPathRooted(relative) || relative.StartsWith('\\') || relative.StartsWith('/')
            || (relative.Length > 1 && relative[1] == ':'))
            return false;
        // Both separators mean a folder to the plugin author, on any OS.
        var normalized = relative.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var root = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(dir, normalized));
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}
