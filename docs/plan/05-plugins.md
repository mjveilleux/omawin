# 05 · Plugins: how (Phase 4, 8.0)

> **Uncompiled sketch.** Nothing here has been built or run. It names real files and functions in this repo as of 2026-10-30, follows [AGENTS.md](../../AGENTS.md), and marks every API it isn't sure of with *(check)*. The design and its reasons are in [plugins.md](../plugins.md); the what and why in [roadmap.md](../roadmap.md) Phase 4 (4.1-4.10) and 8.0. Build it, test it, and fix this file where reality differs.

Constraints that shape every line below:

- The menu app (`menu-app/`) is **.NET 9, WinUI 3, Native AOT**: no loading DLLs at run time, no reflection. JSON goes through source-generated contexts or `JsonNode` / `Utf8JsonWriter`; P/Invoke through `LibraryImport`; XAML bindings through our own attached properties, never `{Binding}`.
- **Plugin code never runs in the shell.** Logic is a process (JSON lines on stdin/stdout); looks are inert XAML the shell loads and fills.
- Omarchy's manifest is kept as is; ours is the `omawin` block. One repo serves both desktops.
- No admin: plugins never elevate; admin work is a fixed verb list run by one short elevated process (2.9).

## 0. Map

| Piece | Files | Roadmap | Ships in |
|---|---|---|---|
| Manifest, validation | `menu-app/Plugins/Manifest.cs`, `ManifestCheck.cs` | 4.1 | v1 |
| `shell.json` (layout, enabled) | `menu-app/Plugins/ShellConfig.cs`; `Bar/Bar.cs` reads it | 4.5 | v1 |
| Registry, hot reload | `menu-app/Plugins/PluginRegistry.cs` | 4.5 | v1 |
| Protocol | `menu-app/Plugins/Protocol.cs` | 4.2 | v1 |
| Processes | `menu-app/Plugins/JobObject.cs`, `PluginProcess.cs`, `PluginLog.cs` | 4.2 | v1 |
| Shared PowerShell host | `menu-app/Plugins/PwshHost.cs`, `plugins/sdk/ps/pwsh-host.ps1`, `plugins/sdk/ps/Omawin.Plugin.ps1` | plugins.md | v3 |
| Event relay | `menu-app/Plugins/EventRelay.cs`; an `Event` on `Bar/GlazeWm.cs` | plugins.md | v2 |
| Bar widget | `menu-app/Bar/CommandWidget.cs`; `BarWidget.Create` fallback in `Bar/Widgets.cs` | 4.2 | v1 |
| Theme resources | `menu-app/App.xaml` (brushes, styles), `menu-app/Plugins/ThemeResources.cs` | 4.3 | v2 |
| Panels | `menu-app/Plugins/Bind.cs` (`Omawin.Plugins`), `PanelBinding.cs`, `PanelCheck.cs`, `PanelHost.cs` | 4.3 | v2 |
| Menu / items / script commands | `menu-app/MenuModel.cs`, `menu-app/Plugins/Fragments.cs` | 4.4, 4.8 | v1 |
| Providers | `menu-app/Plugins/Providers.cs` | 8.0, plugins.md | v2 |
| IPC | `App.xaml.cs` (request format), `bin/omawin.cs` (`Send`), `menu-app/Plugins/ShellIpc.cs` | 4.7 | v1 |
| CLI | `bin/plugin.ps1`, `bin/plugin-templates/`, `bin/commands.txt` | 4.5, 4.9.2 | v1 (scan: v3) |
| Admin verbs | `menu-app/Plugins/AdminQueue.cs`, `bin/admin-verbs.ps1` | 8.0, 2.9 | with 8.x |
| SDK + example | `plugins/sdk/Omawin.Plugin/`, `plugins/omawin.top/` | 8.0 | with 8.x |
| Arch stand-ins | `wsl/omarchy-shims/`, `wsl/setup.sh`, `themes/set-theme.ps1` | 4.9.1 | v3 |
| QML host | `qs-host/` (Qt 6, CMake) | 4.9.3-4 | later |
| Omadoro port | in Omadoro's repo: `omawin/service.ts`, `omawin/Panel.xaml`, `manifest.json` | plugins.md | proof |

Order: §1-§4 and §6 (v1) → move the music and agents widgets to them (4.6) → §5 and §7 (v2; **prove `XamlReader.Load` and the attached properties under AOT first**, §5.0) → §9 with the first phase 8 plugin → §10. The full checklist is §13.

## 1. Requests: one argument per line (4.7, do first)

Arguments with spaces don't survive today (`App.xaml.cs` joins and splits on spaces). Plugins need `omawin shell notify "Two words" "A body"`.

`menu-app/App.xaml.cs`, `Program.Main` (the hand-off) and the reader thread in `Start`:

```csharp
// The hand-off: one argument per line, so spaces survive (omawin shell notify "a b" "c d").
File.WriteAllText(RequestFile, string.Join('\n', args));

// The reader: new format if it has a newline; a lone word or an older omawin.exe's
// space-joined request otherwise.
static string[] SplitRequest(string request) =>
    request.Contains('\n')
        ? request.Split('\n').Select(a => a.TrimEnd('\r')).ToArray()
        : request.Split(' ', StringSplitOptions.RemoveEmptyEntries);
// ...
queue.TryEnqueue(() => app.HandleRequest(SplitRequest(request)));
```

Careful: `request` is `.Trim()`med today; trim only `\r\n` at the ends, never inner blanks (an empty argument is legal).

`bin/omawin.cs` (C# 5: no `$""` interpolation with newer features, no `=>` properties beyond C# 5 rules, `string.Join("\n", ...)`): `Send(string request)` becomes `Send(string[] args)`; `app` rows build the list (the row's words, then the typed words as they came, not re-split):

```csharp
static int Send(string[] args)
{
    EventWaitHandle signal;
    if (EventWaitHandle.TryOpenExisting(@"Local\dotfiles-system-menu-signal", out signal))
    {
        Directory.CreateDirectory(StateDir);
        File.WriteAllText(Path.Combine(StateDir, "system-menu-request"), string.Join("\n", args));
        AllowSetForegroundWindow(-1);
        signal.Set();
        signal.Dispose();
        return 0;
    }
    // not running: start it with the arguments quoted one by one (Quote: "a b" -> "\"a b\"")
    var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "system-menu", "SystemMenu.exe");
    if (!File.Exists(exe)) return Fail("the menu app isn't built: " + exe + " (omawin restart menu builds it)");
    Process.Start(new ProcessStartInfo(exe, string.Join(" ", args.Select(Quote).ToArray())) { UseShellExecute = false });
    return 0;
}
```

`HandleRequest` gains one branch (before the final `else`):

```csharp
else if (args.Length > 1 && args[0] == "--shell") Plugins.ShellIpc.Handle(args[1..]);
```

`bin/commands.txt`:

```
shell | The shell's IPC, as Omarchy's omarchy-shell: toggle <id> [panel], summon <id> <overlay>, close, reload [id], call <id> <action> [json], put-bar-widget <id> <section>, notify <title> [body], open <https-url|file>, propose-key <binding> <action> | app --shell
```

`menu-app/Plugins/ShellIpc.cs`:

```csharp
namespace SystemMenu.Plugins;

// omawin shell <method> ... (SystemMenu --shell ...): Omarchy's omarchy-shell methods, for
// scripts, plugins and the Arch stand-ins. One way: answers go to files the CLI reads
// (%LOCALAPPDATA%\launcher\plugins\status.json), since the request file has no reply channel.
public static class ShellIpc
{
    public static void Handle(string[] a)
    {
        string At(int i) => i < a.Length ? a[i] : "";
        switch (a[0])
        {
            case "toggle": PanelHost.Toggle(At(1), At(2)); break;              // id [panel name]
            case "summon": PanelHost.Open(At(1), At(2), overlay: true); break;  // id overlay-name
            case "close": PanelHost.CloseAll(); break;
            case "reload": PluginRegistry.Rescan(At(1)); break;                 // all, or one id
            case "call": PluginRegistry.SendAction(At(1), At(2), At(3)); break; // id action [json args]
            case "put-bar-widget": ShellConfig.PutWidget(At(1), At(2)); break;  // writes shell.json; the watcher relays it
            case "notify": Notifier.Show(At(1), At(2), open: null); break;      // 5.5's toasts
            case "open": Opener.Open(At(1)); break;                             // https or an existing file only
            case "propose-key": KeyProposals.Add(At(1), At(2), "omawin shell"); break;
            default: App.Log(new Exception("omawin shell: unknown method " + a[0])); break;
        }
    }
}
```

`Notifier` is 5.5's toast helper (`AppNotificationManager`, see 06-dx-finder-sandbox.md) *(check: the name it lands under)*. `Opener.Open` accepts `https://…` and paths that exist; anything else is logged and dropped.

## 2. The manifest, `shell.json` and the registry (4.1, 4.5)

### 2.1 `menu-app/Plugins/Manifest.cs`

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace SystemMenu.Plugins;

// manifest.json: Omarchy's schema (shell/services/PluginRegistry.qml in Omarchy), unchanged,
// plus the `omawin` block. Omarchy fields we don't use (entryPoints: QML) are kept, so a
// clone or a check round-trips them.
public sealed class Manifest
{
    public int SchemaVersion { get; set; }
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string? Author { get; set; }
    public string? License { get; set; }
    public string? Description { get; set; }
    public List<string> Kinds { get; set; } = [];
    public string? Activation { get; set; }          // "startup" | "on-demand" (default)
    public bool KeepLoaded { get; set; }
    public Dictionary<string, string>? EntryPoints { get; set; }
    public BarWidgetInfo? BarWidget { get; set; }
    public OmawinBlock? Omawin { get; set; }
    public JsonObject? Omarchy { get; set; }         // clonedFrom, capabilities: carried through
    [JsonExtensionData] public Dictionary<string, JsonElement>? Rest { get; set; }

    public bool Has(string kind) => Kinds.Contains(kind);
    // Headless services run from the start; everything else when first shown (plugins.md).
    public bool StartsAtStartup => Activation == "startup" || Has("service") || Has("launcher-provider") && Omawin?.Provider?.Always == true;
}

public sealed class BarWidgetInfo
{
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public bool AllowMultiple { get; set; }
    public string DefaultSection { get; set; } = "right";
    public JsonObject? Defaults { get; set; }
    public List<SettingField> Schema { get; set; } = [];
}

public sealed class SettingField
{
    public string Key { get; set; } = "";
    public string Type { get; set; } = "string";     // string, integer, number, boolean, enum, color, secret (ours)
    public string? Label { get; set; }
    public string? Description { get; set; }
    public JsonNode? DefaultValue { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public double? Step { get; set; }
    public List<string>? Options { get; set; }
}

public sealed class OmawinBlock
{
    public string? MinVersion { get; set; }
    public List<string>? Command { get; set; }       // argv, no shell parsing
    public string Side { get; set; } = "windows";    // windows | linux (WSL)
    public int? Interval { get; set; }               // seconds: Waybar's exec + interval; null: long-running
    public bool? SharedHost { get; set; }            // .ps1 only: false keeps it out of the shared pwsh host
    public string? Panel { get; set; }               // the widget's drop-down
    public List<PanelInfo> Panels { get; set; } = []; // more: overlays, extra panels
    public List<PageInfo> Pages { get; set; } = [];   // `page` kind: launcher pages
    public string? Menu { get; set; }                // menu.txt fragment
    public string? Items { get; set; }               // items.txt fragment
    public string Commands { get; set; } = "commands"; // script commands folder
    public WidgetInfo Widget { get; set; } = new();
    public ProviderInfo? Provider { get; set; }      // launcher-provider / finder-source
    public List<string> Permissions { get; set; } = [];
    public List<string> Events { get; set; } = [];   // "glazewm.focus_changed", "theme", "power", "audio"
    public List<KeyProposal> Keys { get; set; } = [];
    public List<string> AdminVerbs { get; set; } = []; // first-party only (§9.6)
    public string? Replaces { get; set; }            // a built-in widget it stands in for ("clock")
    public string? ClonedFrom { get; set; }
    public int MemoryMb { get; set; } = 256;         // the Job Object's cap
}

public sealed class WidgetInfo
{
    public string Style { get; set; } = "text";      // text | meter | ring | slot (a QML host's window, §10.3)
    public string? Click { get; set; }               // "panel" | "action <name>" | "run <argv…>"; default: panel if any, else a click message
    public string? RightClick { get; set; }
    public string? MiddleClick { get; set; }
    public Dictionary<string, string> Classes { get; set; } = []; // plugin class -> theme colour name ("work": "accent")
}

public sealed class PanelInfo { public string Name { get; set; } = ""; public string File { get; set; } = ""; public string Kind { get; set; } = "panel"; } // panel | overlay
public sealed class PageInfo { public string Name { get; set; } = ""; public string Title { get; set; } = ""; public string? Icon { get; set; } public string File { get; set; } = ""; }
public sealed class ProviderInfo { public string? Prefix { get; set; } public string? Title { get; set; } public string? Icon { get; set; } public bool Always { get; set; } public string? Chip { get; set; } }
public sealed class KeyProposal { public string Binding { get; set; } = ""; public string Action { get; set; } = ""; public string? Description { get; set; } }

// One context for everything the plugin system reads and writes. Comments and trailing commas
// allowed: people hand-edit manifests and shell.json.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Manifest))]
[JsonSerializable(typeof(ShellConfig))]
[JsonSerializable(typeof(List<PluginStatus>))]
internal partial class PluginJson : JsonSerializerContext { }
```

### 2.2 `menu-app/Plugins/ManifestCheck.cs`

The same rules serve the registry, `omawin plugin check` (through `SystemMenu.exe --plugin-check`, §8.2) and CI. Omarchy's own rules first, then ours.

```csharp
using System.Text.RegularExpressions;

namespace SystemMenu.Plugins;

public static partial class ManifestCheck
{
    public static readonly string[] Sections = ["left", "centerBegin", "center", "centerEnd", "right"];
    public static readonly HashSet<string> KnownKinds =
        ["bar-widget", "bar", "panel", "overlay", "menu", "service", "page", "launcher-provider", "finder-source", "calendar-provider"];
    static readonly HashSet<string> Runners = ["bun", "pwsh", "uv", "bash", "sh", "python3"]; // bash/sh/python3: side linux only
    static readonly HashSet<string> Permissions = ["network", "processes", "clipboard", "notifications", "admin"]; // + files:<folder>
    static readonly HashSet<string> Events = ["glazewm.focus_changed", "glazewm.workspace_activated", "glazewm.window_managed", "glazewm.window_unmanaged", "glazewm.monitor_updated", "theme", "power", "audio"];

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$")] private static partial Regex IdShape();
    [GeneratedRegex(@"^(lwin|rwin|alt|ctrl|shift|\+|[a-z0-9]|f[0-9]{1,2}|space|enter|tab|escape|left|right|up|down)+$")] private static partial Regex KeyShape(); // (check) against GlazeWM's binding grammar

    // Problems that stop the plugin (errors) and ones shown only (warnings).
    public static (List<string> Errors, List<string> Warnings) Check(Manifest m, string dir)
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
        // Omarchy requires entryPoints; an omawin-only plugin may leave it out.
        if (m.EntryPoints == null && m.Omawin == null) e.Add("entryPoints (Omarchy) or an omawin block is needed");
        if (!string.Equals(Path.GetFileName(dir.TrimEnd('\\', '/')), m.Id, StringComparison.OrdinalIgnoreCase))
            w.Add($"folder '{Path.GetFileName(dir)}' isn't named after the id '{m.Id}'");
        if (m.BarWidget is { } bw && !Sections.Contains(bw.DefaultSection)) e.Add($"barWidget.defaultSection '{bw.DefaultSection}'");
        foreach (var f in m.BarWidget?.Schema ?? [])
            if (f.Type is not ("string" or "integer" or "number" or "boolean" or "enum" or "color" or "secret")) e.Add($"setting '{f.Key}': type '{f.Type}'");
            else if (f.Type == "enum" && (f.Options == null || f.Options.Count == 0)) e.Add($"setting '{f.Key}': enum without options");
        foreach (var (key, file) in m.EntryPoints ?? []) if (!Inside(dir, file)) e.Add($"entryPoints.{key} '{file}' leaves the plugin's folder");

        if (m.Omawin is not { } o) { w.Add("no omawin block: only its menu, items and scripts can be used (or it runs in the QML host, §10.3)"); return (e, w); }
        if (o.MinVersion != null && Version.TryParse(o.MinVersion, out var min) && min > App.OmawinVersion) e.Add($"needs omawin {o.MinVersion}");
        if (o.Side is not ("windows" or "linux")) e.Add($"side '{o.Side}': windows or linux");
        if (o.Interval is < 1) e.Add("interval: at least 1 second");
        if (o.Command is { Count: > 0 } cmd)
        {
            var first = cmd[0];
            bool file = first.Contains('.') && !Runners.Contains(first);
            if (Path.IsPathRooted(first)) e.Add("command: an absolute path; use a file in the plugin or a runner (bun, pwsh, uv)");
            else if (file && !Inside(dir, first)) e.Add($"command '{first}' isn't a file in the plugin");
            else if (!file && !Runners.Contains(first)) e.Add($"command '{first}': a file in the plugin, or bun, pwsh, uv");
            else if (o.Side == "windows" && (first is "bash" or "sh" or "python3" || first.EndsWith(".sh"))) e.Add("a bash script needs \"side\": \"linux\"");
            foreach (var arg in cmd.Skip(1).Where(a => a.Contains('/') || a.Contains('\\')))
                if (!Inside(dir, arg)) w.Add($"command argument '{arg}' points outside the plugin");
        }
        else if (m.Has("bar-widget") || m.Has("service") || m.Has("launcher-provider") || m.Has("finder-source") || m.Has("calendar-provider"))
            e.Add("these kinds need omawin.command");
        foreach (var p in new[] { o.Panel, o.Menu, o.Items }.Concat(o.Panels.Select(x => x.File)).Concat(o.Pages.Select(x => x.File)))
            if (p != null && !(Inside(dir, p) && File.Exists(Path.Combine(dir, p)))) e.Add($"'{p}' is missing or outside the plugin");
        if (o.Widget.Style is not ("text" or "meter" or "ring" or "slot")) e.Add($"widget.style '{o.Widget.Style}'");
        foreach (var p in o.Permissions.Where(p => !Permissions.Contains(p) && !p.StartsWith("files:"))) w.Add($"permission '{p}' is unknown");
        foreach (var ev in o.Events.Where(x => !Events.Contains(x))) w.Add($"event '{ev}' is unknown");
        foreach (var k in o.Keys.Where(k => !KeyShape().IsMatch(k.Binding))) e.Add($"key '{k.Binding}'");
        if (o.AdminVerbs.Count > 0 && !PluginRegistry.IsFirstPartyDir(dir)) e.Add("adminVerbs: first-party plugins only");
        foreach (var v in o.AdminVerbs.Where(v => !AdminQueue.Verbs.ContainsKey(v))) e.Add($"admin verb '{v}' isn't one of omawin's");
        if (o.MemoryMb is < 16 or > 2048) e.Add("memoryMb: 16 to 2048");
        return (e, w);
    }

    // A relative path that stays inside dir after resolving (no .., no drive, no UNC).
    public static bool Inside(string dir, string relative)
    {
        if (string.IsNullOrEmpty(relative) || Path.IsPathRooted(relative) || relative.StartsWith('\\') || relative.StartsWith('/')) return false;
        var root = Path.GetFullPath(dir).TrimEnd('\\') + '\\';
        var full = Path.GetFullPath(Path.Combine(dir, relative));
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}
```

`App.OmawinVersion`: parse the `InformationalVersion` (`v0.9.1` → `0.9.1`; `(dev)` → `Version(999,0)`) once.

### 2.3 `menu-app/Plugins/ShellConfig.cs`

Omarchy's `shell.json`, in the account folder (`~/omawin-account/shell.json`). Absent: built from the repo's `zebar/tokyo-islands/bar.json` (string arrays per section today). Unknown fields kept.

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace SystemMenu.Plugins;

public sealed class ShellConfig
{
    public BarConfig Bar { get; set; } = new();
    public List<string> Plugins { get; set; } = [];          // added plugins turned on
    public List<string> DisabledPlugins { get; set; } = [];  // built-ins turned off (and added ones, to be sure)
    public List<string> AcceptedKeys { get; set; } = [];     // "<id> <binding>": proposed keys you accepted
    public Dictionary<string, JsonObject> PluginSettings { get; set; } = []; // settings of plugins with no bar placement (services, providers)
    [JsonExtensionData] public Dictionary<string, JsonElement>? Rest { get; set; }

    public static string File => Path.Combine(MenuModel.Work, "shell.json");
    static string Default => Path.Combine(MenuModel.Repo, @"zebar\tokyo-islands\bar.json"); // moves with 1.6

    public static ShellConfig Load()
    {
        try
        {
            if (System.IO.File.Exists(File))
                return JsonSerializer.Deserialize(System.IO.File.ReadAllText(File), PluginJson.Default.ShellConfig) ?? new();
            // bar.json: { "left": ["menu", "workspaces"], ..., "clock": {...} } -> placements
            var old = JsonNode.Parse(System.IO.File.ReadAllText(Default))!.AsObject();
            var c = new ShellConfig();
            foreach (var s in ManifestCheck.Sections)
                if (old[s] is JsonArray names) c.Bar.Layout.Section(s).AddRange(names.Select(n => new WidgetPlacement { Id = n!.GetValue<string>() }));
            if (old["clock"] is JsonObject clock) c.Bar.Clock = clock.DeepClone().AsObject();
            return c;
        }
        catch (Exception ex) { App.Log(ex); return new(); }
    }

    // Written whole, atomically (the CLI and the shell both write it; last writer wins, the watcher re-reads).
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File)!);
        var tmp = File + ".tmp";
        System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(this, PluginJson.Default.ShellConfig));
        System.IO.File.Move(tmp, File, overwrite: true);
    }

    // One setting (a plugin's `set`, a panel's `set:<key>`): into its placement, or PluginSettings
    // for a plugin that has none. The shell.json watcher then sends `settings` to the process.
    public static void SetSetting(string id, string instance, string key, JsonNode? value)
    {
        var c = Load();
        var place = ManifestCheck.Sections.SelectMany(s => c.Bar.Layout.Section(s)).FirstOrDefault(p => p.Id == id && p.Instance == instance);
        JsonObject target;
        if (place != null) target = place.Settings ??= [];
        else if (!c.PluginSettings.TryGetValue(id, out target!)) c.PluginSettings[id] = target = [];
        target[key] = value?.DeepClone();
        c.Save();
    }

    public static void PutWidget(string id, string section)
    {
        if (!ManifestCheck.Sections.Contains(section)) return;
        var c = Load();
        foreach (var s in ManifestCheck.Sections) c.Bar.Layout.Section(s).RemoveAll(p => p.Id == id && !(PluginRegistry.Get(id)?.Manifest.BarWidget?.AllowMultiple ?? false));
        c.Bar.Layout.Section(section).Add(new WidgetPlacement { Id = id });
        c.Save();
    }
}

public sealed class BarConfig
{
    public BarLayout Layout { get; set; } = new();
    public JsonObject? Clock { get; set; }   // bar.json's clock formats, kept
    [JsonExtensionData] public Dictionary<string, JsonElement>? Rest { get; set; }
}

public sealed class BarLayout
{
    public List<WidgetPlacement> Left { get; set; } = [];
    public List<WidgetPlacement> CenterBegin { get; set; } = [];
    public List<WidgetPlacement> Center { get; set; } = [];
    public List<WidgetPlacement> CenterEnd { get; set; } = [];
    public List<WidgetPlacement> Right { get; set; } = [];
    public List<WidgetPlacement> Section(string s) => s switch
    { "left" => Left, "centerBegin" => CenterBegin, "center" => Center, "centerEnd" => CenterEnd, _ => Right };
}

// An entry in bar.layout.<section>[]: "clock", or { "id": "acme.weather", "settings": {...} }.
[JsonConverter(typeof(WidgetPlacementConverter))]
public sealed class WidgetPlacement
{
    public string Id { get; set; } = "";
    public JsonObject? Settings { get; set; }
    // allowMultiple: each placement is its own instance (its own process and settings).
    public string Instance => Settings?["instance"]?.GetValue<string>() ?? Id;
}

// Source-generated contexts honour [JsonConverter] on a type, and this one uses no reflection.
public sealed class WidgetPlacementConverter : JsonConverter<WidgetPlacement>
{
    public override WidgetPlacement Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
    {
        if (r.TokenType == JsonTokenType.String) return new WidgetPlacement { Id = r.GetString() ?? "" };
        var node = JsonNode.Parse(ref r)?.AsObject() ?? [];
        var p = new WidgetPlacement { Id = node["id"]?.GetValue<string>() ?? "" };
        // Omarchy puts settings inline next to id, or under "settings": accept both.
        if (node["settings"] is JsonObject s) p.Settings = s.DeepClone().AsObject();
        else { node.Remove("id"); if (node.Count > 0) p.Settings = node; }
        return p;
    }
    public override void Write(Utf8JsonWriter w, WidgetPlacement p, JsonSerializerOptions o)
    {
        if (p.Settings is null or { Count: 0 }) { w.WriteStringValue(p.Id); return; }
        w.WriteStartObject();
        w.WriteString("id", p.Id);
        w.WritePropertyName("settings");
        p.Settings.WriteTo(w);
        w.WriteEndObject();
    }
}
```

*(check)* Omarchy's exact layout entry shape (`{id, ...inline}` vs `{id, settings}`) in `shell/services/` `BarLayout`/`ShellConfig`; the converter reads both.

`Bar/Bar.cs`: `ReadLayout()` becomes `config = ShellConfig.Load()`, clock formats from `config.Bar.Clock`; `Open()` iterates `config.Bar.Layout.Section(place)` and calls `BarWidget.Create(placement)` (§4.1). The registry's `Changed` event calls a new `Bar.Relayout()` (`Hide(); Show();` on the UI thread, without the arrival fade).

### 2.4 `menu-app/Plugins/PluginRegistry.cs`

```csharp
using System.Text.Json;
using Microsoft.UI.Dispatching;

namespace SystemMenu.Plugins;

public enum PluginOrigin { BuiltIn, Shared, Account }

public sealed class Plugin
{
    public required Manifest Manifest { get; init; }
    public required string Dir { get; init; }
    public required PluginOrigin Origin { get; init; }
    public bool Enabled { get; set; }
    public List<string> Errors { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
    public string Id => Manifest.Id;
    public string DataDir => Path.Combine(MenuModel.StateDir, "plugins", Id);
    public bool Usable => Enabled && Errors.Count == 0;
    // One process per instance (allowMultiple widgets), shared by every monitor's bar.
    public readonly Dictionary<string, PluginProcess> Processes = [];
    public readonly HashSet<string> StalePanels = [];
}

public sealed record PluginStatus(string Id, string Origin, bool Enabled, string Version, string State, int Pid, long MemoryBytes, string[] Errors);

public static class PluginRegistry
{
    static readonly Dictionary<string, Plugin> plugins = new(StringComparer.OrdinalIgnoreCase);
    public static event Action? Changed; // on the UI thread: bar layout, menus, pages
    public static long Generation { get; private set; } // bumped by each scan: MenuModel's cache key
    static DispatcherQueue? ui;
    static ShellConfig config = new();

    static (string Dir, PluginOrigin Origin)[] Roots =>
    [
        (Path.Combine(MenuModel.Repo, "plugins"), PluginOrigin.BuiltIn),
        (Path.Combine(MenuModel.Shared, "plugins"), PluginOrigin.Shared),
        (Path.Combine(MenuModel.Work, "plugins"), PluginOrigin.Account),
    ];

    public static bool IsFirstPartyDir(string dir) =>
        Path.GetFullPath(dir).StartsWith(Path.GetFullPath(Path.Combine(MenuModel.Repo, "plugins")) + "\\", StringComparison.OrdinalIgnoreCase);

    public static Plugin? Get(string id) => plugins.GetValueOrDefault(id);
    public static IEnumerable<Plugin> Enabled => plugins.Values.Where(p => p.Usable);

    // From App.OnLaunched, after Bar.Start (the bar's widgets ask for their plugins while it builds).
    public static void Start()
    {
        ui = DispatcherQueue.GetForCurrentThread();
        Scan();
        Watch();
        foreach (var p in Enabled.Where(p => p.Manifest.StartsAtStartup)) Acquire(p, p.Id);
    }

    static void Scan()
    {
        config = ShellConfig.Load();
        Generation++;
        var found = new Dictionary<string, Plugin>(StringComparer.OrdinalIgnoreCase);
        foreach (var (root, origin) in Roots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                var file = Path.Combine(dir, "manifest.json");
                if (!File.Exists(file)) continue; // plugins/sdk, a half-cloned repo
                var p = Read(dir, file, origin);
                if (found.TryGetValue(p.Id, out var earlier))
                    p.Warnings.Add($"also in {earlier.Dir}: this one ({origin}) wins");
                found[p.Id] = p; // account over shared over built-in
            }
        }
        foreach (var p in found.Values)
            p.Enabled = !config.DisabledPlugins.Contains(p.Id, StringComparer.OrdinalIgnoreCase)
                && (p.Origin == PluginOrigin.BuiltIn || config.Plugins.Contains(p.Id, StringComparer.OrdinalIgnoreCase));
        // Stop what's gone or off; keep processes of plugins that are unchanged.
        foreach (var old in plugins.Values)
            if (!found.TryGetValue(old.Id, out var now) || !now.Usable || now.Dir != old.Dir) StopAll(old);
            else foreach (var kv in old.Processes) now.Processes[kv.Key] = kv.Value;
        plugins.Clear();
        foreach (var p in found.Values) plugins[p.Id] = p;
        // shell.json may have changed a placement's settings: tell the running processes.
        foreach (var p in plugins.Values)
            foreach (var (instance, proc) in p.Processes)
            {
                var now = Settings(p, Placement(p.Id, instance));
                if (now.ToJsonString() != proc.Settings.ToJsonString()) proc.UpdateSettings(now);
            }
        WriteStatus();
    }

    static WidgetPlacement? Placement(string id, string instance) =>
        ManifestCheck.Sections.SelectMany(s => config.Bar.Layout.Section(s)).FirstOrDefault(x => x.Id == id && x.Instance == instance);

    static Plugin Read(string dir, string file, PluginOrigin origin)
    {
        Manifest m;
        try { m = JsonSerializer.Deserialize(File.ReadAllText(file), PluginJson.Default.Manifest) ?? new(); }
        catch (Exception ex) { m = new Manifest { Id = Path.GetFileName(dir), Name = Path.GetFileName(dir) }; return new Plugin { Manifest = m, Dir = dir, Origin = origin, Errors = [$"manifest.json: {ex.Message}"] }; }
        var (errors, warnings) = ManifestCheck.Check(m, dir);
        foreach (var x in errors) PluginLog.For(m.Id).Write("error", "manifest: " + x);
        return new Plugin { Manifest = m, Dir = dir, Origin = origin, Errors = errors, Warnings = warnings };
    }

    // --- processes: started when first wanted, stopped when nothing wants them -----------------
    static readonly Dictionary<string, int> users = [];

    public static PluginProcess? Acquire(Plugin p, string instance, WidgetPlacement? place = null)
    {
        if (!p.Usable || p.Manifest.Omawin?.Command is not { Count: > 0 }) return null;
        var key = p.Id + "#" + instance;
        users[key] = users.GetValueOrDefault(key) + 1;
        if (!p.Processes.TryGetValue(instance, out var proc))
        {
            proc = PluginProcess.For(p, instance, Settings(p, place));
            p.Processes[instance] = proc;
            proc.Start();
        }
        return proc;
    }

    public static void Release(Plugin p, string instance)
    {
        var key = p.Id + "#" + instance;
        if ((users[key] = users.GetValueOrDefault(key) - 1) > 0) return;
        if (p.Manifest.KeepLoaded || p.Manifest.StartsAtStartup) return;
        if (p.Processes.Remove(instance, out var proc)) proc.Stop();
    }

    static void StopAll(Plugin p) { foreach (var proc in p.Processes.Values) proc.Stop(); p.Processes.Clear(); }

    // The manifest's defaults, then the schema's defaultValues, then the placement's settings;
    // `secret` fields from Credential Manager (never in shell.json).
    public static System.Text.Json.Nodes.JsonObject Settings(Plugin p, WidgetPlacement? place)
    {
        var s = new System.Text.Json.Nodes.JsonObject();
        foreach (var f in p.Manifest.BarWidget?.Schema ?? []) if (f.DefaultValue != null) s[f.Key] = f.DefaultValue.DeepClone();
        foreach (var (k, v) in p.Manifest.BarWidget?.Defaults ?? []) s[k] = v?.DeepClone();
        foreach (var (k, v) in config.PluginSettings.GetValueOrDefault(p.Id) ?? []) s[k] = v?.DeepClone();
        foreach (var (k, v) in place?.Settings ?? []) s[k] = v?.DeepClone();
        foreach (var f in (p.Manifest.BarWidget?.Schema ?? []).Where(f => f.Type == "secret"))
            s[f.Key] = Secrets.Read($"omawin:plugin:{p.Id}:{f.Key}"); // CredRead, §2.5
        return s;
    }

    public static void SendAction(string id, string name, string jsonArgs)
    {
        if (Get(id) is not { Usable: true } p) return;
        var args = string.IsNullOrEmpty(jsonArgs) ? new System.Text.Json.Nodes.JsonObject() : System.Text.Json.Nodes.JsonNode.Parse(jsonArgs) ?? new System.Text.Json.Nodes.JsonObject();
        (Acquire(p, p.Id) ?? null)?.Send(Protocol.Action(name, args));
    }

    public static void Broadcast(string line, Func<Plugin, bool>? to = null)
    {
        foreach (var p in Enabled.Where(to ?? (_ => true)))
            foreach (var proc in p.Processes.Values) proc.Send(line);
    }

    public static void Rescan(string id = "") { Scan(); Changed?.Invoke(); }

    // What a panel's `settings.x` paths read, and what its `set:<key>` actions write.
    public static System.Text.Json.Nodes.JsonObject CurrentSettings(Plugin p) => p.Processes.Values.FirstOrDefault()?.Settings ?? Settings(p, null);
    public static void SetSetting(Plugin p, string key, System.Text.Json.Nodes.JsonNode? value)
    {
        if (p.Processes.Values.FirstOrDefault() is { } proc) proc.Set(key, value);
        else ShellConfig.SetSetting(p.Id, p.Id, key, value);
    }

    // --- hot reload ------------------------------------------------------------------------------
    // One watcher per root (three handles, not one per plugin), plus shell.json. Changes are
    // collected per plugin and acted on 300 ms after the last one (an editor's save is several
    // events; a git pull is hundreds).
    static readonly List<FileSystemWatcher> watchers = [];
    static readonly Dictionary<string, HashSet<string>> pending = new(StringComparer.OrdinalIgnoreCase);
    static DispatcherQueueTimer? debounce;

    static void Watch()
    {
        debounce = ui!.CreateTimer();
        debounce.Interval = TimeSpan.FromMilliseconds(300);
        debounce.IsRepeating = false;
        debounce.Tick += (_, _) => Flush();
        foreach (var (root, _) in Roots)
        {
            try
            {
                Directory.CreateDirectory(root); // account/shared only; the repo's exists
                var w = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                    InternalBufferSize = 64 * 1024,
                };
                FileSystemEventHandler on = (_, e) => Note(root, e.FullPath);
                w.Changed += on; w.Created += on; w.Deleted += on;
                w.Renamed += (_, e) => { Note(root, e.OldFullPath); Note(root, e.FullPath); };
                w.Error += (_, _) => Note(root, "*"); // overflow: rescan everything
                w.EnableRaisingEvents = true;
                watchers.Add(w);
            }
            catch (Exception ex) { App.Log(ex); }
        }
        try
        {
            var dir = Path.GetDirectoryName(ShellConfig.File)!;
            Directory.CreateDirectory(dir);
            var w = new FileSystemWatcher(dir, "shell.json") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName };
            FileSystemEventHandler on = (_, _) => Note(dir, "*config");
            w.Changed += on; w.Created += on; w.Renamed += (_, _) => Note(dir, "*config");
            w.EnableRaisingEvents = true;
            watchers.Add(w);
        }
        catch (Exception ex) { App.Log(ex); }
    }

    // Off the UI thread (watcher threads): record, then (re)start the timer on the UI thread.
    static void Note(string root, string path)
    {
        string id, rel;
        if (path is "*" or "*config") { id = path; rel = path; }
        else
        {
            var parts = Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar);
            if (parts.Length == 0 || parts.Contains(".git") || parts[0].StartsWith('.')) return;
            id = parts[0];
            rel = string.Join('/', parts.Skip(1));
        }
        lock (pending) { if (!pending.TryGetValue(id, out var set)) pending[id] = set = []; set.Add(rel); }
        ui?.TryEnqueue(() => { debounce!.Stop(); debounce.Start(); });
    }

    static void Flush()
    {
        Dictionary<string, HashSet<string>> now;
        lock (pending) { now = new(pending, StringComparer.OrdinalIgnoreCase); pending.Clear(); }
        bool rescan = now.ContainsKey("*") || now.ContainsKey("*config");
        foreach (var (folder, files) in now)
        {
            if (folder.StartsWith('*')) continue;
            var p = plugins.Values.FirstOrDefault(x => Path.GetFileName(x.Dir).Equals(folder, StringComparison.OrdinalIgnoreCase));
            if (p == null || files.Contains("") || files.Contains("manifest.json")) { rescan = true; continue; } // new, removed, or re-registered
            foreach (var f in files.Where(f => f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))) { p.StalePanels.Add(f); PanelHost.ReloadIfOpen(p, f); }
            if (files.Any(f => IsCode(f))) foreach (var proc in p.Processes.Values) proc.Restart("a file changed");
            if (files.Any(f => f is "menu.txt" or "items.txt" || f.StartsWith(p.Manifest.Omawin?.Commands ?? "commands"))) Fragments.Invalidate();
        }
        if (rescan) Rescan(); else WriteStatus();
    }

    static bool IsCode(string f) => Path.GetExtension(f).ToLowerInvariant() is ".ps1" or ".psm1" or ".ts" or ".js" or ".mjs" or ".py" or ".exe" or ".sh" or ".dll" or ".json" && f != "manifest.json";

    // For `omawin plugin list` and `omawin profile ram`: who's on, which pid, how much memory.
    public static void WriteStatus()
    {
        var rows = plugins.Values.Select(p =>
        {
            var proc = p.Processes.Values.FirstOrDefault();
            return new PluginStatus(p.Id, p.Origin.ToString(), p.Enabled, p.Manifest.Version, proc?.State.ToString() ?? (p.Errors.Count > 0 ? "invalid" : "idle"),
                proc?.Pid ?? 0, p.Processes.Values.Sum(x => x.MemoryBytes()), [.. p.Errors]);
        }).ToList();
        try
        {
            var file = Path.Combine(MenuModel.StateDir, "plugins", "status.json");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(rows, PluginJson.Default.ListPluginStatus));
            File.Move(file + ".tmp", file, overwrite: true);
        }
        catch (Exception ex) { App.Log(ex); }
    }
}
```

Status is rewritten on every start, stop and crash (`PluginProcess` calls `PluginRegistry.WriteStatus()` through the UI queue). `omawin profile ram` (`bin/profile-ram.ps1`) reads `status.json` and attributes each pid's tree (the job's process list, §3.2) to the plugin, against a budget per plugin (its `memoryMb`).

### 2.5 Secrets

`secret` settings are stored with Windows' Credential Manager under `omawin:plugin:<id>:<key>` (generic credential, per user, DPAPI): `CredReadW` / `CredWriteW` / `CredDeleteW` through `LibraryImport` (same pattern as §3.2). The settings page writes them; `shell.json` never holds them; `init` carries them to the plugin's stdin (never its command line or environment).

## 3. Plugin processes (4.2)

### 3.1 The protocol: `menu-app/Plugins/Protocol.cs`

Every message from [plugins.md](../plugins.md), plus the provider and admin ones (§7, §9.6). Inbound lines are parsed with `JsonNode` and a switch on `type` (no reflection, no serializer); outbound are written with `Utf8JsonWriter`.

**Plugin → shell (stdout)**

| `type` | Fields | Meaning |
|---|---|---|
| *(none)* | `text`, `alt`, `tooltip`, `class` (string or list), `percentage` | Waybar's bar line |
| `state` | `data`, `merge?` | panel state (`merge: true` merges top-level keys) |
| `notify` | `title`, `body?`, `icon?`, `open?` (https), `urgency?` | a Windows toast |
| `open-panel` / `close-panel` | `name?` | show / hide a panel or overlay |
| `run` | `command?` (argv) or `open?` (https or file) | start something (argv: through `omawin.exe` names only, see below) |
| `log` | `level`, `message` | to the plugin's log |
| `set` | `key`, `value` | persist one setting (written into `shell.json`'s placement; `secret` to Credential Manager) |
| `results` | `id`, `items[]`, `done` | launcher-provider answer |
| `found` | `id`, `items[]`, `done` | finder-source answer |
| `calendars` / `events` | `items[]` (+ `from`, `to`) | calendar-provider data |
| `reply` | `req`, `ok`, `message?`, `data?` | answer to an action that carried `req` |
| `admin` | `verb`, `args[]`, `why` | ask for an admin verb (first-party, §9.6) |
| `slot` | `width` | the QML host's widget wants this much bar (§10.3) |

**Shell → plugin (stdin)**: `init`, `click`, `wheel`, `panel`, `action`, `settings`, `theme`, `event`, `query`, `cancel`, `find`, `range`, `admin-result`, `slot-rect`, `shutdown`.

```csharp
using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SystemMenu.Plugins;

public abstract record PluginMessage;
public sealed record BarLine(string? Text, string? Alt, string? Tooltip, string[] Classes, double? Percentage) : PluginMessage;
public sealed record StateMessage(JsonNode Data, bool Merge) : PluginMessage;
public sealed record NotifyMessage(string Title, string? Body, string? Icon, string? Open, string Urgency) : PluginMessage;
public sealed record PanelRequest(bool Open, string? Name) : PluginMessage;
public sealed record RunMessage(string[]? Command, string? Open) : PluginMessage;
public sealed record LogMessage(string Level, string Message) : PluginMessage;
public sealed record SetMessage(string Key, JsonNode? Value) : PluginMessage;
public sealed record ResultsMessage(int Id, JsonArray Items, bool Done) : PluginMessage;
public sealed record FoundMessage(int Id, JsonArray Items, bool Done) : PluginMessage;
public sealed record CalendarMessage(string Type, JsonArray Items, string? From, string? To) : PluginMessage;
public sealed record ReplyMessage(int Req, bool Ok, string? Message, JsonNode? Data) : PluginMessage;
public sealed record AdminRequest(string Verb, string[] Args, string Why) : PluginMessage;
public sealed record SlotMessage(double Width) : PluginMessage;
public sealed record NotJson(string Line) : PluginMessage; // to the log; or, for interval scripts, plain text = the bar's text

public static class Protocol
{
    // One stdout line. Never throws: a bad line is logged, the bar keeps its last state.
    public static PluginMessage Parse(string line)
    {
        line = line.Trim();
        if (line.Length == 0 || line[0] != '{') return new NotJson(line);
        JsonNode? n;
        try { n = JsonNode.Parse(line); } catch { return new NotJson(line); }
        if (n is not JsonObject o) return new NotJson(line);
        string? S(string k) => o[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : o[k]?.ToJsonString();
        bool B(string k) => o[k] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
        int I(string k) => o[k] is JsonValue v && v.TryGetValue<int>(out var i) ? i : 0;
        string[] L(string k) => o[k] switch
        {
            JsonArray a => a.Select(x => x?.ToString() ?? "").ToArray(),
            JsonValue v when v.TryGetValue<string>(out var s) => s.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            _ => [],
        };
        JsonArray A(string k) => o[k] as JsonArray ?? [];
        switch (S("type"))
        {
            case null:
                double? pct = o["percentage"] is JsonValue pv && pv.TryGetValue<double>(out var d) ? d : null;
                return new BarLine(S("text"), S("alt"), S("tooltip"), L("class"), pct);
            case "state": return new StateMessage(o["data"]?.DeepClone() ?? new JsonObject(), B("merge"));
            case "notify": return new NotifyMessage(S("title") ?? "", S("body"), S("icon"), S("open"), S("urgency") ?? "normal");
            case "open-panel": return new PanelRequest(true, S("name"));
            case "close-panel": return new PanelRequest(false, S("name"));
            case "run": return new RunMessage(o["command"] is JsonArray ? L("command") : null, S("open"));
            case "log": return new LogMessage(S("level") ?? "info", S("message") ?? "");
            case "set": return new SetMessage(S("key") ?? "", o["value"]?.DeepClone());
            case "results": return new ResultsMessage(I("id"), (JsonArray)A("items").DeepClone(), B("done"));
            case "found": return new FoundMessage(I("id"), (JsonArray)A("items").DeepClone(), B("done"));
            case "calendars" or "events": return new CalendarMessage(S("type")!, (JsonArray)A("items").DeepClone(), S("from"), S("to"));
            case "reply": return new ReplyMessage(I("req"), B("ok"), S("message"), o["data"]?.DeepClone());
            case "admin": return new AdminRequest(S("verb") ?? "", L("args"), S("why") ?? "");
            case "slot": return new SlotMessage(o["width"] is JsonValue w && w.TryGetValue<double>(out var wd) ? wd : 0);
            default: return new NotJson(line); // a newer plugin's message: logged, ignored
        }
    }

    // --- shell -> plugin ---------------------------------------------------------------------------
    static string Write(Action<Utf8JsonWriter> body)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        { w.WriteStartObject(); body(w); w.WriteEndObject(); }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
    static void Node(Utf8JsonWriter w, string name, JsonNode? n) { w.WritePropertyName(name); if (n == null) w.WriteNullValue(); else n.WriteTo(w); }

    public static string Init(Plugin p, string instance, JsonObject settings, string dataDir, string pluginDir) => Write(w =>
    {
        w.WriteString("type", "init");
        Node(w, "settings", settings);
        w.WriteString("theme", MenuModel.CurrentThemeName());
        Node(w, "palette", ThemeResources.PaletteJson());          // so a plugin can draw in the theme without reading files
        w.WriteString("omawinVersion", App.OmawinVersion.ToString());
        w.WriteString("id", p.Id);
        w.WriteString("instance", instance);
        w.WriteString("pluginDir", pluginDir);                     // a Linux path for side: linux
        w.WriteString("dataDir", dataDir);
        w.WriteString("side", p.Manifest.Omawin?.Side ?? "windows");
    });
    public static string Click(string button) => Write(w => { w.WriteString("type", "click"); w.WriteString("button", button); });
    public static string Wheel(int delta) => Write(w => { w.WriteString("type", "wheel"); w.WriteNumber("delta", delta); });
    public static string Panel(bool open, string name) => Write(w => { w.WriteString("type", "panel"); w.WriteBoolean("open", open); w.WriteString("name", name); });
    public static string Action(string name, JsonNode args) => Write(w => { w.WriteString("type", "action"); w.WriteString("name", name); Node(w, "args", args); });
    public static string Settings(JsonObject s) => Write(w => { w.WriteString("type", "settings"); Node(w, "settings", s); });
    public static string Theme(string name) => Write(w => { w.WriteString("type", "theme"); w.WriteString("name", name); Node(w, "palette", ThemeResources.PaletteJson()); });
    public static string Event(string source, string name, JsonNode? data) => Write(w => { w.WriteString("type", "event"); w.WriteString("source", source); w.WriteString("name", name); Node(w, "data", data); });
    public static string Query(int id, string text) => Write(w => { w.WriteString("type", "query"); w.WriteNumber("id", id); w.WriteString("text", text); });
    public static string Cancel(int id) => Write(w => { w.WriteString("type", "cancel"); w.WriteNumber("id", id); });
    public static string Find(int id, string query, int limit, JsonObject filters) => Write(w => { w.WriteString("type", "find"); w.WriteNumber("id", id); w.WriteString("query", query); w.WriteNumber("limit", limit); Node(w, "filters", filters); });
    public static string Range(DateOnly from, DateOnly to) => Write(w => { w.WriteString("type", "range"); w.WriteString("from", from.ToString("yyyy-MM-dd")); w.WriteString("to", to.ToString("yyyy-MM-dd")); });
    public static string AdminResult(string verb, bool ok, string message) => Write(w => { w.WriteString("type", "admin-result"); w.WriteString("verb", verb); w.WriteBoolean("ok", ok); w.WriteString("message", message); });
    public static string SlotRect(int x, int y, int width, int height, double scale) => Write(w => { w.WriteString("type", "slot-rect"); w.WriteNumber("x", x); w.WriteNumber("y", y); w.WriteNumber("width", width); w.WriteNumber("height", height); w.WriteNumber("scale", scale); });
    public static string Shutdown() => Write(w => w.WriteString("type", "shutdown"));
}
```

*(check)* `JsonNode.Parse`, `JsonNode.WriteTo` and `JsonValue.TryGetValue<T>` under `PublishAot` with reflection-based serialization off: they're documented as trim-safe since .NET 8, but confirm there's no IL2026/IL3050 warning in the AOT build.

`run` with `command`: only `omawin`/`omawin.exe` as argv[0] is started (through `MenuModel.Omawin(args[1..])`); anything else is logged and refused, so a plugin can't use the shell to start programs outside its own Job Object. `open` goes through `Opener.Open` (https or an existing file).

### 3.2 Job Objects: `menu-app/Plugins/JobObject.cs`

One job per plugin instance: kill on close (the plugin dies with the shell, and with `Stop()`), a memory cap for the whole tree, below-normal priority, a low CPU weight.

```csharp
using System.Runtime.InteropServices;

namespace SystemMenu.Plugins;

public sealed unsafe partial class JobObject : IDisposable
{
    const int JobObjectBasicProcessIdList = 3, JobObjectExtendedLimitInformation = 9, JobObjectCpuRateControlInformation = 15;
    const uint KillOnJobClose = 0x2000, JobMemory = 0x200, PriorityClass = 0x20, DieOnUnhandledException = 0x400;
    const uint BelowNormalPriorityClass = 0x4000;
    const uint CpuRateEnable = 0x1, CpuRateWeightBased = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    struct BasicLimit
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass, SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)] struct IoCounters { public ulong R, W, O, RB, WB, OB; }
    [StructLayout(LayoutKind.Sequential)]
    struct ExtendedLimit
    {
        public BasicLimit Basic;
        public IoCounters Io;
        public nuint ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }
    [StructLayout(LayoutKind.Sequential)] struct CpuRate { public uint ControlFlags; public uint Weight; } // union: CpuRate | Weight | MinRate+MaxRate

    [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateJobObject(nint attributes, string? name);
    [LibraryImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(nint job, int infoClass, void* info, uint length);
    [LibraryImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryInformationJobObject(nint job, int infoClass, void* info, uint length, out uint returned);
    [LibraryImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(nint job, nint process);
    [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    nint handle;

    public JobObject(int memoryMb)
    {
        handle = CreateJobObject(0, null);
        if (handle == 0) throw new System.ComponentModel.Win32Exception();
        var limit = new ExtendedLimit
        {
            Basic = new BasicLimit { LimitFlags = KillOnJobClose | JobMemory | PriorityClass | DieOnUnhandledException, PriorityClass = BelowNormalPriorityClass },
            JobMemoryLimit = (nuint)memoryMb * 1024 * 1024,
        };
        if (!SetInformationJobObject(handle, JobObjectExtendedLimitInformation, &limit, (uint)sizeof(ExtendedLimit)))
            throw new System.ComponentModel.Win32Exception();
        // Weight 1-9 (5 is normal): plugins lose to everything you're doing. Not fatal if refused.
        var cpu = new CpuRate { ControlFlags = CpuRateEnable | CpuRateWeightBased, Weight = 3 };
        SetInformationJobObject(handle, JobObjectCpuRateControlInformation, &cpu, (uint)sizeof(CpuRate));
    }

    public void Add(System.Diagnostics.Process p)
    {
        if (!AssignProcessToJobObject(handle, p.Handle)) throw new System.ComponentModel.Win32Exception();
    }

    // Every process in the job (the plugin and anything it started): for profile ram and memory.
    public int[] ProcessIds()
    {
        const int max = 64;
        var buffer = stackalloc nuint[2 + max]; // JOBOBJECT_BASIC_PROCESS_ID_LIST: two DWORDs (padded to pointer size?) then ULONG_PTRs (check layout)
        var raw = (uint*)buffer;
        if (!QueryInformationJobObject(handle, JobObjectBasicProcessIdList, buffer, (uint)(sizeof(nuint) * (2 + max)), out _)) return [];
        uint count = raw[1];
        var ids = new int[Math.Min(count, (uint)max)];
        var list = (nuint*)(raw + 2);
        for (int i = 0; i < ids.Length; i++) ids[i] = (int)list[i];
        return ids;
    }

    public void Dispose() { if (handle != 0) { CloseHandle(handle); handle = 0; } } // kills the tree (KillOnJobClose)
}
```

Two things to settle on the PC:

- **The start race.** `Process.Start` then `AssignProcessToJobObject` leaves a moment where the child could start a grandchild outside the job. Fine for v1 (runners don't spawn in their first millisecond). The exact fix is `CreateProcessW` with `PROC_THREAD_ATTRIBUTE_JOB_LIST` in a `STARTUPINFOEX` (Windows 10+) *(check)*, wrapped in a small `JobProcess.Start(psi, job)` that returns pipes; do it when the shared host (§3.6) lands, since that spawns runspaces' children too.
- **uiAccess.** GlazeWM starts the menu app, which inherits GlazeWM's `uiAccess` (the comment on `--quit` in `App.xaml.cs`). Plugin processes inherit the menu app's token, so they'd get `uiAccess` too (UIPI bypass: sending input to elevated windows). Start plugins with a token without it: `OpenProcessToken` → `DuplicateTokenEx` → `SetTokenInformation(TokenUIAccess, 0)` → `CreateProcessAsUserW` *(check: whether clearing `TokenUIAccess` needs `SeTcbPrivilege`; if it does, start plugins through a broker that GlazeWM didn't start, e.g. a one-shot `explorer.exe`-parented launcher, or the per-user GlazeWM build without uiAccess from 2.9 makes this moot)*. Until settled, `PluginProcess` refuses to start when `IsUiAccess()` and `OMAWIN_PLUGINS_UIACCESS_OK` isn't set, logging why.

### 3.3 `menu-app/Plugins/PluginLog.cs`

```csharp
namespace SystemMenu.Plugins;

// %LOCALAPPDATA%\launcher\plugins\<id>\plugin.log, rotated at 1 MB into plugin.log.1 (one old copy).
// Also keeps the last 20 stderr lines in memory for the failed widget's tooltip.
public sealed class PluginLog
{
    static readonly Dictionary<string, PluginLog> logs = new(StringComparer.OrdinalIgnoreCase);
    public static PluginLog For(string id) { lock (logs) return logs.TryGetValue(id, out var l) ? l : logs[id] = new PluginLog(id); }

    readonly string file;
    readonly Queue<string> tail = new();
    PluginLog(string id) => file = Path.Combine(MenuModel.StateDir, "plugins", id, "plugin.log");

    public void Write(string level, string text)
    {
        lock (this)
        {
            if (level is "stderr" or "error") { tail.Enqueue(text); while (tail.Count > 20) tail.Dequeue(); }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                var info = new FileInfo(file);
                if (info.Exists && info.Length > 1_000_000) File.Move(file, file + ".1", overwrite: true);
                File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level,-6} {text}\n");
            }
            catch (IOException) { }
        }
    }
    public string Tail() { lock (this) return string.Join('\n', tail); }
}
```

### 3.4 `menu-app/Plugins/PluginProcess.cs`

```csharp
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.UI.Dispatching;

namespace SystemMenu.Plugins;

public enum ProcessState { Starting, Running, Waiting, Failed, Stopped }

// One plugin instance's process: started per its manifest, in a Job Object, talking JSON lines.
// Long-running by default; `interval` runs it Waybar-style. Crashes restart after 1 s, 5 s,
// 30 s, then it shows as failed. Messages are handed to the UI thread, coalesced: only the
// newest bar line and state per frame.
public sealed class PluginProcess
{
    static readonly TimeSpan[] Backoff = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)];

    public readonly Plugin Plugin;
    public readonly string Instance;
    JsonObject settings;
    readonly PluginLog log;
    readonly DispatcherQueue ui = DispatcherQueue.GetForCurrentThread();
    Process? process;
    JobObject? job;
    StreamWriter? stdin;
    int crashes;
    DateTime startedAt;
    bool stopping;
    DispatcherQueueTimer? timer; // restart backoff, or the interval

    public ProcessState State { get; private set; } = ProcessState.Stopped;
    public int Pid => process is { HasExited: false } p ? p.Id : 0;
    public BarLine? LastBar { get; private set; }
    public JsonNode? LastState { get; private set; }
    public event Action<PluginMessage>? Message; // UI thread: widgets, panels, providers subscribe
    public event Action? StateChanged;

    PluginProcess(Plugin p, string instance, JsonObject s) { Plugin = p; Instance = instance; settings = s; log = PluginLog.For(p.Id); }

    // A .ps1 goes to the shared host when one runs (§3.6); everything else gets its own process.
    public static PluginProcess For(Plugin p, string instance, JsonObject settings) => new(p, instance, settings);

    bool Interval => Plugin.Manifest.Omawin?.Interval is > 0;

    public void Start()
    {
        stopping = false;
        if (Interval) { ScheduleInterval(first: true); return; }
        Launch(keepStdin: true);
    }

    // --- what to run -----------------------------------------------------------------------------
    // (file, argv) for the manifest's command. No shell parsing, ever.
    public static (string File, List<string> Args) Resolve(Plugin p)
    {
        var o = p.Manifest.Omawin!;
        var cmd = o.Command!;
        var args = new List<string>();
        string first = cmd[0], file;
        string Abs(string rel) => Path.GetFullPath(Path.Combine(p.Dir, rel));
        string ext = Path.GetExtension(first).ToLowerInvariant();

        if (o.Side == "linux")
        {
            // In the user's distro (bin/defaults.txt `linux`), in the plugin's folder. wsl.exe
            // translates the Windows --cd itself; the command and its arguments run as written.
            file = "wsl.exe";
            args.AddRange(["-d", Defaults.LinuxDistro(), "--cd", p.Dir, "--"]);
            args.AddRange(cmd);
            return (file, args);
        }
        switch (first, ext)
        {
            case ("bun", _): file = Tools.Bun(); args.AddRange(cmd.Skip(1)); break;
            case ("pwsh", _): file = Tools.Pwsh(); args.AddRange(cmd.Skip(1)); break;
            case ("uv", _): file = Tools.Uv(); args.AddRange(cmd.Skip(1)); break;
            case (_, ".ps1"): file = Tools.Pwsh(); args.AddRange(["-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", Abs(first)]); args.AddRange(cmd.Skip(1)); break;
            case (_, ".ts" or ".js" or ".mjs"): file = Tools.Bun(); args.Add(Abs(first)); args.AddRange(cmd.Skip(1)); break;
            case (_, ".py"): file = Tools.Uv(); args.AddRange(["run", "--no-project", Abs(first)]); args.AddRange(cmd.Skip(1)); break; // (check) uv flags for a lone script with PEP 723 deps
            case (_, ".exe"): file = Abs(first); args.AddRange(cmd.Skip(1)); break;
            default: throw new InvalidOperationException($"don't know how to run '{first}'");
        }
        return (file, args);
    }

    ProcessStartInfo Info()
    {
        var (file, args) = Resolve(Plugin);
        var psi = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Plugin.Dir,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        // The basics only: no tokens or the shell's own variables leak into plugins (plugins.md, Safety).
        var keep = new[] { "SystemRoot", "windir", "ComSpec", "PATH", "PATHEXT", "TEMP", "TMP", "USERPROFILE", "HOMEDRIVE", "HOMEPATH",
            "LOCALAPPDATA", "APPDATA", "ProgramData", "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432", "CommonProgramFiles",
            "OS", "PROCESSOR_ARCHITECTURE", "NUMBER_OF_PROCESSORS", "USERNAME", "USERDOMAIN", "COMPUTERNAME", "SystemDrive", "PSModulePath", "BUN_INSTALL" };
        var env = psi.Environment;
        var saved = keep.Where(k => env.ContainsKey(k)).ToDictionary(k => k, k => env[k]);
        env.Clear();
        foreach (var (k, v) in saved) env[k] = v;
        env["OMAWIN_PLUGIN_ID"] = Plugin.Id;
        env["OMAWIN_PLUGIN_DIR"] = Plugin.Dir;
        env["OMAWIN_DATA_DIR"] = Plugin.DataDir;
        env["OMAWIN_SDK"] = Path.Combine(MenuModel.Repo, "plugins", "sdk");
        env["OMAWIN_VERSION"] = App.OmawinVersion.ToString();
        env["NO_COLOR"] = "1";
        if (Plugin.Manifest.Omawin!.Side == "linux") env["WSLENV"] = "OMAWIN_PLUGIN_ID:OMAWIN_PLUGIN_DIR/p:OMAWIN_DATA_DIR/p:OMAWIN_VERSION:NO_COLOR";
        return psi;
    }

    // --- running ---------------------------------------------------------------------------------
    void Launch(bool keepStdin)
    {
        try
        {
            Directory.CreateDirectory(Plugin.DataDir);
            job?.Dispose();
            job = new JobObject(Plugin.Manifest.Omawin!.MemoryMb);
            var psi = Info();
            log.Write("start", $"{psi.FileName} {string.Join(' ', psi.ArgumentList)}");
            process = Process.Start(psi) ?? throw new InvalidOperationException("didn't start");
            job.Add(process);
            startedAt = DateTime.UtcNow;
            SetState(ProcessState.Running);
            stdin = process.StandardInput;
            stdin.AutoFlush = true;
            var p = process;
            new Thread(() => ReadOut(p)) { IsBackground = true, Name = "plugin " + Plugin.Id }.Start();
            new Thread(() => ReadErr(p)) { IsBackground = true, Name = "plugin err " + Plugin.Id }.Start();
            p.EnableRaisingEvents = true;
            p.Exited += (_, _) => ui.TryEnqueue(() => Exited(p));
            Send(Protocol.Init(Plugin, Instance, settings, DataDirFor(), PluginDirFor()));
            if (!keepStdin) { try { stdin.Close(); } catch (IOException) { } stdin = null; }
        }
        catch (Exception ex)
        {
            log.Write("error", ex.Message);
            Crashed();
        }
    }

    string DataDirFor() => Plugin.Manifest.Omawin!.Side == "linux" ? Wsl.ToLinuxPath(Plugin.DataDir) : Plugin.DataDir;   // /mnt/c/Users/...
    string PluginDirFor() => Plugin.Manifest.Omawin!.Side == "linux" ? Wsl.ToLinuxPath(Plugin.Dir) : Plugin.Dir;

    public void Send(string line)
    {
        var w = stdin;
        if (w == null) return;
        try { lock (w) w.WriteLine(line); }
        catch (IOException) { } // gone; Exited will follow
        catch (ObjectDisposedException) { }
    }

    // Coalescing: the reader thread keeps only the newest bar line and state, and asks the UI
    // thread once; everything else is queued in order.
    BarLine? pendingBar;
    StateMessage? pendingState;
    int flushQueued;
    readonly Queue<PluginMessage> pendingOther = new();
    string? lastLine; // interval mode: the last line printed

    void ReadOut(Process p)
    {
        try
        {
            string? line;
            while ((line = p.StandardOutput.ReadLine()) != null)
            {
                lastLine = line;
                if (Interval) continue; // read at exit
                Deliver(Protocol.Parse(line));
            }
        }
        catch (Exception ex) { log.Write("error", "stdout: " + ex.Message); }
    }

    void ReadErr(Process p)
    {
        try { string? line; while ((line = p.StandardError.ReadLine()) != null) log.Write("stderr", line); }
        catch (Exception) { }
    }

    void Deliver(PluginMessage m)
    {
        switch (m)
        {
            case NotJson n: log.Write("stdout", n.Line); return;
            case LogMessage l: log.Write(l.Level, l.Message); return;
            case BarLine b: Interlocked.Exchange(ref pendingBar, b); break;
            case StateMessage s when !s.Merge: Interlocked.Exchange(ref pendingState, s); break;
            default: lock (pendingOther) pendingOther.Enqueue(m); break;
        }
        if (Interlocked.Exchange(ref flushQueued, 1) == 0) ui.TryEnqueue(Flush);
    }

    void Flush()
    {
        flushQueued = 0;
        if (Interlocked.Exchange(ref pendingBar, null) is { } bar) { LastBar = bar; Message?.Invoke(bar); }
        if (Interlocked.Exchange(ref pendingState, null) is { } st) { LastState = st.Data; Message?.Invoke(st); }
        while (true)
        {
            PluginMessage? m;
            lock (pendingOther) if (!pendingOther.TryDequeue(out m)) break;
            Handle(m);
        }
    }

    // Messages the process itself handles; the rest go to subscribers (widget, panel, providers).
    void Handle(PluginMessage m)
    {
        switch (m)
        {
            case StateMessage { Merge: true } s:
                if (LastState is JsonObject cur && s.Data is JsonObject add) foreach (var (k, v) in add) cur[k] = v?.DeepClone();
                else LastState = s.Data;
                Message?.Invoke(new StateMessage(LastState!, false));
                break;
            case NotifyMessage n: Notifier.Show(n.Title, n.Body, n.Open is { } o && o.StartsWith("https://") ? o : null, icon: n.Icon, app: Plugin.Manifest.Name); break;
            case PanelRequest r when r.Open: PanelHost.Open(Plugin.Id, r.Name ?? "", overlay: false); break;
            case PanelRequest r: PanelHost.Close(Plugin.Id, r.Name ?? ""); break;
            case RunMessage { Open: { } url }: Opener.Open(url); break;
            case RunMessage { Command: [var exe, .. var rest] } when exe is "omawin" or "omawin.exe": MenuModel.Omawin(rest); break;
            case RunMessage r: log.Write("warn", $"run refused: only omawin commands and https links ({string.Join(' ', r.Command ?? [])})"); break;
            case SetMessage s: Store(s); break;
            case AdminRequest a: AdminQueue.Request(Plugin, a, reply => Send(reply)); break;
            default: Message?.Invoke(m); break;
        }
    }

    public JsonObject Settings => settings;
    public void Set(string key, JsonNode? value) => Store(new SetMessage(key, value));

    void Store(SetMessage s)
    {
        var field = Plugin.Manifest.BarWidget?.Schema.FirstOrDefault(f => f.Key == s.Key);
        if (field == null) { log.Write("warn", $"set: '{s.Key}' isn't in the schema"); return; }
        if (field.Type == "secret") Secrets.Write($"omawin:plugin:{Plugin.Id}:{s.Key}", s.Value?.ToString() ?? "");
        else ShellConfig.SetSetting(Plugin.Id, Instance, s.Key, s.Value); // the shell.json watcher sends `settings` back
        settings[s.Key] = s.Value?.DeepClone();
    }

    public void UpdateSettings(JsonObject s) { settings = s; if (Interval) RunNowIfIdle(); else Send(Protocol.Settings(s)); }

    void Exited(Process p)
    {
        if (p != process) return;
        int code = 0;
        try { code = p.ExitCode; } catch { }
        if (Interval) { IntervalDone(code); return; }
        stdin = null;
        if (stopping) { SetState(ProcessState.Stopped); return; }
        log.Write("exit", $"exited with {code}");
        Crashed();
    }

    void Crashed()
    {
        if (DateTime.UtcNow - startedAt > TimeSpan.FromMinutes(1)) crashes = 0; // it ran well for a while: start counting again
        if (crashes >= Backoff.Length) { SetState(ProcessState.Failed); return; }
        var wait = Backoff[crashes++];
        SetState(ProcessState.Waiting);
        After(wait, () => { if (!stopping) Launch(keepStdin: true); });
    }

    public void Restart(string why)
    {
        log.Write("reload", why);
        crashes = 0;
        Kill();
        stopping = false;
        if (Interval) RunNowIfIdle(); else Launch(keepStdin: true);
    }

    public void Stop()
    {
        stopping = true;
        timer?.Stop();
        Send(Protocol.Shutdown());
        // A second to save its state (Omadoro writes its file), then the job takes the tree.
        var p = process;
        After(TimeSpan.FromSeconds(1), () => { if (p == process) Kill(); });
    }

    void Kill()
    {
        try { stdin?.Close(); } catch { }
        stdin = null;
        job?.Dispose(); job = null; // KillOnJobClose: the whole tree
        process?.Dispose(); process = null;
    }

    // --- interval mode (Waybar's exec + interval) ---------------------------------------------------
    void ScheduleInterval(bool first)
    {
        var every = TimeSpan.FromSeconds(Plugin.Manifest.Omawin!.Interval!.Value);
        // Staggered: a stable offset per id, so plugins with the same interval don't all fire together.
        var offset = first ? TimeSpan.FromMilliseconds((uint)StableHash(Plugin.Id + Instance) % (uint)Math.Min(every.TotalMilliseconds, 10_000)) : every;
        After(offset, () => { if (!stopping) RunOnce(); });
    }

    void RunOnce()
    {
        lastLine = null;
        Launch(keepStdin: false); // init is written, then stdin closed: one-shot scripts never wait on it
        var p = process;
        // A script that hangs past its interval (or 30 s) is ended.
        After(TimeSpan.FromSeconds(Math.Min(Plugin.Manifest.Omawin!.Interval!.Value, 30)), () => { if (p == process && p is { HasExited: false }) { log.Write("warn", "took too long"); Kill(); IntervalDone(-1); } });
    }

    void IntervalDone(int code)
    {
        if (lastLine is { } line)
        {
            var m = Protocol.Parse(line);
            // Waybar's non-JSON mode: a plain line is the text.
            Deliver(m is NotJson n ? new BarLine(n.Line, null, null, [], null) : m);
            crashes = 0;
            SetState(ProcessState.Running);
        }
        else if (code != 0) { log.Write("exit", $"exited with {code}, nothing printed"); if (++crashes > Backoff.Length) { SetState(ProcessState.Failed); return; } }
        Kill();
        if (!stopping) ScheduleInterval(first: false);
    }

    void RunNowIfIdle() { if (process == null) { timer?.Stop(); RunOnce(); } }

    // Waybar's exec-on-event: a click on an interval widget runs its click command, then refreshes.
    public void ClickedInterval() => RunNowIfIdle();

    // --- helpers ---------------------------------------------------------------------------------
    void After(TimeSpan wait, System.Action then)
    {
        timer?.Stop();
        timer = ui.CreateTimer();
        timer.Interval = wait;
        timer.IsRepeating = false;
        timer.Tick += (_, _) => then();
        timer.Start();
    }

    void SetState(ProcessState s) { State = s; StateChanged?.Invoke(); PluginRegistry.WriteStatus(); }

    public string FailureText => $"{Plugin.Manifest.Name} stopped working.\n{log.Tail()}\n\nomawin plugin log {Plugin.Id}";

    public long MemoryBytes()
    {
        long sum = 0;
        foreach (var pid in job?.ProcessIds() ?? [])
            try { using var p = Process.GetProcessById(pid); sum += p.PrivateMemorySize64; } catch { }
        return sum;
    }

    static int StableHash(string s) { unchecked { int h = 23; foreach (var c in s) h = h * 31 + c; return h & 0x7fffffff; } }
}
```

Helpers referenced: `Tools.Bun()/Pwsh()/Uv()` (look up `bun.exe` on PATH, then `%USERPROFILE%\.bun\bin\bun.exe`; `pwsh.exe`; `uv.exe`, then `%USERPROFILE%\.local\bin\uv.exe`; cache), `Defaults.LinuxDistro()` (read `bin/defaults.txt`'s `linux` line as `omawin.cs` does), `Wsl.ToLinuxPath` (`C:\Users\x\…` → `/mnt/c/Users/x/…`; a `\\wsl.localhost\<distro>\…` path → its Linux path).

**Linux side, the data folder.** For `side: linux`, `dataDir` is the Windows data folder seen from Linux (`/mnt/c/…`). It's slow-ish over 9P but small; a plugin that writes a lot should use `$XDG_STATE_HOME` in Linux instead (that's what Omarchy plugins already do).

### 3.5 Event relay: `menu-app/Plugins/EventRelay.cs`

`Bar/GlazeWm.cs` gets one event, raised in `Handle` for every `event_subscription` message before the refresh logic:

```csharp
public event Action<string, JsonElement>? Event; // (eventType, data), on the socket's thread

// in Handle, kind == "event_subscription":
if (Event != null && root.TryGetProperty("data", out var ev) && ev.TryGetProperty("eventType", out var type))
    Event(type.GetString() ?? "", ev.Clone());
```

```csharp
namespace SystemMenu.Plugins;

// The shell's own events, relayed to the plugins that asked (omawin.events), so they don't each
// open a websocket to GlazeWM or poll the battery.
public static class EventRelay
{
    public static void Start(Microsoft.UI.Dispatching.DispatcherQueue ui)
    {
        Bar.Wm.Event += (type, data) =>
        {
            var name = "glazewm." + type;
            var json = System.Text.Json.Nodes.JsonNode.Parse(data.GetRawText());
            ui.TryEnqueue(() => PluginRegistry.Broadcast(Protocol.Event("glazewm", type, json), p => p.Manifest.Omawin!.Events.Contains(name)));
        };
        Windows.System.Power.PowerManager.BatteryStatusChanged += (_, _) => Power(ui);
        Windows.System.Power.PowerManager.RemainingChargePercentChanged += (_, _) => Power(ui);
        Windows.System.Power.PowerManager.PowerSupplyStatusChanged += (_, _) => Power(ui);
        // audio: Audio.cs's default-device / volume callback (IMMNotificationClient) raises one (check: there's no event there yet)
    }

    // Bar.ApplyTheme calls this after a theme switch.
    public static void Theme(string name) => PluginRegistry.Broadcast(Protocol.Theme(name), p => p.Manifest.Omawin!.Events.Contains("theme") || p.Processes.Count > 0);

    static void Power(Microsoft.UI.Dispatching.DispatcherQueue ui) => ui.TryEnqueue(() =>
    {
        var data = new System.Text.Json.Nodes.JsonObject
        {
            ["percent"] = Windows.System.Power.PowerManager.RemainingChargePercent,
            ["status"] = Windows.System.Power.PowerManager.BatteryStatus.ToString(),
            ["supply"] = Windows.System.Power.PowerManager.PowerSupplyStatus.ToString(),
        };
        PluginRegistry.Broadcast(Protocol.Event("power", "changed", data), p => p.Manifest.Omawin!.Events.Contains("power"));
    });
}
```

Every running plugin gets `theme` (it's cheap and every panel-drawing plugin needs it); the rest only when listed.

### 3.6 One host for PowerShell plugins (v3, sketch)

A `pwsh` costs ~60 MB and ~0.3 s; three PowerShell widgets shouldn't cost three. One host process, one runspace per plugin, the same JSON lines framed with the plugin's key.

The plugin side can't share `[Console]::In`, so PowerShell plugins read and write through two functions. The SDK file defines them over stdin/stdout for a plugin run on its own; the host defines them over its channels. A plugin written with them runs either way; one that uses the console directly sets `"sharedHost": false`.

`plugins/sdk/ps/Omawin.Plugin.ps1` (dot-sourced by plugins; the `plugin new` template does it):

```powershell
# omawin's plugin protocol for PowerShell plugins (docs/plugins.md): read the shell's messages,
# write bar lines and state. Inside the shared host these are already defined; on its own, a
# plugin talks over stdin / stdout.
if (-not (Get-Command Read-OmawinMessage -ErrorAction SilentlyContinue)) {
    [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
    $script:OmawinIn = [Console]::In
    function Read-OmawinMessage([int]$TimeoutMs = -1) {
        # -1: wait; else $null after the timeout (a widget's tick)
        if ($TimeoutMs -lt 0) { $line = $script:OmawinIn.ReadLine() }
        else {
            if (-not $script:OmawinPending) { $script:OmawinPending = $script:OmawinIn.ReadLineAsync() }
            if (-not $script:OmawinPending.Wait($TimeoutMs)) { return $null }
            $line = $script:OmawinPending.Result; $script:OmawinPending = $null
        }
        if ($null -eq $line) { exit 0 } # the shell closed stdin: stop
        $line | ConvertFrom-Json -AsHashtable
    }
    function Write-OmawinMessage([Parameter(Mandatory)]$Message) {
        [Console]::Out.WriteLine(($Message | ConvertTo-Json -Compress -Depth 20)); [Console]::Out.Flush()
    }
}
function Write-OmawinBar([string]$Text, [string]$Tooltip, [string]$Class, [double]$Percentage = -1) {
    $m = @{ text = $Text }
    if ($Tooltip) { $m.tooltip = $Tooltip }; if ($Class) { $m.class = $Class }; if ($Percentage -ge 0) { $m.percentage = $Percentage }
    Write-OmawinMessage $m
}
function Write-OmawinState([hashtable]$Data) { Write-OmawinMessage @{ type = 'state'; data = $Data } }
function Send-OmawinNotify([string]$Title, [string]$Body) { Write-OmawinMessage @{ type = 'notify'; title = $Title; body = $Body } }
```

`plugins/sdk/ps/pwsh-host.ps1` (started by `PwshHost.cs` in its own Job Object, memory cap = the sum of its plugins' caps):

```powershell
# The shared PowerShell host: one process, one runspace per plugin. stdin lines are
# {"to":"<key>","line":"<a protocol line>"} or {"start":"<key>","script":"<path>","dir":"<dir>"} / {"stop":"<key>"};
# stdout lines are {"from":"<key>","line":"<protocol line>"} or {"from":"<key>","exit":<code>}.
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$out = [Console]::Out
$lock = [object]::new()
$plugins = @{}   # key -> @{ PowerShell; Inbox; Handle }

function Emit($obj) { $text = $obj | ConvertTo-Json -Compress -Depth 5; [Threading.Monitor]::Enter($lock); try { $out.WriteLine($text); $out.Flush() } finally { [Threading.Monitor]::Exit($lock) } }

# Defined in each runspace: the plugin's Read/Write-OmawinMessage over this host's channels.
$prelude = {
    param($Inbox, $Emit, $Key)
    function Read-OmawinMessage([int]$TimeoutMs = -1) {
        $line = $null
        if (-not $Inbox.TryTake([ref]$line, $TimeoutMs)) { return $null }
        if ($null -eq $line) { exit 0 }
        $line | ConvertFrom-Json -AsHashtable
    }
    function Write-OmawinMessage($Message) { & $Emit @{ from = $Key; line = ($Message | ConvertTo-Json -Compress -Depth 20) } }
}

while ($null -ne ($raw = [Console]::In.ReadLine())) {
    $m = $raw | ConvertFrom-Json -AsHashtable
    if ($m.start) {
        $inbox = [Collections.Concurrent.BlockingCollection[string]]::new()
        $ps = [PowerShell]::Create()   # its own runspace
        $emit = { param($o) Emit $o }.GetNewClosure()
        $null = $ps.AddScript($prelude).AddArgument($inbox).AddArgument($emit).AddArgument($m.start)
        $null = $ps.AddScript("Set-Location -LiteralPath '$($m.dir -replace "'", "''")'; . '$($m.script -replace "'", "''")'")
        $handle = $ps.BeginInvoke()
        $plugins[$m.start] = @{ PowerShell = $ps; Inbox = $inbox; Handle = $handle }
        # (check) prelude functions must be visible to the script: AddScript(...,useLocalScope:$false), or one script block that dot-sources both
    }
    elseif ($m.to -and $plugins[$m.to]) { $plugins[$m.to].Inbox.Add($m.line) }
    elseif ($m.stop -and $plugins[$m.stop]) {
        $p = $plugins[$m.stop]; $p.Inbox.Add($null); $p.Inbox.CompleteAdding()
        if (-not $p.Handle.AsyncWaitHandle.WaitOne(1000)) { $p.PowerShell.Stop() }
        $p.PowerShell.Dispose(); $plugins.Remove($m.stop); Emit @{ from = $m.stop; exit = 0 }
    }
}
```

`PwshHost.cs` (C#): one `Process` like `PluginProcess.Launch`, a `Dictionary<string, PluginProcess>` keyed by `"<id>#<instance>"`; `PluginProcess` gains a transport seam (`ITransport { void Send(string); event Action<string> Line; event Action<int> Exit; }`) with two implementations: its own process (above) and a channel in the host (`Send` → `{"to":key,"line":…}`; host lines with `from:key` → `Line`). A crash of the host restarts every PowerShell plugin with the usual backoff. Memory attribution for `profile ram`: the host's memory split evenly (good enough; runspaces don't report their own).

## 4. The bar's command widget (4.2)

### 4.1 `BarWidget.Create` falls back to plugins (`menu-app/Bar/Widgets.cs`)

```csharp
public static BarWidget? Create(Plugins.WidgetPlacement place) => place.Id switch
{
    "menu" => new MenuWidget(),
    "workspaces" => new WorkspacesWidget(),
    // ... the built-ins as today ...
    "tray" => new TrayWidget(),
    _ => Plugins.PluginRegistry.Get(place.Id) is { Usable: true } p && p.Manifest.Has("bar-widget") ? new CommandWidget(p, place) : null,
};
```

A plugin with `"replaces": "clock"` is placed where `clock` was by `plugin enable` (it edits `shell.json`), not by `Create`.

### 4.2 `menu-app/Bar/CommandWidget.cs`

Styles: `text` (glyph + text, Waybar's look), `meter` (text with a thin bar under it, `percentage`), `ring` (a 14 px arc that empties, then the text), `slot` (empty space of the plugin's requested width, for the QML host, §10.3).

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SystemMenu.Plugins;

namespace SystemMenu;

// A plugin's widget: draws the newest line its process printed ({"text","tooltip","class",
// "percentage"}) in the theme. Clicks and the wheel go to the process (or open its panel).
// While the process waits to restart or has failed, a warning glyph with the last errors.
sealed class CommandWidget : BarWidget
{
    readonly Plugin plugin;
    readonly WidgetPlacement place;
    readonly WidgetInfo info;
    readonly Cell cell;
    readonly Grid ring = new() { Width = 14, Height = 14, Margin = new Thickness(0, 0, 2, 0), Visibility = Visibility.Collapsed };
    readonly Path track = new() { StrokeThickness = 2 }, arc = new() { StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    readonly Rectangle meterTrack = new() { Height = 2, RadiusX = 1, RadiusY = 1 }, meterFill = new() { Height = 2, RadiusX = 1, RadiusY = 1, HorizontalAlignment = HorizontalAlignment.Left };
    readonly Grid meter = new() { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(4, 0, 4, 3), Visibility = Visibility.Collapsed };
    PluginProcess? process;
    BarLine? line;
    double percent;
    public override FrameworkElement Element => cell;

    public CommandWidget(Plugin p, WidgetPlacement placement)
    {
        plugin = p;
        place = placement;
        info = p.Manifest.Omawin?.Widget ?? new WidgetInfo();
        cell = new Cell(p.Manifest.BarWidget?.DisplayName ?? p.Manifest.Name) { Padding = new Thickness(5, 0, 5, 0) };
        if (info.Style == "ring")
        {
            ring.Children.Add(track);
            ring.Children.Add(arc);
            cell.Row.Children.Insert(0, ring); // before the glyph
        }
        if (info.Style == "meter")
        {
            meter.Children.Add(meterTrack);
            meter.Children.Add(meterFill);
            cell.Children.Add(meter);
            meter.SizeChanged += (_, e) => meterFill.Width = e.NewSize.Width * percent / 100;
        }
        cell.Click = () => Clicked(info.Click, "left");
        cell.RightClick = () => Clicked(info.RightClick, "right");
        cell.Wheel = up => process?.Send(Protocol.Wheel(up));
        cell.Unloaded += (_, _) => Detach();
    }

    public override void Attach(BarWindow bar)
    {
        base.Attach(bar);
        process = PluginRegistry.Acquire(plugin, place.Instance, place);
        if (process == null) { Fail("No command to run"); return; }
        process.Message += OnMessage;
        process.StateChanged += Show;
        line = process.LastBar; // another monitor's bar already started it
        Show();
    }

    void Detach()
    {
        if (process == null) return;
        process.Message -= OnMessage;
        process.StateChanged -= Show;
        PluginRegistry.Release(plugin, place.Instance);
        process = null;
    }

    void OnMessage(PluginMessage m)
    {
        if (m is BarLine b) { line = b; Show(); }
        else if (m is SlotMessage s) { cell.Width = s.Width; ReportSlot(); }
    }

    // "panel" | "action <name>" | "run <omawin command…>" | null: the default (panel if there is
    // one, else a click message to the process).
    void Clicked(string? what, string button)
    {
        var o = plugin.Manifest.Omawin!;
        what ??= button == "left" && (o.Panel != null) ? "panel" : null;
        if (what == "panel") { PanelHost.Toggle(plugin.Id, "", anchor: cell); return; }
        if (what?.StartsWith("action ") == true) { process?.Send(Protocol.Action(what[7..], new System.Text.Json.Nodes.JsonObject { ["instance"] = place.Instance })); return; }
        if (what?.StartsWith("run ") == true) { MenuModel.Omawin(what[4..].Split(' ', StringSplitOptions.RemoveEmptyEntries)); process?.ClickedInterval(); return; }
        process?.Send(Protocol.Click(button));
    }

    void Show()
    {
        if (process?.State == ProcessState.Failed || process?.State == ProcessState.Waiting && line == null)
        { Fail(process!.FailureText); return; }
        cell.Red = false;
        if (line == null) { cell.SetText(null); cell.SetIcon(null); return; }
        // Waybar's text often starts with a Nerd Font glyph and a space: the glyph goes in the icon slot.
        var text = line.Text ?? "";
        if (text.Length > 1 && char.IsSurrogate(text[0]) || text.Length > 0 && text[0] >= '' && text[0] <= '')
        {
            int n = char.IsSurrogate(text[0]) ? 2 : 1;
            cell.SetIcon(text[..n]);
            cell.SetText(text[n..].TrimStart());
        }
        else { cell.SetIcon(null); cell.SetText(text); }
        cell.Tip = line.Tooltip ?? plugin.Manifest.Name;
        cell.Visibility = string.IsNullOrEmpty(text) && info.Style == "text" ? Visibility.Collapsed : Visibility.Visible;
        Paint(Theme);
    }

    void Fail(string tip)
    {
        cell.SetIcon(Glyph(0xF0026)); // nf-md-alert
        cell.SetText(null);
        cell.Tip = tip;
        cell.Red = true;
        cell.Visibility = Visibility.Visible;
    }

    // class -> a theme colour: the manifest's map first ("work": "accent"), then Waybar's usual
    // names, then a theme colour name itself.
    string ColorFor(Theme t)
    {
        foreach (var c in line?.Classes ?? [])
        {
            var name = info.Classes.GetValueOrDefault(c, c);
            var hex = name switch
            {
                "critical" or "urgent" or "red" => t.Red,
                "warning" or "yellow" => t.Yellow,
                "good" or "green" or "charging" => t.Green,
                "muted" or "comment" or "disabled" => t.Comment,
                "accent" or "blue" or "active" => t.Accent,
                "cyan" => t.Cyan,
                "purple" or "magenta" => t.Purple,
                _ => null,
            };
            if (hex != null) return hex;
        }
        return t.Foreground;
    }

    public override void Paint(Theme theme)
    {
        base.Paint(theme);
        cell.Paint(theme);
        if (line == null) return;
        var color = new SolidColorBrush(Bar.Hex(ColorFor(theme)));
        cell.Icon.Foreground = cell.Text.Foreground = color;
        var faint = Bar.Hex(theme.Foreground); faint.A = 40;
        double pct = percent = Math.Clamp(line.Percentage ?? 0, 0, 100);
        if (info.Style == "ring")
        {
            ring.Visibility = line.Percentage == null ? Visibility.Collapsed : Visibility.Visible;
            track.Stroke = new SolidColorBrush(faint);
            track.Data = Arc(7, 7, 6, 0, 359.99);
            arc.Stroke = color;
            arc.Data = pct <= 0 ? null : Arc(7, 7, 6, 0, pct * 3.6);
        }
        else if (info.Style == "meter")
        {
            meter.Visibility = line.Percentage == null ? Visibility.Collapsed : Visibility.Visible;
            meterTrack.Fill = new SolidColorBrush(faint);
            meterFill.Fill = color;
            meterFill.Width = meter.ActualWidth * pct / 100;
        }
    }

    // A circular arc from `from` to `to` degrees, clockwise from 12 o'clock.
    static Geometry Arc(double cx, double cy, double r, double from, double to)
    {
        Windows.Foundation.Point At(double deg) { var a = (deg - 90) * Math.PI / 180; return new(cx + r * Math.Cos(a), cy + r * Math.Sin(a)); }
        var figure = new PathFigure { StartPoint = At(from), IsClosed = false };
        figure.Segments.Add(new ArcSegment { Point = At(to), Size = new Windows.Foundation.Size(r, r), IsLargeArc = to - from > 180, SweepDirection = SweepDirection.Clockwise });
        var g = new PathGeometry();
        g.Figures.Add(figure);
        return g;
    }

    // For the QML host's slot: where this cell is on the screen, in pixels.
    void ReportSlot()
    {
        if (info.Style != "slot" || process == null || Bar == null) return;
        var p = cell.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, 0));
        double scale = cell.XamlRoot?.RasterizationScale ?? 1;
        process.Send(Protocol.SlotRect(Bar.Screen.X + (int)(p.X * scale), Bar.Screen.Y + (int)(p.Y * scale), (int)(cell.ActualWidth * scale), (int)(cell.ActualHeight * scale), scale));
    }
}
```

`Cell` needs a small change: expose its inner row (`public StackPanel Row => row;`) so a widget can put the ring before the glyph.


## 5. Panels: inert XAML (4.3)

### 5.0 Prove it first (a day, on the PC)

Before building the rest, a throwaway branch: `App.xaml` gets the `Omawin.Plugins` probe (below), `MainWindow` loads `tests/panel-probe.xaml` with `XamlReader.Load`, walks it, sets a `TextBlock` through `Bind.Text`, and fires an `Action.Click`, **in the AOT build** (`build.ps1 -Aot`). Three outcomes:

1. Works: build §5 as written.
2. `XamlReader.Load` works but can't resolve `using:Omawin.Plugins` attached properties: fall back to `Tag` conventions on the same tree walk (`Tag="bind:Text=city; action:Click=refresh"`), parsed by `PanelBinding` instead of reading attached properties. Plugins' XAML changes, the protocol doesn't.
3. `XamlReader.Load` itself fails under AOT: the declarative JSON panel from plugins.md (rows, toggles, sliders, lists) drawn by the shell.

### 5.1 Theme resources in `Application.Resources` (`menu-app/App.xaml`)

Declared in compiled XAML (no `Style` built in code, so nothing depends on `typeof` → `TypeName` under AOT); `ThemeResources.Apply` only changes the brushes' colours, so open panels follow a theme switch without reloading.

```xml
<Application
    x:Class="SystemMenu.App"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:omawin="using:Omawin.Plugins"
    RequestedTheme="Dark">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls" />
            </ResourceDictionary.MergedDictionaries>
            <!-- The theme, for plugin panels (docs/plugins.md): colours filled in by ThemeResources.Apply. -->
            <SolidColorBrush x:Key="OmawinBackground" Color="#1a1b26"/>
            <SolidColorBrush x:Key="OmawinSurface" Color="#1f2335"/>
            <SolidColorBrush x:Key="OmawinSelection" Color="#292e42"/>
            <SolidColorBrush x:Key="OmawinBorder" Color="#3b4261"/>
            <SolidColorBrush x:Key="OmawinForeground" Color="#c0caf5"/>
            <SolidColorBrush x:Key="OmawinMuted" Color="#a9b1d6"/>
            <SolidColorBrush x:Key="OmawinComment" Color="#565f89"/>
            <SolidColorBrush x:Key="OmawinAccent" Color="#7aa2f7"/>
            <SolidColorBrush x:Key="OmawinPurple" Color="#bb9af7"/>
            <SolidColorBrush x:Key="OmawinCyan" Color="#7dcfff"/>
            <SolidColorBrush x:Key="OmawinRed" Color="#f7768e"/>
            <SolidColorBrush x:Key="OmawinGreen" Color="#9ece6a"/>
            <SolidColorBrush x:Key="OmawinYellow" Color="#e0af68"/>
            <FontFamily x:Key="OmawinFont">Segoe UI Variable Text</FontFamily>
            <FontFamily x:Key="OmawinMonoFont">JetBrainsMono Nerd Font, JetBrainsMono NF, Cascadia Mono, Consolas</FontFamily>
            <CornerRadius x:Key="OmawinCornerRadius">8</CornerRadius>
            <Style x:Key="OmawinTitle" TargetType="TextBlock">
                <Setter Property="FontSize" Value="18"/>
                <Setter Property="FontWeight" Value="SemiBold"/>
                <Setter Property="Foreground" Value="{StaticResource OmawinForeground}"/>
            </Style>
            <Style x:Key="OmawinBody" TargetType="TextBlock">
                <Setter Property="FontSize" Value="13"/>
                <Setter Property="Foreground" Value="{StaticResource OmawinForeground}"/>
                <Setter Property="TextWrapping" Value="Wrap"/>
            </Style>
            <Style x:Key="OmawinCaption" TargetType="TextBlock">
                <Setter Property="FontSize" Value="12"/>
                <Setter Property="Foreground" Value="{StaticResource OmawinMuted}"/>
            </Style>
            <Style x:Key="OmawinIconButton" TargetType="Button">
                <Setter Property="FontFamily" Value="{StaticResource OmawinMonoFont}"/>
                <Setter Property="FontSize" Value="20"/>
                <Setter Property="Width" Value="48"/>
                <Setter Property="Height" Value="48"/>
                <Setter Property="CornerRadius" Value="24"/>
                <Setter Property="Foreground" Value="{StaticResource OmawinForeground}"/>
                <Setter Property="Background" Value="{StaticResource OmawinSelection}"/>
            </Style>
            <!-- Never shown: puts Omawin.Plugins.Bind / Action into the generated XAML type
                 info, so XamlReader.Load can resolve them in loose panels (check: grep
                 obj\...\XamlTypeInfo.g.cs for Omawin.Plugins.Bind after a build). -->
            <DataTemplate x:Key="OmawinPluginProbe">
                <Button omawin:Bind.Text="a" omawin:Bind.Items="a" omawin:Bind.Value="a" omawin:Bind.IsOn="a" omawin:Bind.IsChecked="a"
                        omawin:Bind.Visible="a" omawin:Bind.Source="a" omawin:Bind.Content="a" omawin:Bind.Enabled="a" omawin:Bind.Foreground="a"
                        omawin:Bind.Maximum="a" omawin:Bind.Selected="a" omawin:Bind.Tooltip="a"
                        omawin:Action.Click="a" omawin:Action.Toggled="a" omawin:Action.Changed="a" omawin:Action.TextChanged="a"
                        omawin:Action.Submitted="a" omawin:Action.Invoked="a" omawin:Action.Arg="a" omawin:Action.Debounce="0"/>
            </DataTemplate>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

`menu-app/Plugins/ThemeResources.cs`:

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace SystemMenu.Plugins;

// The current theme as Application.Resources brushes with fixed keys (OmawinAccent, ...): what
// plugin panels use, so none hard-codes a colour (AGENTS.md's theme rule). Called at start and
// from Bar.ApplyTheme on every switch. Our own pages can move to the same keys.
public static class ThemeResources
{
    static readonly (string Key, Func<Theme, string> Pick)[] Colors =
    [
        ("OmawinBackground", t => t.Background), ("OmawinSurface", t => t.Surface), ("OmawinSelection", t => t.Selection),
        ("OmawinBorder", t => t.Border), ("OmawinForeground", t => t.Foreground), ("OmawinMuted", t => t.Muted),
        ("OmawinComment", t => t.Comment), ("OmawinAccent", t => t.Accent), ("OmawinPurple", t => t.Purple),
        ("OmawinCyan", t => t.Cyan), ("OmawinRed", t => t.Red), ("OmawinGreen", t => t.Green), ("OmawinYellow", t => t.Yellow),
    ];
    static Theme current = new();

    public static void Apply(Theme theme)
    {
        current = theme;
        var res = Application.Current.Resources;
        foreach (var (key, pick) in Colors)
        {
            var color = Bar.Hex(pick(theme));
            // Change the brush, don't replace it: everything already holding it repaints.
            if (res.TryGetValue(key, out var o) && o is SolidColorBrush b) b.Color = color;
            else res[key] = new SolidColorBrush(color);
            res[key + "Color"] = color;
        }
        PanelHost.Repaint(theme); // RequestedTheme (light/dark) on open panels
    }

    // A class name or theme colour name ("accent", "critical") -> its brush, for Bind.Foreground.
    public static Brush? Brush(string name)
    {
        var key = name.ToLowerInvariant() switch
        {
            "critical" or "urgent" or "red" => "OmawinRed", "warning" or "yellow" => "OmawinYellow", "good" or "green" => "OmawinGreen",
            "muted" => "OmawinMuted", "comment" => "OmawinComment", "accent" or "blue" => "OmawinAccent", "cyan" => "OmawinCyan",
            "purple" or "magenta" => "OmawinPurple", "foreground" => "OmawinForeground", _ => null,
        };
        return key != null && Application.Current.Resources.TryGetValue(key, out var b) ? b as Brush : null;
    }

    // The palette for `init` / `theme` messages: { "accent": "#7aa2f7", ... }.
    public static System.Text.Json.Nodes.JsonObject PaletteJson()
    {
        var o = new System.Text.Json.Nodes.JsonObject { ["name"] = current.Name, ["mode"] = current.Mode };
        foreach (var (key, pick) in Colors) o[key["Omawin".Length..].ToLowerInvariant()] = pick(current);
        return o;
    }
}
```

### 5.2 The attached properties: `menu-app/Plugins/Bind.cs`

The namespace is `Omawin.Plugins` (what panels write: `xmlns:omawin="using:Omawin.Plugins"`), not `SystemMenu.Plugins`. Each property only *holds* a path or an action name; nothing happens on set. The binder (§5.3) reads them after loading.

```csharp
using Microsoft.UI.Xaml;

namespace Omawin.Plugins;

// omawin:Bind.<Property>="path.into.state": the shell sets that property from the plugin's
// {"type":"state"} data. Paths: "now.temp", "days[0].name", "settings.units"; a leading "!"
// negates (for Visible / Enabled). Inside an item template, paths are relative to the item;
// "$index" is its position, "$root.x" reaches the whole state.
public sealed partial class Bind : DependencyObject
{
    static DependencyProperty Make(string name) => DependencyProperty.RegisterAttached(name, typeof(string), typeof(Bind), new PropertyMetadata(null));

    public static readonly DependencyProperty TextProperty = Make("Text");
    public static readonly DependencyProperty ContentProperty = Make("Content");
    public static readonly DependencyProperty ItemsProperty = Make("Items");
    public static readonly DependencyProperty IsOnProperty = Make("IsOn");
    public static readonly DependencyProperty IsCheckedProperty = Make("IsChecked");
    public static readonly DependencyProperty ValueProperty = Make("Value");
    public static readonly DependencyProperty MaximumProperty = Make("Maximum");
    public static readonly DependencyProperty VisibleProperty = Make("Visible");
    public static readonly DependencyProperty EnabledProperty = Make("Enabled");
    public static readonly DependencyProperty SourceProperty = Make("Source");
    public static readonly DependencyProperty ForegroundProperty = Make("Foreground");
    public static readonly DependencyProperty SelectedProperty = Make("Selected");
    public static readonly DependencyProperty TooltipProperty = Make("Tooltip");

    public static string GetText(DependencyObject o) => (string)o.GetValue(TextProperty);
    public static void SetText(DependencyObject o, string v) => o.SetValue(TextProperty, v);
    public static string GetContent(DependencyObject o) => (string)o.GetValue(ContentProperty);
    public static void SetContent(DependencyObject o, string v) => o.SetValue(ContentProperty, v);
    public static string GetItems(DependencyObject o) => (string)o.GetValue(ItemsProperty);
    public static void SetItems(DependencyObject o, string v) => o.SetValue(ItemsProperty, v);
    public static string GetIsOn(DependencyObject o) => (string)o.GetValue(IsOnProperty);
    public static void SetIsOn(DependencyObject o, string v) => o.SetValue(IsOnProperty, v);
    public static string GetIsChecked(DependencyObject o) => (string)o.GetValue(IsCheckedProperty);
    public static void SetIsChecked(DependencyObject o, string v) => o.SetValue(IsCheckedProperty, v);
    public static string GetValue(DependencyObject o) => (string)o.GetValue(ValueProperty);
    public static void SetValue(DependencyObject o, string v) => o.SetValue(ValueProperty, v);
    public static string GetMaximum(DependencyObject o) => (string)o.GetValue(MaximumProperty);
    public static void SetMaximum(DependencyObject o, string v) => o.SetValue(MaximumProperty, v);
    public static string GetVisible(DependencyObject o) => (string)o.GetValue(VisibleProperty);
    public static void SetVisible(DependencyObject o, string v) => o.SetValue(VisibleProperty, v);
    public static string GetEnabled(DependencyObject o) => (string)o.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject o, string v) => o.SetValue(EnabledProperty, v);
    public static string GetSource(DependencyObject o) => (string)o.GetValue(SourceProperty);
    public static void SetSource(DependencyObject o, string v) => o.SetValue(SourceProperty, v);
    public static string GetForeground(DependencyObject o) => (string)o.GetValue(ForegroundProperty);
    public static void SetForeground(DependencyObject o, string v) => o.SetValue(ForegroundProperty, v);
    public static string GetSelected(DependencyObject o) => (string)o.GetValue(SelectedProperty);
    public static void SetSelected(DependencyObject o, string v) => o.SetValue(SelectedProperty, v);
    public static string GetTooltip(DependencyObject o) => (string)o.GetValue(TooltipProperty);
    public static void SetTooltip(DependencyObject o, string v) => o.SetValue(TooltipProperty, v);

    // For the binder: every bindable property and its name, in one place.
    public static readonly (string Name, DependencyProperty Property)[] All =
    [
        ("Text", TextProperty), ("Content", ContentProperty), ("Items", ItemsProperty), ("IsOn", IsOnProperty), ("IsChecked", IsCheckedProperty),
        ("Value", ValueProperty), ("Maximum", MaximumProperty), ("Visible", VisibleProperty), ("Enabled", EnabledProperty),
        ("Source", SourceProperty), ("Foreground", ForegroundProperty), ("Selected", SelectedProperty), ("Tooltip", TooltipProperty),
    ];
}

// omawin:Action.<Event>="name": the shell sends {"type":"action","name":…,"args":{…}} to the
// plugin. Arg: a path whose value goes with it (in an item template: that item's, e.g. "id").
// Debounce: milliseconds (TextChanged defaults to 250, Changed to 100).
public sealed partial class Action : DependencyObject
{
    static DependencyProperty Make(string name) => DependencyProperty.RegisterAttached(name, typeof(string), typeof(Action), new PropertyMetadata(null));

    public static readonly DependencyProperty ClickProperty = Make("Click");         // ButtonBase.Click; Tapped elsewhere
    public static readonly DependencyProperty ToggledProperty = Make("Toggled");     // ToggleSwitch, CheckBox, ToggleButton
    public static readonly DependencyProperty ChangedProperty = Make("Changed");     // Slider, NumberBox, ComboBox, ListView selection, RatingControl
    public static readonly DependencyProperty TextChangedProperty = Make("TextChanged"); // TextBox, AutoSuggestBox (user input only)
    public static readonly DependencyProperty SubmittedProperty = Make("Submitted"); // AutoSuggestBox.QuerySubmitted, Enter in a TextBox
    public static readonly DependencyProperty InvokedProperty = Make("Invoked");     // KeyboardAccelerator
    public static readonly DependencyProperty ArgProperty = Make("Arg");
    public static readonly DependencyProperty DebounceProperty = Make("Debounce");

    public static string GetClick(DependencyObject o) => (string)o.GetValue(ClickProperty);
    public static void SetClick(DependencyObject o, string v) => o.SetValue(ClickProperty, v);
    public static string GetToggled(DependencyObject o) => (string)o.GetValue(ToggledProperty);
    public static void SetToggled(DependencyObject o, string v) => o.SetValue(ToggledProperty, v);
    public static string GetChanged(DependencyObject o) => (string)o.GetValue(ChangedProperty);
    public static void SetChanged(DependencyObject o, string v) => o.SetValue(ChangedProperty, v);
    public static string GetTextChanged(DependencyObject o) => (string)o.GetValue(TextChangedProperty);
    public static void SetTextChanged(DependencyObject o, string v) => o.SetValue(TextChangedProperty, v);
    public static string GetSubmitted(DependencyObject o) => (string)o.GetValue(SubmittedProperty);
    public static void SetSubmitted(DependencyObject o, string v) => o.SetValue(SubmittedProperty, v);
    public static string GetInvoked(DependencyObject o) => (string)o.GetValue(InvokedProperty);
    public static void SetInvoked(DependencyObject o, string v) => o.SetValue(InvokedProperty, v);
    public static string GetArg(DependencyObject o) => (string)o.GetValue(ArgProperty);
    public static void SetArg(DependencyObject o, string v) => o.SetValue(ArgProperty, v);
    public static string GetDebounce(DependencyObject o) => (string)o.GetValue(DebounceProperty);
    public static void SetDebounce(DependencyObject o, string v) => o.SetValue(DebounceProperty, v);
}
```

Inside `namespace Omawin.Plugins`, `Action` shadows `System.Action`: write `System.Action` explicitly in that namespace. *(check)* the XAML compiler accepts a `sealed partial class : DependencyObject` with only static members as an attached-property owner (it does for UWP; the probe in `App.xaml` proves it for WinUI 3).

### 5.3 The binder: `menu-app/Plugins/PanelBinding.cs`

One walk after loading records every bound element and every action; each `state` line sets values directly. No reflection: children are found by type, values set by type.

```csharp
using System.Text.Json.Nodes;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using B = Omawin.Plugins.Bind;
using A = Omawin.Plugins.Action;

namespace SystemMenu.Plugins;

public sealed class PanelBinding
{
    readonly List<(DependencyObject Target, string Prop, string Path)> bound = [];
    readonly List<(ItemsControl Control, DataTemplate Template, string Path, List<(FrameworkElement El, PanelBinding Binding)> Made)> lists = [];
    readonly Action<string, JsonObject> send;
    readonly Plugin plugin;
    JsonNode? scope;  // the item (in a template) or the whole state
    JsonNode? root;   // the whole state
    int index = -1;
    bool applying;    // values set by Apply don't echo back as actions

    PanelBinding(Plugin p, Action<string, JsonObject> send) { plugin = p; this.send = send; }

    public static PanelBinding Collect(Plugin p, FrameworkElement top, Action<string, JsonObject> send)
    {
        var b = new PanelBinding(p, send);
        b.Walk(top);
        return b;
    }

    // --- the walk ----------------------------------------------------------------------------------
    void Walk(DependencyObject o)
    {
        foreach (var (name, dp) in B.All)
            if (o.GetValue(dp) is string path && path.Length > 0)
            {
                if (name == "Items" && o is ItemsControl ic and not ComboBox && ic.ItemTemplate is { } t)
                {
                    ic.ItemTemplate = null; // our elements are their own items
                    lists.Add((ic, t, path, []));
                }
                else bound.Add((o, name, path));
            }
        Wire(o);
        if (o is UIElement u) foreach (var ka in u.KeyboardAccelerators) Wire(ka);
        foreach (var child in Children(o)) Walk(child);
    }

    // Children by type: the logical content of the controls panels may use.
    static IEnumerable<DependencyObject> Children(DependencyObject o)
    {
        switch (o)
        {
            case Panel p: foreach (var c in p.Children) yield return c; break;           // StackPanel, Grid, Canvas, RelativePanel, VariableSizedWrapGrid
            case Border b when b.Child != null: yield return b.Child; break;
            case Viewbox v when v.Child != null: yield return v.Child; break;
            case UserControl uc when uc.Content != null: yield return uc.Content; break;
            case ItemsControl: break;                                                    // items are ours (Bind.Items) or literal: walk literal items
            case ContentControl cc when cc.Content is DependencyObject d: yield return d; break; // Button, ScrollViewer, Expander, ...
        }
        if (o is ItemsControl lit) foreach (var item in lit.Items) if (item is DependencyObject d) yield return d;
        if (o is Expander ex && ex.Header is DependencyObject h) yield return h;
        if (o is SplitView sv) { if (sv.Pane != null) yield return sv.Pane; if (sv.Content != null) yield return sv.Content; }
    }

    // --- actions -----------------------------------------------------------------------------------
    void Wire(DependencyObject o)
    {
        if (A.GetClick(o) is { Length: > 0 } click)
        {
            if (o is ButtonBase bb) bb.Click += (_, _) => Fire(click, o, null);
            else if (o is UIElement ue) ue.Tapped += (_, e) => { e.Handled = true; Fire(click, o, null); };
        }
        if (A.GetToggled(o) is { Length: > 0 } toggled)
        {
            if (o is ToggleSwitch ts) ts.Toggled += (_, _) => { if (!applying) Fire(toggled, o, ts.IsOn); };
            else if (o is ToggleButton tb) // CheckBox, ToggleButton, RadioButton
            {
                tb.Checked += (_, _) => { if (!applying) Fire(toggled, o, true); };
                tb.Unchecked += (_, _) => { if (!applying) Fire(toggled, o, false); };
            }
        }
        if (A.GetChanged(o) is { Length: > 0 } changed)
        {
            var later = new Debounce(o, 100);
            switch (o)
            {
                case RangeBase rb: rb.ValueChanged += (_, e) => { if (!applying) later.Run(() => Fire(changed, o, e.NewValue)); }; break;
                case NumberBox nb: nb.ValueChanged += (_, e) => { if (!applying) later.Run(() => Fire(changed, o, e.NewValue)); }; break;
                case Selector s: s.SelectionChanged += (_, _) => { if (!applying) Fire(changed, o, new JsonObject { ["index"] = s.SelectedIndex, ["value"] = s.SelectedItem?.ToString() }); }; break;
                case RatingControl r: r.ValueChanged += (_, _) => { if (!applying) Fire(changed, o, r.Value); }; break;
            }
        }
        if (A.GetTextChanged(o) is { Length: > 0 } typed)
        {
            var later = new Debounce(o, 250);
            if (o is AutoSuggestBox asb)
                asb.TextChanged += (_, e) => { if (e.Reason == AutoSuggestionBoxTextChangeReason.UserInput) later.Run(() => Fire(typed, o, asb.Text)); };
            else if (o is TextBox tx)
                tx.TextChanged += (_, _) => { if (!applying) later.Run(() => Fire(typed, o, tx.Text)); };
        }
        if (A.GetSubmitted(o) is { Length: > 0 } submitted)
        {
            if (o is AutoSuggestBox asb)
                asb.QuerySubmitted += (_, e) => Fire(submitted, o, new JsonObject { ["text"] = e.QueryText, ["chosen"] = e.ChosenSuggestion?.ToString() });
            else if (o is TextBox tx)
                tx.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { e.Handled = true; Fire(submitted, o, tx.Text); } };
        }
        if (A.GetInvoked(o) is { Length: > 0 } invoked && o is KeyboardAccelerator ka)
            ka.Invoked += (_, e) => { e.Handled = true; Fire(invoked, o, null); };
    }

    // The action, with the control's value and its Arg (resolved against this item, when in a template).
    void Fire(string name, DependencyObject from, JsonNode? value)
    {
        var args = new JsonObject();
        if (value != null) args["value"] = value;
        if (A.GetArg(from) is { Length: > 0 } argPath) args["arg"] = Resolve(argPath)?.DeepClone();
        if (index >= 0) args["index"] = index;
        // `plugin` actions from menu rows and panels share one name space; "set:<key>" is a
        // shortcut that stores a setting directly (a settings toggle needs no plugin code).
        if (name.StartsWith("set:")) { PluginRegistry.SetSetting(plugin, name[4..], value); return; }
        send(name, args);
    }

    // Waits until the control has been still for `ms`, then runs the newest action once.
    sealed class Debounce
    {
        readonly DispatcherQueueTimer timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        System.Action? last;
        public Debounce(DependencyObject o, int fallbackMs)
        {
            timer.Interval = TimeSpan.FromMilliseconds(int.TryParse(A.GetDebounce(o), out var ms) ? ms : fallbackMs);
            timer.IsRepeating = false;
            timer.Tick += (_, _) => last?.Invoke();
        }
        public void Run(System.Action then) { last = then; timer.Stop(); timer.Start(); }
    }

    // --- values ------------------------------------------------------------------------------------
    public void Apply(JsonNode? state) => Apply(state, state, -1);

    void Apply(JsonNode? itemOrState, JsonNode? whole, int i)
    {
        scope = itemOrState; root = whole; index = i;
        applying = true;
        try
        {
            foreach (var (target, prop, path) in bound) Set(target, prop, path);
            foreach (var list in lists) Fill(list.Control, list.Template, list.Path, list.Made);
        }
        finally { applying = false; }
    }

    JsonNode? Resolve(string path)
    {
        bool not = path.StartsWith('!');
        if (not) path = path[1..];
        JsonNode? n = path.StartsWith("$root.") ? root : scope;
        if (path.StartsWith("$root.")) path = path[6..];
        if (path == "$index") return index;
        if (path == "settings" || path.StartsWith("settings.")) { n = new JsonObject { ["settings"] = PluginRegistry.CurrentSettings(plugin) }; }
        if (path != ".")
            foreach (var part in path.Split('.'))
            {
                var name = part; int? at = null;
                var bracket = part.IndexOf('[');
                if (bracket > 0 && part.EndsWith(']') && int.TryParse(part[(bracket + 1)..^1], out var k)) { name = part[..bracket]; at = k; }
                n = n is JsonObject o ? o[name] : null;
                if (at is int idx) n = n is JsonArray a && idx < a.Count ? a[idx] : null;
                if (n == null) break;
            }
        return not ? !Truthy(n) : n;
    }

    static bool Truthy(JsonNode? n) => n switch
    {
        null => false,
        JsonArray a => a.Count > 0,
        JsonObject => true,
        JsonValue v when v.TryGetValue<bool>(out var b) => b,
        JsonValue v when v.TryGetValue<double>(out var d) => d != 0,
        JsonValue v when v.TryGetValue<string>(out var s) => s.Length > 0,
        _ => true,
    };
    static string Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : n?.ToJsonString() ?? "";
    static double Num(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var d) ? d : double.TryParse(Str(n), out var p) ? p : 0;

    void Set(DependencyObject t, string prop, string path)
    {
        var v = Resolve(path);
        switch (prop, t)
        {
            case ("Text", TextBlock tb): tb.Text = Str(v); break;
            case ("Text", AutoSuggestBox asb) when !HasFocus(asb): asb.Text = Str(v); break;  // never under the user's typing
            case ("Text", TextBox tx) when !HasFocus(tx): tx.Text = Str(v); break;
            case ("Text", ContentControl cc): cc.Content = Str(v); break;
            case ("Content", ContentControl cc): cc.Content = Str(v); break;
            case ("IsOn", ToggleSwitch ts): ts.IsOn = Truthy(v); break;
            case ("IsChecked", ToggleButton tb): tb.IsChecked = Truthy(v); break;
            case ("Value", RangeBase rb): rb.Value = Num(v); break;                    // Slider, ProgressBar
            case ("Value", ProgressRing pr): pr.IsIndeterminate = v == null; pr.Value = Num(v); break;
            case ("Value", NumberBox nb): nb.Value = Num(v); break;
            case ("Value", RatingControl rc): rc.Value = Num(v); break;
            case ("Maximum", RangeBase rb): rb.Maximum = Num(v); break;
            case ("Maximum", ProgressRing pr): pr.Maximum = Num(v); break;
            case ("Visible", UIElement u): u.Visibility = Truthy(v) ? Visibility.Visible : Visibility.Collapsed; break;
            case ("Enabled", Control c): c.IsEnabled = Truthy(v); break;
            case ("Foreground", Control c): if (ThemeResources.Brush(Str(v)) is { } b1) c.Foreground = b1; break;
            case ("Foreground", TextBlock tb): if (ThemeResources.Brush(Str(v)) is { } b2) tb.Foreground = b2; break;
            case ("Source", Image img): img.Source = Picture(Str(v)); break;
            case ("Selected", Selector s): s.SelectedIndex = v is JsonValue jv && jv.TryGetValue<int>(out var si) ? si : s.Items.IndexOf(Str(v)); break;
            case ("Tooltip", _): ToolTipService.SetToolTip(t, Str(v)); break;
            case ("Items", AutoSuggestBox asb): asb.ItemsSource = (v as JsonArray)?.Select(Str).ToList(); break;   // suggestions
            case ("Items", ComboBox cb): Strings(cb, v); break;
            case ("Items", ItemsControl ic): Strings(ic, v); break;                    // a list without a template: strings
        }
    }

    static void Strings(ItemsControl ic, JsonNode? v)
    {
        var items = (v as JsonArray)?.Select(Str).ToList() ?? [];
        if (ic.Items.Count == items.Count && ic.Items.Select(x => x?.ToString()).SequenceEqual(items)) return;
        ic.Items.Clear();
        foreach (var s in items) ic.Items.Add(s);
    }

    // Item templates: one element per item, made from the template once and kept; bound against
    // that item. Rows are reused by position (cheap; a keyed diff by "id" if lists get long).
    void Fill(ItemsControl ic, DataTemplate template, string path, List<(FrameworkElement El, PanelBinding Binding)> made)
    {
        var items = Resolve(path) as JsonArray;
        int n = items?.Count ?? 0;
        while (made.Count > n) { ic.Items.RemoveAt(made.Count - 1); made.RemoveAt(made.Count - 1); }
        while (made.Count < n)
        {
            var el = (FrameworkElement)template.LoadContent();
            var b = new PanelBinding(plugin, send);
            b.Walk(el);
            ic.Items.Add(el);
            made.Add((el, b));
        }
        for (int i = 0; i < n; i++) made[i].Binding.Apply(items![i], root, i);
    }

    static bool HasFocus(UIElement u) => u.XamlRoot != null && FocusManager.GetFocusedElement(u.XamlRoot) is DependencyObject f && (f == u || IsInside(f, u));
    static bool IsInside(DependencyObject f, DependencyObject u) { for (var p = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(f); p != null; p = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(p)) if (p == u) return true; return false; }

    // Images: https, or a file inside the plugin or its data folder. Nothing else.
    BitmapImage? Picture(string s)
    {
        if (s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return new BitmapImage(new Uri(s));
        foreach (var dir in new[] { plugin.Dir, plugin.DataDir })
            if (!Path.IsPathRooted(s) && ManifestCheck.Inside(dir, s) && File.Exists(Path.Combine(dir, s))) return new BitmapImage(new Uri(Path.Combine(dir, s)));
        if (Path.IsPathRooted(s) && (s.StartsWith(plugin.DataDir, StringComparison.OrdinalIgnoreCase) || s.StartsWith(plugin.Dir, StringComparison.OrdinalIgnoreCase)) && File.Exists(s)) return new BitmapImage(new Uri(s));
        return null;
    }
}
```

`PluginRegistry.CurrentSettings(plugin)` returns the merged settings the process has (§2.4 `Settings`); `PluginRegistry.SetSetting` writes through `ShellConfig.SetSetting` (or `Secrets.Write`) and sends `settings` to the process.

*(check)* `ItemsControl.Items.Add(FrameworkElement)` displays the element directly in a plain `ItemsControl`; in a `ListView` it's wrapped in a `ListViewItem` (fine, and selection works). With `ItemTemplate` nulled first, no template is applied to the element.

### 5.4 Validation: `menu-app/Plugins/PanelCheck.cs`

Before `XamlReader.Load`: a plain XML read that refuses anything outside the allowed vocabulary, with the reason in the plugin's log. `XamlReader` already rejects `x:Class` and event handlers; this also keeps out types that load content (`WebView2`, `Frame`, `MediaPlayerElement`), reflection bindings, and resource dictionaries pulled from elsewhere.

```csharp
using System.Xml;

namespace SystemMenu.Plugins;

public static class PanelCheck
{
    const string Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    const string Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    const string Ours = "using:Omawin.Plugins";
    static readonly HashSet<string> Elements =
    [
        // layout
        "StackPanel", "Grid", "RowDefinition", "ColumnDefinition", "Border", "Canvas", "RelativePanel", "ScrollViewer", "Viewbox", "VariableSizedWrapGrid", "SplitView", "Expander",
        // text and shapes
        "TextBlock", "RichTextBlock", "Paragraph", "Run", "Span", "Bold", "Italic", "LineBreak", "FontIcon", "SymbolIcon", "PathIcon", "Image", "Rectangle", "Ellipse", "Line", "Path", "PersonPicture", "InfoBar", "InfoBadge",
        // controls
        "Button", "HyperlinkButton", "RepeatButton", "ToggleButton", "DropDownButton", "SplitButton", "ToggleSwitch", "CheckBox", "RadioButton", "RadioButtons",
        "Slider", "ProgressBar", "ProgressRing", "RatingControl", "NumberBox", "TextBox", "PasswordBox", "AutoSuggestBox", "ComboBox", "ComboBoxItem",
        "ListView", "ListViewItem", "GridView", "ItemsControl", "ItemsRepeater", "CalendarView", "DatePicker", "TimePicker", "ColorPicker", "TeachingTip",
        "MenuFlyout", "MenuFlyoutItem", "MenuFlyoutSeparator", "Flyout", "ToolTip",
        // resources and input
        "DataTemplate", "KeyboardAccelerator", "SolidColorBrush", "LinearGradientBrush", "GradientStop", "Thickness", "CornerRadius", "Style", "Setter", "ResourceDictionary",
    ];

    public static List<string> Check(string xaml)
    {
        var problems = new List<string>();
        if (xaml.Length > 256 * 1024) { problems.Add("larger than 256 KB"); return problems; }
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true };
        try
        {
            using var r = XmlReader.Create(new StringReader(xaml), settings);
            while (r.Read())
            {
                if (r.NodeType != XmlNodeType.Element) continue;
                var name = r.LocalName;
                if (r.NamespaceURI == Presentation)
                {
                    var type = name.Contains('.') ? name[..name.IndexOf('.')] : name; // Grid.RowDefinitions: a property element of an allowed type
                    if (!Elements.Contains(type)) problems.Add($"line {((IXmlLineInfo)r).LineNumber}: <{name}> isn't allowed in a panel");
                }
                else if (r.NamespaceURI != Xaml) problems.Add($"line {((IXmlLineInfo)r).LineNumber}: namespace '{r.NamespaceURI}' isn't allowed");
                if (name == "ResourceDictionary" && r.GetAttribute("Source") != null) problems.Add("ResourceDictionary Source: panels can't load other files");
                if (r.MoveToFirstAttribute())
                    do
                    {
                        if (r.Prefix == "xmlns" || r.Name == "xmlns")
                        {
                            if (r.Value is not (Presentation or Xaml or Ours)) problems.Add($"namespace '{r.Value}' isn't allowed");
                        }
                        else if (r.NamespaceURI == Xaml && r.LocalName is "Class" or "Code" or "FieldModifier" or "ClassModifier") problems.Add($"x:{r.LocalName}: panels hold no code");
                        else if (r.Value.TrimStart().StartsWith("{Binding") || r.Value.TrimStart().StartsWith("{x:Bind")) problems.Add($"{r.Name}: use omawin:Bind, not {{Binding}} (no reflection in the shell)");
                        else if (r.LocalName == "Source" && name == "Image" && !r.Value.StartsWith("ms-appx:///")) problems.Add("Image Source: use omawin:Bind.Source (https or a file in the plugin)");
                        else if (r.LocalName == "NavigateUri" && !r.Value.StartsWith("https://")) problems.Add("NavigateUri: https only");
                    } while (r.MoveToNextAttribute());
            }
        }
        catch (XmlException ex) { problems.Add($"line {ex.LineNumber}: {ex.Message}"); }
        return problems;
    }
}
```

### 5.5 `menu-app/Plugins/PanelHost.cs`

```csharp
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace SystemMenu.Plugins;

// A plugin's panel (a drop-down under its widget) or overlay (a card centred on the screen):
// its loose XAML, loaded with XamlReader, wrapped in the theme's card, bound to the plugin's
// state. One window per open panel; hidden, not destroyed, on close (quick to show again).
public static class PanelHost
{
    sealed class Open_
    {
        public required Plugin Plugin; public required string Name; public required string File;
        public required Window Window; public required Border Card;
        public PanelBinding? Binding; public PluginProcess? Process; public bool Overlay; public bool Stale;
    }
    static readonly Dictionary<string, Open_> panels = [];
    static string Key(string id, string name) => id + "/" + name;

    public static void Toggle(string id, string name, FrameworkElement? anchor = null)
    {
        if (panels.TryGetValue(Key(id, name), out var p) && p.Window.Visible) Close(id, name);
        else Open(id, name, overlay: false, anchor);
    }

    public static void Open(string id, string name, bool overlay, FrameworkElement? anchor = null)
    {
        if (PluginRegistry.Get(id) is not { Usable: true } plugin) return;
        var o = plugin.Manifest.Omawin!;
        var info = string.IsNullOrEmpty(name)
            ? (o.Panel != null ? new PanelInfo { Name = "", File = o.Panel } : o.Panels.FirstOrDefault())
            : o.Panels.FirstOrDefault(x => x.Name == name);
        if (info == null) { PluginLog.For(id).Write("warn", $"no panel '{name}'"); return; }
        overlay |= info.Kind == "overlay";
        CloseAll(except: Key(id, info.Name)); // one panel at a time, as Omarchy's bar

        if (!panels.TryGetValue(Key(id, info.Name), out var p) || p.Stale || plugin.StalePanels.Remove(info.File))
        {
            p?.Window.Close();
            p = Load(plugin, info);
            if (p == null) return;
            panels[Key(id, info.Name)] = p;
        }
        p.Overlay = overlay;
        p.Process ??= PluginRegistry.Acquire(plugin, plugin.Id);
        if (p.Process != null)
        {
            p.Process.Message -= Feed(p); p.Process.Message += Feed(p);
            if (p.Process.LastState != null) p.Binding?.Apply(p.Process.LastState);
            p.Process.Send(Protocol.Panel(true, info.Name));
        }
        Place(p, anchor);
        Show(p);
    }

    static Open_? Load(Plugin plugin, PanelInfo info)
    {
        var log = PluginLog.For(plugin.Id);
        var file = Path.Combine(plugin.Dir, info.File);
        string xaml;
        try { xaml = System.IO.File.ReadAllText(file); } catch (Exception ex) { log.Write("error", $"{info.File}: {ex.Message}"); return null; }
        var problems = PanelCheck.Check(xaml);
        if (problems.Count > 0) { foreach (var x in problems) log.Write("error", $"{info.File}: {x}"); Notifier.Show(plugin.Manifest.Name, "Its panel has errors: omawin plugin log " + plugin.Id, null); return null; }
        FrameworkElement content;
        try { content = (FrameworkElement)XamlReader.Load(xaml); }
        catch (Exception ex) { log.Write("error", $"{info.File}: {ex.Message}"); return null; }

        var card = new Border
        {
            Child = content,
            Background = (Brush)Application.Current.Resources["OmawinSurface"],
            BorderBrush = (Brush)Application.Current.Resources["OmawinBorder"],
            BorderThickness = new Thickness(1),
            CornerRadius = (CornerRadius)Application.Current.Resources["OmawinCornerRadius"],
        };
        var window = new PanelWindow(card); // like BarWindow: borderless, tool window, topmost; but it does take focus
        var p = new Open_ { Plugin = plugin, Name = info.Name, File = info.File, Window = window, Card = card };
        p.Binding = PanelBinding.Collect(plugin, content, (action, args) => p.Process?.Send(Protocol.Action(action, args)));
        card.PreviewKeyDown += (_, e) => Keys(p, e);
        window.Activated += (_, e) => { if (e.WindowActivationState == WindowActivationState.Deactivated && !p.Overlay) Close(plugin.Id, info.Name); };
        return p;
    }

    static Action<PluginMessage> Feed(Open_ p) => feeds.TryGetValue(p, out var f) ? f : feeds[p] = m => { if (m is StateMessage s) p.Binding?.Apply(s.Data); };
    static readonly Dictionary<Open_, Action<PluginMessage>> feeds = [];

    // Escape closes; h/j/k/l move focus like the arrows (Omarchy users expect it), except while typing.
    static void Keys(Open_ p, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape) { Close(p.Plugin.Id, p.Name); e.Handled = true; return; }
        var focused = FocusManager.GetFocusedElement(p.Card.XamlRoot);
        if (focused is TextBox or AutoSuggestBox or PasswordBox or NumberBox) return;
        FocusNavigationDirection? dir = e.Key switch
        {
            Windows.System.VirtualKey.H => FocusNavigationDirection.Left,
            Windows.System.VirtualKey.J => FocusNavigationDirection.Down,
            Windows.System.VirtualKey.K => FocusNavigationDirection.Up,
            Windows.System.VirtualKey.L => FocusNavigationDirection.Right,
            _ => null,
        };
        if (dir is { } d) { FocusManager.TryMoveFocus(d, new FindNextElementOptions { SearchRoot = p.Card.XamlRoot.Content }); e.Handled = true; }
    }

    // Under the widget (its left edge, clamped to the monitor), or centred on the focused monitor.
    static void Place(Open_ p, FrameworkElement? anchor)
    {
        p.Card.Measure(new Windows.Foundation.Size(900, 900));
        var size = p.Card.DesiredSize;
        var screen = Monitors.Focused(); // GlazeWM's focused monitor (Bar.Wm.Last), else the primary; (x, y, w, h, scale)
        int w = (int)Math.Ceiling(size.Width * screen.Scale), h = (int)Math.Ceiling(size.Height * screen.Scale);
        int x, y;
        if (!p.Overlay && anchor?.XamlRoot != null && BarWindow.Of(anchor) is { } bar)
        {
            var at = anchor.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, 0));
            double s = anchor.XamlRoot.RasterizationScale;
            x = bar.Screen.X + (int)(at.X * s);
            y = bar.Screen.Y + (int)Math.Round(BarWindow.Height * s) + (int)(6 * s);
            x = Math.Clamp(x, bar.Screen.X + 8, bar.Screen.X + bar.Screen.Width - w - 8);
        }
        else { x = screen.X + (screen.Width - w) / 2; y = screen.Y + (screen.Height - h) / 3; }
        ((PanelWindow)p.Window).MoveAndResize(x, y, w, h);
    }

    // The motion tokens (1.4: Motion.cs, see 01-snappy.md (check: final names)): fade and drop in.
    static void Show(Open_ p)
    {
        var win = (PanelWindow)p.Window;
        p.Card.Opacity = 0;
        var move = new TranslateTransform { Y = p.Overlay ? 8 : -6 };
        p.Card.RenderTransform = move;
        win.ShowAndFocus();
        var board = new Storyboard();
        var fade = new DoubleAnimation { To = 1, Duration = Motion.Open.Duration, EasingFunction = Motion.Open.Ease };
        var slide = new DoubleAnimation { To = 0, Duration = Motion.Open.Duration, EasingFunction = Motion.Open.Ease };
        Storyboard.SetTarget(fade, p.Card); Storyboard.SetTargetProperty(fade, "Opacity");
        Storyboard.SetTarget(slide, move); Storyboard.SetTargetProperty(slide, "Y");
        board.Children.Add(fade); board.Children.Add(slide);
        board.Begin();
        // First focusable control, so the keys work at once.
        if (FocusManager.FindFirstFocusableElement(p.Card) is Control c) c.Focus(FocusState.Keyboard);
    }

    public static void Close(string id, string name)
    {
        if (!panels.TryGetValue(Key(id, name), out var p) || !p.Window.Visible) return;
        p.Process?.Send(Protocol.Panel(false, name));
        var board = new Storyboard();
        var fade = new DoubleAnimation { To = 0, Duration = Motion.Close.Duration, EasingFunction = Motion.Close.Ease };
        Storyboard.SetTarget(fade, p.Card); Storyboard.SetTargetProperty(fade, "Opacity");
        board.Children.Add(fade);
        board.Completed += (_, _) =>
        {
            ((PanelWindow)p.Window).HideWindow();
            if (p.Process != null) { p.Process.Message -= Feed(p); PluginRegistry.Release(p.Plugin, p.Plugin.Id); p.Process = null; }
        };
        board.Begin();
    }

    public static void CloseAll(string? except = null) { foreach (var (k, p) in panels.ToList()) if (k != except) Close(p.Plugin.Id, p.Name); }
    public static void ReloadIfOpen(Plugin plugin, string file)
    {
        foreach (var p in panels.Values.Where(p => p.Plugin.Id == plugin.Id && p.File.Equals(file, StringComparison.OrdinalIgnoreCase)))
        {
            p.Stale = true;
            if (p.Window.Visible) Open(plugin.Id, p.Name, p.Overlay); // authors see the edit at once
        }
    }
    public static void Repaint(Theme t) { foreach (var p in panels.Values) p.Card.RequestedTheme = t.Mode == "light" ? ElementTheme.Light : ElementTheme.Dark; }
}
```

`PanelWindow` (`menu-app/Plugins/PanelWindow.cs`): `BarWindow`'s recipe (`OverlappedPresenter` without border and title bar, the `NoFrame` subclass, `WS_EX_TOOLWINDOW`, `DWMWA_WINDOW_CORNER_PREFERENCE` round), **without** `WS_EX_NOACTIVATE` (a panel takes the keyboard), `IsAlwaysOnTop = true`, its title `omawin panel` so GlazeWM's rule for SystemMenu leaves it alone (`glazewm/config.yaml` already ignores SystemMenu's windows *(check: by process or title)*). `ShowAndFocus()` = `appWindow.Show(true)` + `SetForegroundWindow` (the menu app is allowed: it's the foreground's owner after a bar click, or `AllowSetForegroundWindow` from `omawin.exe`). `BarWindow.Of(element)` finds the bar whose `Content.XamlRoot` is the element's.

**The overlay kind** is the same window, centred, staying open while you click elsewhere on it (it closes on Escape or on its plugin's `close-panel`); summoned with `omawin shell summon <id> <name>` or a proposed key whose action is `overlay <name>`.

**Launcher pages** (`page` kind): `MainWindow.Pages.cs` learns `plugin:<id>/<name>` page names: `IsPage` accepts them, `RenderPage` hides the list and shows a `ContentPresenter` with the page's loaded XAML (same `PanelCheck`, `XamlReader.Load`, `PanelBinding`, no card), and the search box's text goes to the plugin as `Action.TextChanged` of the name `query` (debounced 150 ms). Menu rows open them as `@plugin:<id>/<name>`. Settings pages generated from a manifest's `schema` are built in code (one control per field type) into the same slot (v2).

## 6. Menu rows, launcher items, script commands (4.4, 4.8)

### 6.1 `menu.txt` layered, plus plugin fragments (`menu-app/MenuModel.cs`)

`Load` reads `LayeredLines("menu.txt")` (repo, shared folder, account folder; a later row with the same id replaces the earlier one) and then each enabled plugin's fragment. The cache stamp becomes the newest write time of every file read plus the registry's generation.

```csharp
// In MenuModel.Load, replacing `foreach (var raw in File.ReadAllLines(MenuFile))`:
foreach (var (raw, owner) in Layered("menu.txt").Where(File.Exists).SelectMany(f => File.ReadAllLines(f).Select(l => (l, (Plugins.Plugin?)null)))
                             .Concat(Plugins.Fragments.MenuLines()))
{
    // ... the existing parsing ...; for a plugin's row (owner != null):
    //   id     -> "plugins." + owner.Id.Replace('.', '-') + "." + id        (or under the section it names: "@section:<id>")
    //   action "plugin <name>" -> "plugin " + owner.Id + " " + name
}
// and the stamp:
static DateTime Stamp() => Layered("menu.txt").Concat(Plugins.Fragments.Files()).Select(File.GetLastWriteTimeUtc).DefaultIfEmpty().Max()
    .AddTicks(Plugins.PluginRegistry.Generation);
```

A plugin's fragment uses the same format (`id | icon | label | action`). Its rows land under `setup.plugins.<id>` unless its first line is `@section <id>` naming an existing top-level section (`apps`, `setup`, …), in which case rows go under `<section>.<plugin-id>`. Ids can't collide with omawin's: they always carry the plugin's id.

`MainWindow.Run` gets one case:

```csharp
case "plugin": // a plugin's menu row: "plugin <id> <action> [json args]"
    var parts = e.Argument.Split(' ', 3);
    if (parts.Length >= 2) Plugins.PluginRegistry.SendAction(parts[0], parts[1], parts.Length > 2 ? parts[2] : "");
    break;
```

### 6.2 `menu-app/Plugins/Fragments.cs`: items and script commands

```csharp
using System.Text.RegularExpressions;

namespace SystemMenu.Plugins;

// What enabled plugins add to the menu and the launcher, and the script commands of
// ~/omawin-account/scripts (4.8: the smallest plugin is one script with a header).
public static partial class Fragments
{
    public sealed record ScriptCommand(string Name, string Icon, string Mode, string Side, string File, string? PluginId);

    static List<ScriptCommand>? scripts;
    public static void Invalidate() => scripts = null;

    public static IEnumerable<(string Line, Plugin? Owner)> MenuLines() =>
        PluginRegistry.Enabled.Where(p => p.Manifest.Omawin?.Menu != null)
            .SelectMany(p => SafeLines(Path.Combine(p.Dir, p.Manifest.Omawin!.Menu!)).Select(l => (l, (Plugin?)p)));

    public static IEnumerable<string> ItemLines() =>
        PluginRegistry.Enabled.Where(p => p.Manifest.Omawin?.Items != null)
            .SelectMany(p => SafeLines(Path.Combine(p.Dir, p.Manifest.Omawin!.Items!)));

    public static IEnumerable<string> Files() =>
        PluginRegistry.Enabled.SelectMany(p => new[] { p.Manifest.Omawin?.Menu, p.Manifest.Omawin?.Items }.Where(f => f != null).Select(f => Path.Combine(p.Dir, f!)));

    static string[] SafeLines(string file) { try { return File.ReadAllLines(file); } catch { return []; } }

    // # @name Restart Docker
    // # @icon 󰡨
    // # @mode silent | terminal   (default: terminal)
    // # @side linux               (.sh scripts: always linux)
    [GeneratedRegex(@"^\s*(?:#|//|--|REM)\s*@(name|icon|mode|side)\s+(.+?)\s*$", RegexOptions.IgnoreCase)] private static partial Regex Header();

    public static List<ScriptCommand> Scripts()
    {
        if (scripts != null) return scripts;
        var found = new List<ScriptCommand>();
        var places = new List<(string Dir, string? Owner)> { (Path.Combine(MenuModel.Shared, "scripts"), null), (Path.Combine(MenuModel.Work, "scripts"), null) };
        places.AddRange(PluginRegistry.Enabled.Select(p => (Path.Combine(p.Dir, p.Manifest.Omawin?.Commands ?? "commands"), (string?)p.Id)));
        foreach (var (dir, owner) in places.Where(x => Directory.Exists(x.Dir)))
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is not (".ps1" or ".sh" or ".ts" or ".js" or ".py" or ".cmd" or ".bat" or ".exe")) continue;
                string name = Path.GetFileNameWithoutExtension(file), icon = "\uF489", mode = "terminal", side = ext == ".sh" ? "linux" : "windows";
                if (ext != ".exe")
                    foreach (var line in File.ReadLines(file).Take(20))
                        if (Header().Match(line) is { Success: true } m)
                            switch (m.Groups[1].Value.ToLowerInvariant())
                            {
                                case "name": name = m.Groups[2].Value; break;
                                case "icon": icon = m.Groups[2].Value; break;
                                case "mode": mode = m.Groups[2].Value.ToLowerInvariant() == "silent" ? "silent" : "terminal"; break;
                                case "side": side = m.Groups[2].Value.ToLowerInvariant() == "linux" ? "linux" : "windows"; break;
                            }
                found.Add(new ScriptCommand(name, icon, mode, side, file, owner));
            }
        return scripts = found;
    }

    // Run one: in a terminal (omawin launch terminal, honouring the default) or hidden; Linux ones in WSL.
    public static void Run(ScriptCommand s)
    {
        string[] argv = Path.GetExtension(s.File).ToLowerInvariant() switch
        {
            ".ps1" => ["pwsh", "-NoLogo", "-NoProfile", "-File", s.File],
            ".ts" or ".js" => ["bun", s.File],
            ".py" => ["uv", "run", "--no-project", s.File],
            ".sh" => ["bash", Wsl.ToLinuxPath(s.File)],
            _ => [s.File],
        };
        if (s.Mode == "terminal")
            MenuModel.Omawin(["launch", "terminal", "--title", "menu-task", .. s.Side == "linux" ? new[] { "--wsl" } : [], "--", .. argv]);
        else if (s.Side == "linux")
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("wsl.exe", ["-d", Defaults.LinuxDistro(), "--", .. argv]) { CreateNoWindow = true, UseShellExecute = false });
        else
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(argv[0], argv[1..]) { CreateNoWindow = true, UseShellExecute = false });
    }
}
```

`Launcher.cs` adds `Fragments.ItemLines()` after `MenuModel.LayeredLines("items.txt")`, and one row per `Fragments.Scripts()` entry (the icon, the name, a `script` tag; Enter runs it). A plugin's script commands start outside its Job Object: they're something you ran, like any launcher item.

## 7. Providers (8.0; plugins.md's Calendar example)

Kinds the shell draws and plugins fill. Same process and protocol; each request carries an `id`, answers with the same `id`, and the shell drops answers to requests it no longer cares about.

### 7.1 The contracts

**`launcher-provider`** (8.17 processes, 8.20 connections): rows for what you type.

```json
"omawin": { "command": ["omawin-top.exe"], "provider": { "prefix": "top", "title": "Processes", "icon": "󰍛", "always": false } }
```

```jsonc
// shell -> plugin, on each keystroke after "top " (or every query when "always": true; debounced 60 ms)
{"type":"query","id":41,"text":"code"}
{"type":"cancel","id":40}
// plugin -> shell (any number of batches; the last has "done": true)
{"type":"results","id":41,"done":true,"items":[
  {"id":"pid:1234","title":"Code.exe","subtitle":"412 MB · 3.1% CPU","icon":"󰨞","badge":"12",
   "actions":[{"name":"activate","title":"Focus"},{"name":"kill","title":"End task","confirm":true,"key":"ctrl+k"}]}]}
// Enter (or an action's key) -> plugin
{"type":"action","name":"kill","args":{"id":"pid:1234","query":"code"}}
```

**`finder-source`** (8.2 whole-disk search): another place Win+F searches, as a chip.

```jsonc
{"type":"find","id":7,"query":"invoice 2026","limit":200,"filters":{"kind":"file","ext":["pdf"]}}
{"type":"found","id":7,"done":false,"items":[{"path":"D:\\Scans\\invoice-2026-03.pdf","name":"invoice-2026-03.pdf","size":81234,"modified":"2026-03-04T10:22:00Z","kind":"file"}]}
{"type":"found","id":7,"done":true,"items":[]}
{"type":"cancel","id":7}
```

**`page`**: a launcher page (8.3 cleanup, 8.15 Health, 8.16 startup list): `pages: [{ "name": "health", "title": "Health", "icon": "󰓙", "file": "Health.xaml" }]`; shown as `@plugin:<id>/health`; fed by `state`, driven by actions (§5.5's last paragraph).

**`calendar-provider`** (plugins.md, Calendar for Omarchy): the clock's native calendar popup asks every provider.

```jsonc
// shell -> plugin when the popup opens or moves to another month
{"type":"range","from":"2026-10-01","to":"2026-11-15"}
// plugin -> shell, now and whenever something changes
{"type":"calendars","items":[{"id":"hey:work","name":"Work","color":"accent","writable":true}]}
{"type":"events","from":"2026-10-01","to":"2026-11-15","items":[
  {"id":"hey:123","calendar":"hey:work","title":"Team standup","start":"2026-10-30T09:30:00+01:00","end":"2026-10-30T09:45:00+01:00",
   "allDay":false,"place":"","link":"https://app.hey.com/calendar/...","reminders":[10]}]}
// shell -> plugin: the form's edits; "req" asks for a reply
{"type":"action","name":"create","args":{"req":5,"calendar":"hey:work","title":"Lunch","when":"tomorrow 1pm for 1h","zone":"Europe/Lisbon"}}
{"type":"action","name":"update","args":{"req":6,"id":"hey:123","title":"Standup (moved)"}}
{"type":"action","name":"delete","args":{"req":7,"id":"hey:123"}}
{"type":"action","name":"join","args":{"id":"hey:123"}}
// plugin -> shell
{"type":"reply","req":5,"ok":true,"data":{"id":"hey:456"}}
{"type":"reply","req":6,"ok":false,"message":"HEY said: not allowed"}
```

`color` is a theme colour name (`accent`, `green`, …) or `#rrggbb` *(design call: theme names keep calendars in the theme; hex only when the service insists)*. Loose date parsing (`"tomorrow 1pm for 1h"`) is the provider's (`Calendar.js` has it); the shell sends the form's text as typed.

### 7.2 `menu-app/Plugins/Providers.cs`

```csharp
using System.Text.Json.Nodes;

namespace SystemMenu.Plugins;

// The shell's side of the provider kinds: numbered requests, answers matched by id, late ones
// dropped. Results go to whoever asked (the launcher, the finder, the calendar popup) through a
// callback on the UI thread.
public static class Providers
{
    static int next;
    static readonly Dictionary<int, (Plugin Plugin, Action<JsonArray, bool> Got)> waiting = [];
    static readonly Dictionary<string, int> current = []; // plugin id -> its newest query id

    public static IEnumerable<Plugin> Launcher(string text) => PluginRegistry.Enabled.Where(p => p.Manifest.Has("launcher-provider")
        && p.Manifest.Omawin?.Provider is { } pr && (pr.Always || pr.Prefix != null && text.StartsWith(pr.Prefix + " ", StringComparison.OrdinalIgnoreCase)));

    // One keystroke: ask each matching provider; `got` may be called several times (batches).
    public static void Query(string text, Action<Plugin, JsonArray, bool> got)
    {
        foreach (var p in Launcher(text))
        {
            var proc = PluginRegistry.Acquire(p, p.Id); // kept: providers are StartsAtStartup or keep their process while the launcher is open
            if (proc == null) continue;
            Subscribe(proc);
            var prefix = p.Manifest.Omawin!.Provider!.Prefix;
            var rest = prefix != null && text.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase) ? text[(prefix.Length + 1)..] : text;
            if (current.TryGetValue(p.Id, out var old)) { proc.Send(Protocol.Cancel(old)); waiting.Remove(old); }
            int id = ++next;
            current[p.Id] = id;
            waiting[id] = (p, (items, done) => got(p, items, done));
            proc.Send(Protocol.Query(id, rest));
        }
    }

    public static void Find(Plugin p, string query, int limit, JsonObject filters, Action<JsonArray, bool> got)
    {
        var proc = PluginRegistry.Acquire(p, p.Id);
        if (proc == null) return;
        Subscribe(proc);
        int id = ++next;
        waiting[id] = (p, got);
        proc.Send(Protocol.Find(id, query, limit, filters));
    }

    static readonly HashSet<PluginProcess> subscribed = [];
    static void Subscribe(PluginProcess proc)
    {
        if (!subscribed.Add(proc)) return;
        proc.Message += m =>
        {
            (int id, JsonArray items, bool done) = m switch
            {
                ResultsMessage r => (r.Id, r.Items, r.Done),
                FoundMessage f => (f.Id, f.Items, f.Done),
                _ => (0, null!, false),
            };
            if (id == 0 || !waiting.TryGetValue(id, out var w)) return;
            w.Got(items, done);
            if (done) waiting.Remove(id);
        };
    }

    // The launcher closed: stop everything still running.
    public static void CancelAll()
    {
        foreach (var (id, w) in waiting) w.Plugin.Processes.GetValueOrDefault(w.Plugin.Id)?.Send(Protocol.Cancel(id));
        waiting.Clear(); current.Clear();
    }
}
```

Launcher glue (`MainWindow.xaml.cs`, where typed text is searched): call `Providers.Query(text, …)` alongside the local search; draw local rows at once; provider rows arrive within the frame or later and are appended under a header with the provider's `title`; a provider that hasn't answered in 300 ms shows a dim "Asking <title>…" row. Rows' Enter sends `action` with the item's `id`; `confirm: true` asks first (the existing confirm flow, as `!` rows in `menu.txt`). The calendar popup keeps a `CalendarProviders` list the same way (`range` on open, `calendars`/`events` messages held per provider, `reply` matched by `req`).

## 8. `omawin plugin` (4.5, 4.9.2)

### 8.1 `bin/commands.txt`

```
plugin add | Add a plugin from its git link: shows what it runs, its permissions and proposed keys, asks, clones it into ~/omawin-account/plugins, leaves it off ([--ref <tag>] [--shared] [--yes]) | pwsh bin/plugin.ps1 add
plugin enable | Turn a plugin on and place its widget: omawin plugin enable <id> [--section right] | pwsh bin/plugin.ps1 enable
plugin disable | Turn a plugin off (built-ins too) | pwsh bin/plugin.ps1 disable
plugin remove | Take an added plugin away (asks; moved aside, not deleted) | pwsh bin/plugin.ps1 remove
plugin update | Update plugins (or one): fetch, show the diff, ask, fast-forward; pinned ones stay on their tag | pwsh bin/plugin.ps1 update
plugin list | Every plugin: built-in or added, on or off, version, state, memory (-Json for agents) | pwsh bin/plugin.ps1 list
plugin clone | Copy a built-in to <you>.<name> to change it (Omarchy's clone); the copy is on, the original off | pwsh bin/plugin.ps1 clone
plugin new | Start a plugin: omawin plugin new <name> [--lang ps1|ts|py|bash]; on and hot-reloading | pwsh bin/plugin.ps1 new
plugin check | Check a plugin's manifest and panels without enabling it: omawin plugin check [path] (CI too) | pwsh bin/plugin.ps1 check
plugin log | A plugin's log, followed: omawin plugin log <id> | pwsh bin/plugin.ps1 log
plugin scan | Will this Omarchy plugin run here? runs as is / with stand-ins / needs a port: omawin plugin scan <git-url|path> [-Catalog <file>] [-Json] | pwsh bin/plugin.ps1 scan
```

### 8.2 `bin/plugin.ps1`

PowerShell 7 (it runs after install; nothing here needs 5.1). It reads and writes the same files the shell does; the shell's watcher picks the changes up, and `omawin shell reload` nudges it where a watcher can't see (a new root folder).

```powershell
<#
.SYNOPSIS
    omawin plugin <add|enable|disable|remove|update|list|clone|new|check|log|scan> (docs/plugins.md).
.DESCRIPTION
    Plugins live in ~/omawin/plugins (built-in), the shared folder's plugins\ (every account; each
    enables them itself) and ~/omawin-account/plugins (this account). Which are on, and where
    widgets sit: ~/omawin-account/shell.json (Omarchy's format). New plugins land off, so you can
    read the code first. --yes answers every question with yes, for scripts and agents.
#>
# -Json and -Catalog are real parameters (a single-dash word would otherwise be refused); the
# double-dash options (--yes, --ref, --section, --lang, --shared) arrive in $Rest as Omarchy spells them.
param([Parameter(Position = 0)][string]$Verb = 'list', [switch]$Json, [string]$Catalog,
      [Parameter(Position = 1, ValueFromRemainingArguments)][string[]]$Rest = @())
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\user-layer.ps1"   # $UserLayer, $SharedLayer (check: the variable names it sets)

$Repo = Split-Path $PSScriptRoot
$Roots = [ordered]@{ builtin = Join-Path $Repo 'plugins'; shared = Join-Path $SharedLayer 'plugins'; account = Join-Path $UserLayer 'plugins' }
$ShellJson = Join-Path $UserLayer 'shell.json'
$StateDir = Join-Path $env:LOCALAPPDATA 'launcher'
$Yes = $Rest -contains '--yes'
if ($Rest -contains '--json') { $Json = [switch]$true }
function Opt([string]$Name) { $i = [array]::IndexOf($Rest, $Name); if ($i -ge 0 -and $i + 1 -lt $Rest.Count) { $Rest[$i + 1] } }
$Args1 = @($Rest | Where-Object { $_ -notmatch '^-' })
# Option values aren't positional arguments.
foreach ($o in '--ref', '--section', '--lang') { if ($v = Opt $o) { $Args1 = @($Args1 | Where-Object { $_ -ne $v }) } }

function Say([string]$Text, [string]$Color = 'Gray') { Write-Host $Text -ForegroundColor $Color }
function Ask([string]$Question) {
    if ($Yes) { return $true }
    (Read-Host "$Question [y/N]") -match '^(y|yes)$'
}

# --- reading -------------------------------------------------------------------------------------
function Get-Plugins {
    $all = [ordered]@{}
    foreach ($origin in $Roots.Keys) {
        $root = $Roots[$origin]
        if (-not (Test-Path $root)) { continue }
        foreach ($dir in Get-ChildItem $root -Directory) {
            $file = Join-Path $dir.FullName 'manifest.json'
            if (-not (Test-Path $file)) { continue }
            try { $m = Get-Content $file -Raw | ConvertFrom-Json -AsHashtable } catch { Say "  $($dir.Name): manifest.json isn't valid JSON ($($_.Exception.Message))" Yellow; continue }
            $all[$m.id] = [pscustomobject]@{ Id = $m.id; Origin = $origin; Dir = $dir.FullName; Manifest = $m }
        }
    }
    $all
}

function Get-ShellConfig {
    if (Test-Path $ShellJson) { return Get-Content $ShellJson -Raw | ConvertFrom-Json -AsHashtable }
    # The default: the repo's bar layout as Omarchy's shell.json.
    $bar = Get-Content (Join-Path $Repo 'zebar\tokyo-islands\bar.json') -Raw | ConvertFrom-Json -AsHashtable
    $layout = [ordered]@{}
    foreach ($s in 'left', 'centerBegin', 'center', 'centerEnd', 'right') { $layout[$s] = @($bar[$s]) }
    [ordered]@{ bar = [ordered]@{ layout = $layout; clock = $bar.clock }; plugins = @(); disabledPlugins = @() }
}

function Save-ShellConfig($Config) {
    New-Item -ItemType Directory -Force (Split-Path $ShellJson) | Out-Null
    if ((Test-Path $ShellJson) -and -not (Test-Path "$ShellJson.backup-plugins")) { Copy-Item $ShellJson "$ShellJson.backup-plugins" } # the first edit keeps your original
    $tmp = "$ShellJson.tmp"
    $Config | ConvertTo-Json -Depth 20 | Set-Content $tmp -Encoding utf8NoBOM
    Move-Item $tmp $ShellJson -Force
}

function Show-Manifest($m, [string]$Dir) {
    $o = $m.omawin
    Say ""; Say "  $($m.name) $($m.version)  ($($m.id))" White
    if ($m.description) { Say "  $($m.description)" }
    Say "  by $($m.author), $($m.license)"
    Say "  kinds: $($m.kinds -join ', ')"
    if ($o) {
        if ($o.command) { Say "  runs: $($o.command -join ' ')$(if ($o.side -eq 'linux') { '  (in Linux)' })$(if ($o.interval) { "  every $($o.interval) s" })" Cyan }
        Say "  permissions it declares: $(if ($o.permissions) { $o.permissions -join ', ' } else { 'none' }) (shown, not enforced: a plugin runs with your rights)"
        foreach ($k in @($o.keys)) { if ($k) { Say "  proposes a key: $($k.binding) -> $($k.action)  ($($k.description)); bound only if you accept it on the Plugins page" Yellow } }
    } else { Say "  no omawin block: an Omarchy plugin. omawin plugin scan says whether it can run here." Yellow }
    if ($Dir) { Say "  files: $((Get-ChildItem $Dir -Recurse -File | Where-Object FullName -notmatch '\\\.git\\').Count) in $Dir" }
}

# --- verbs ---------------------------------------------------------------------------------------
function Invoke-Add {
    $url = $Args1[0]
    if (-not $url) { throw 'omawin plugin add <git-url> [--ref <tag>] [--shared]' }
    if ($url -notmatch '^(https://|git@)[\w.@:/~-]+$') { throw "not a git link: $url" }
    $ref = Opt '--ref'
    $root = if ($Rest -contains '--shared') { $Roots.shared } else { $Roots.account }
    $tmp = Join-Path ([IO.Path]::GetTempPath()) "omawin-plugin-$([guid]::NewGuid().ToString('n').Substring(0, 8))"
    # From the curated index: install the commit it reviewed (plugins/index.txt: id | name | description | url | commit | runs).
    $indexed = Get-Content (Join-Path $Repo 'plugins\index.txt') -ErrorAction SilentlyContinue | Where-Object { $_ -notmatch '^\s*#' } |
        ForEach-Object { $c = $_ -split '\s*\|\s*'; if ($c[3] -eq $url) { $c[4] } } | Select-Object -First 1
    git clone --quiet --filter=blob:none $url $tmp
    if ($LASTEXITCODE) { throw "git clone failed" }
    if ($ref) { git -C $tmp checkout --quiet $ref }
    elseif ($indexed) { git -C $tmp checkout --quiet $indexed; Say "  the reviewed commit from omawin's index: $indexed" }
    $file = Join-Path $tmp 'manifest.json'
    if (-not (Test-Path $file)) { Remove-Item $tmp -Recurse -Force; throw 'no manifest.json at the root: not a plugin' }
    $m = Get-Content $file -Raw | ConvertFrom-Json -AsHashtable
    Show-Manifest $m $tmp
    $check = Invoke-Check $tmp -Quiet
    if (-not $check) { Say "  it has errors (above); adding it anyway leaves it off and unusable until fixed." Yellow }
    $dest = Join-Path $root $m.id
    if (Test-Path $dest) { Remove-Item $tmp -Recurse -Force; throw "$($m.id) is already there: $dest (omawin plugin update $($m.id))" }
    if (-not (Ask "  Add $($m.name)? It stays off until you enable it.")) { Remove-Item $tmp -Recurse -Force; return }
    New-Item -ItemType Directory -Force $root | Out-Null
    Move-Item $tmp $dest
    Say "  added: $dest" Green
    Say "  read it, then: omawin plugin enable $($m.id)"
}

function Invoke-Enable {
    $id = $Args1[0]; $all = Get-Plugins
    $p = $all[$id]; if (-not $p) { throw "no plugin $id (omawin plugin list)" }
    $c = Get-ShellConfig
    $c.disabledPlugins = @($c.disabledPlugins | Where-Object { $_ -ne $id })
    if ($p.Origin -ne 'builtin' -and $c.plugins -notcontains $id) { $c.plugins = @($c.plugins) + $id }
    $m = $p.Manifest
    if ($m.kinds -contains 'bar-widget') {
        $section = (Opt '--section') ?? $m.barWidget.defaultSection ?? 'right'
        $placed = $false
        foreach ($s in $c.bar.layout.Keys) { if (@($c.bar.layout[$s]) | Where-Object { ($_ -is [string] ? $_ : $_.id) -eq $id }) { $placed = $true } }
        if ($m.omawin.replaces) {
            # In the place of the built-in it replaces (the clock), which comes out of the bar.
            foreach ($s in @($c.bar.layout.Keys)) {
                $list = [Collections.ArrayList]@($c.bar.layout[$s])
                $i = $list.IndexOf($m.omawin.replaces); if ($i -ge 0) { $list[$i] = $id; $c.bar.layout[$s] = @($list); $placed = $true }
            }
        }
        if (-not $placed) { $c.bar.layout[$section] = @($c.bar.layout[$section]) + $id }
    }
    Save-ShellConfig $c
    Say "  $($m.name) is on$(if ($m.kinds -contains 'bar-widget') { " (in the bar's $section)" })." Green
    if ($m.omawin.keys) { Say "  It proposes keys: accept them in Setup > Plugins > $($m.name)." Yellow }
}

function Invoke-Disable {
    $id = $Args1[0]; $c = Get-ShellConfig
    $c.plugins = @($c.plugins | Where-Object { $_ -ne $id })
    if ($c.disabledPlugins -notcontains $id) { $c.disabledPlugins = @($c.disabledPlugins) + $id }
    foreach ($s in @($c.bar.layout.Keys)) { $c.bar.layout[$s] = @($c.bar.layout[$s] | Where-Object { ($_ -is [string] ? $_ : $_.id) -ne $id }) }
    Save-ShellConfig $c
    Say "  $id is off." Green
}

function Invoke-Remove {
    $id = $Args1[0]; $p = (Get-Plugins)[$id]
    if (-not $p) { throw "no plugin $id" }
    if ($p.Origin -eq 'builtin') { throw "$id is built in: omawin plugin disable $id turns it off" }
    if (-not (Ask "  Remove $id from $($p.Dir)?")) { return }
    Invoke-Disable
    # Moved aside, never deleted (AGENTS.md): your edits in it stay recoverable.
    $aside = Join-Path $StateDir "plugins-removed\$id-$(Get-Date -Format yyyyMMdd-HHmmss)"
    New-Item -ItemType Directory -Force (Split-Path $aside) | Out-Null
    Move-Item $p.Dir $aside
    Say "  removed; the folder is in $aside (its data stays in $StateDir\plugins\$id)." Green
}

function Invoke-Update {
    $all = Get-Plugins
    $ids = if ($Args1[0]) { @($Args1[0]) } else { @($all.Keys | Where-Object { $all[$_].Origin -ne 'builtin' }) }
    foreach ($id in $ids) {
        $p = $all[$id]
        if (-not $p -or -not (Test-Path (Join-Path $p.Dir '.git'))) { Say "  $id`: not a git clone, skipped"; continue }
        if ($p.Origin -eq 'builtin') { Say "  $id`: built in, updated with omawin itself"; continue }
        $dir = $p.Dir
        $newRef = Opt '--ref'
        $pinned = git -C $dir describe --tags --exact-match HEAD 2>$null
        $branch = git -C $dir symbolic-ref --quiet --short HEAD 2>$null
        git -C $dir fetch --quiet --tags origin
        if ($newRef) { $target = $newRef }
        elseif (-not $branch -and $pinned) { Say "  $id`: pinned to $pinned, stays (--ref <tag> moves it)"; continue }
        else { $target = "@{u}" }
        $count = git -C $dir rev-list --count "HEAD..$target"
        if ([int]$count -eq 0) { Say "  $id`: up to date"; continue }
        Say ""; Say "  $id`: $count new commit(s)" White
        git -C $dir --no-pager log --oneline "HEAD..$target"
        git -C $dir --no-pager diff --stat "HEAD..$target"
        if (-not $Yes) { git -C $dir --no-pager diff "HEAD..$target" -- . ':(exclude)*.png' ':(exclude)*.jpg' | Out-Host } # the whole diff: read before you run it
        $before = Get-Content (Join-Path $dir 'manifest.json') -Raw | ConvertFrom-Json -AsHashtable
        if (-not (Ask "  Apply?")) { continue }
        if ($newRef) { git -C $dir checkout --quiet $newRef } else { git -C $dir merge --ff-only --quiet $target }
        if ($LASTEXITCODE) { Say "  $id`: can't fast-forward (local changes?); left as it was" Yellow; continue }
        $after = Get-Content (Join-Path $dir 'manifest.json') -Raw | ConvertFrom-Json -AsHashtable
        # A changed command, new permissions or new keys are worth saying out loud.
        if (($before.omawin.command -join ' ') -ne ($after.omawin.command -join ' ')) { Say "  it now runs: $($after.omawin.command -join ' ')" Yellow }
        $newPerms = @($after.omawin.permissions | Where-Object { $before.omawin.permissions -notcontains $_ })
        if ($newPerms) { Say "  new permissions: $($newPerms -join ', ')" Yellow }
        Say "  $id updated." Green
    }
}

function Invoke-List {
    $all = Get-Plugins; $c = Get-ShellConfig
    $status = @{}
    $statusFile = Join-Path $StateDir 'plugins\status.json'
    if (Test-Path $statusFile) { foreach ($s in (Get-Content $statusFile -Raw | ConvertFrom-Json)) { $status[$s.id] = $s } }
    $rows = foreach ($p in $all.Values) {
        $on = $c.disabledPlugins -notcontains $p.Id -and ($p.Origin -eq 'builtin' -or $c.plugins -contains $p.Id)
        $s = $status[$p.Id]
        [pscustomobject]@{ Id = $p.Id; Name = $p.Manifest.name; Origin = $p.Origin; On = $on; Version = $p.Manifest.version
            State = $s.state ?? ''; MemoryMB = if ($s.memoryBytes) { [math]::Round($s.memoryBytes / 1MB) } else { $null } }
    }
    if ($Json) { $rows | ConvertTo-Json -Depth 4; return }
    $rows | Sort-Object Origin, Id | Format-Table -AutoSize
}

function Invoke-Clone {
    $id = $Args1[0]; $p = (Get-Plugins)[$id]
    if (-not $p -or $p.Origin -ne 'builtin') { throw "clone copies a built-in plugin; $id isn't one" }
    $me = ($env:USERNAME -replace '[^A-Za-z0-9-]', '').ToLowerInvariant()
    $newId = "$me.$(($id -split '\.')[-1])"
    $dest = Join-Path $Roots.account $newId
    if (Test-Path $dest) { throw "$dest exists" }
    Copy-Item $p.Dir $dest -Recurse
    $mf = Join-Path $dest 'manifest.json'
    $m = Get-Content $mf -Raw | ConvertFrom-Json -AsHashtable
    $m.id = $newId; $m.name = "$($m.name) (mine)"
    if (-not $m.omawin) { $m.omawin = [ordered]@{} }
    $m.omawin.clonedFrom = $id
    if (-not $m.omarchy) { $m.omarchy = [ordered]@{} }; $m.omarchy.clonedFrom = $id   # Omarchy's own field, so its tooling agrees
    $m | ConvertTo-Json -Depth 20 | Set-Content $mf -Encoding utf8NoBOM
    $script:Args1 = @($id); Invoke-Disable
    $script:Args1 = @($newId); Invoke-Enable
    Say "  your copy: $dest; edit it and the shell reloads it." Green
}

function Invoke-Log {
    $id = $Args1[0]; $file = Join-Path $StateDir "plugins\$id\plugin.log"
    if (-not (Test-Path $file)) { Say "  nothing logged for $id yet ($file)"; return }
    Get-Content $file -Tail 60 -Wait
}

# Checks use the shell's own rules: SystemMenu.exe --plugin-check prints problems and exits 1 on
# errors (handled before the single-copy check in Program.Main, like --qs-selftest, so it works
# while the menu app runs). It loads each panel with XamlReader too, so CI catches bad XAML.
function Invoke-Check([string]$Path, [switch]$Quiet) {
    if (-not $Path) { $Path = $Args1[0] ?? (Get-Location).Path }
    $exe = Join-Path $env:LOCALAPPDATA 'system-menu\SystemMenu.exe'
    $out = & $exe --plugin-check (Resolve-Path $Path).Path 2>&1
    $ok = $LASTEXITCODE -eq 0
    if (-not $Quiet -or -not $ok) { $out | ForEach-Object { Say "  $_" ($(if ("$_" -match '^error') { 'Red' } elseif ("$_" -match '^warn') { 'Yellow' } else { 'Gray' })) } }
    if (-not $Quiet) { Say $(if ($ok) { '  ok' } else { '  errors: fix them before enabling' }) $(if ($ok) { 'Green' } else { 'Red' }) }
    $ok
}

. "$PSScriptRoot\plugin-new.ps1"   # Invoke-New (§8.3)
. "$PSScriptRoot\plugin-scan.ps1"  # Invoke-Scan (§8.4)

switch ($Verb) {
    'add' { Invoke-Add } 'enable' { Invoke-Enable } 'disable' { Invoke-Disable } 'remove' { Invoke-Remove }
    'update' { Invoke-Update } 'list' { Invoke-List } 'clone' { Invoke-Clone } 'new' { Invoke-New }
    'check' { if (-not (Invoke-Check)) { exit 1 } } 'log' { Invoke-Log } 'scan' { Invoke-Scan }
    default { throw "omawin plugin $Verb`: add, enable, disable, remove, update, list, clone, new, check, log, scan" }
}
```

`SystemMenu.exe --plugin-check <dir>` (in `Program.Main`, before the mutex): needs WinUI for `XamlReader`, so it does `Application.Start` with a tiny `App` path that runs `ManifestCheck.Check`, then `PanelCheck.Check` + `XamlReader.Load` on each panel and page, then a dry `PanelBinding.Collect` (catches `omawin:Action` on an element that has no such event), prints `error: …` / `warn: …` lines to a console it attaches (`AttachConsole(-1)`, as `--qs-selftest` does), and exits with 1 on any error. *(check: `--qs-selftest`'s console handling, and whether WinUI can start without a window for this.)*

### 8.3 `omawin plugin new`: `bin/plugin-new.ps1` and `bin/plugin-templates/`

```powershell
# omawin plugin new <name> [--lang ps1|ts|py|bash]: a working plugin in the account folder, on
# and hot-reloading: a manifest, a widget script that counts clicks, a panel, a README.
function Invoke-New {
    $name = $Args1[0]; if (-not $name) { throw 'omawin plugin new <name> [--lang ps1|ts|py|bash]' }
    $lang = (Opt '--lang') ?? 'ts'
    if ($lang -notin 'ps1', 'ts', 'py', 'bash') { throw "--lang: ps1, ts, py or bash" }
    $slug = ($name.ToLowerInvariant() -replace '[^a-z0-9]+', '-').Trim('-')
    $me = ($env:USERNAME -replace '[^A-Za-z0-9-]', '').ToLowerInvariant()
    $id = "$me.$slug"
    $dest = Join-Path $Roots.account $id
    if (Test-Path $dest) { throw "$dest exists" }
    $templates = Join-Path $PSScriptRoot 'plugin-templates'
    New-Item -ItemType Directory -Force $dest | Out-Null
    $script = @{ ps1 = 'widget.ps1'; ts = 'widget.ts'; py = 'widget.py'; bash = 'widget.sh' }[$lang]
    $command = @{ ps1 = @('widget.ps1'); ts = @('bun', 'widget.ts'); py = @('uv', 'run', '--no-project', 'widget.py'); bash = @('bash', 'widget.sh') }[$lang]
    $vars = @{ '{{id}}' = $id; '{{name}}' = $name; '{{author}}' = $env:USERNAME }
    foreach ($f in $script, 'Panel.xaml', 'README.md') {
        $text = Get-Content (Join-Path $templates $f) -Raw
        foreach ($k in $vars.Keys) { $text = $text.Replace($k, $vars[$k]) }
        Set-Content (Join-Path $dest $f) $text -Encoding utf8NoBOM -NoNewline
    }
    $manifest = [ordered]@{
        schemaVersion = 1; id = $id; name = $name; version = '0.1.0'; author = $env:USERNAME; license = 'MIT'
        description = "$name for the omawin bar."; kinds = @('bar-widget', 'panel')
        barWidget = [ordered]@{ displayName = $name; category = 'Info'; allowMultiple = $false; defaultSection = 'right'
            defaults = [ordered]@{ greeting = 'Hello' }
            schema = @([ordered]@{ key = 'greeting'; type = 'string'; label = 'Greeting'; defaultValue = 'Hello' }) }
        omawin = [ordered]@{ command = $command; panel = 'Panel.xaml'; side = $(if ($lang -eq 'bash') { 'linux' } else { 'windows' }); permissions = @() }
    }
    $manifest | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $dest 'manifest.json') -Encoding utf8NoBOM
    git -C $dest init --quiet
    $script:Args1 = @($id); Invoke-Enable
    Say "  $dest: edit $script or Panel.xaml and the bar reloads it. omawin plugin log $id shows its errors." Green
    & omawin.exe launch editor $dest
}
```

`bin/plugin-templates/widget.ts`:

```ts
// {{name}}: an omawin bar widget (docs/plugins.md). The shell sends one JSON object per line on
// stdin; print one per line on stdout. Edit and save: the shell restarts it.
import { createInterface } from "node:readline";

type Msg = { type: string; [k: string]: any };
const out = (o: object) => process.stdout.write(JSON.stringify(o) + "\n");

let settings: Record<string, any> = {};
let clicks = 0;

function render() {
  out({ text: `\u{f0a7a} ${clicks}`, tooltip: `${settings.greeting ?? "Hello"}! Clicked ${clicks} times`, class: clicks % 2 ? "accent" : "" });
  out({ type: "state", data: { title: `${settings.greeting ?? "Hello"} from {{name}}`, clicks, odd: clicks % 2 === 1 } });
}

for await (const line of createInterface({ input: process.stdin })) {
  let m: Msg;
  try { m = JSON.parse(line); } catch { continue; }
  switch (m.type) {
    case "init": settings = m.settings ?? {}; break;
    case "settings": settings = m.settings ?? {}; break;
    case "click": if (m.button === "right") clicks = 0; break;
    case "action": if (m.name === "add") clicks++; if (m.name === "reset") clicks = 0; break;
    case "shutdown": process.exit(0);
  }
  render();
}
```

`bin/plugin-templates/widget.ps1`:

```powershell
# {{name}}: an omawin bar widget (docs/plugins.md). Read-OmawinMessage / Write-OmawinMessage come
# from omawin's SDK, so this also runs in the shared PowerShell host. Edit and save: it restarts.
. (Join-Path $env:OMAWIN_SDK 'ps\Omawin.Plugin.ps1')
$settings = @{}; $clicks = 0
function Render {
    Write-OmawinBar -Text "$([char]::ConvertFromUtf32(0xF0A7A)) $clicks" -Tooltip "$($settings.greeting)! Clicked $clicks times"
    Write-OmawinState @{ title = "$($settings.greeting) from {{name}}"; clicks = $clicks; odd = ($clicks % 2) -eq 1 }
}
while ($true) {
    $m = Read-OmawinMessage
    switch ($m.type) {
        'init' { $settings = $m.settings }
        'settings' { $settings = $m.settings }
        'click' { if ($m.button -eq 'right') { $clicks = 0 } }
        'action' { if ($m.name -eq 'add') { $clicks++ } elseif ($m.name -eq 'reset') { $clicks = 0 } }
        'shutdown' { exit 0 }
    }
    Render
}
```

`bin/plugin-templates/widget.sh` (side linux; Omarchy's command-module style, works unchanged in Waybar too):

```bash
#!/bin/bash
# {{name}}: an omawin bar widget, in Linux (side: linux). One JSON line in, one out.
clicks=0
while IFS= read -r line; do
  case "$(jq -r '.type' <<<"$line")" in
    action) [[ $(jq -r '.name' <<<"$line") == "add" ]] && ((clicks++)) ;;
    shutdown) exit 0 ;;
  esac
  printf '{"text":"󰩺 %d","tooltip":"Clicked %d times"}\n' "$clicks" "$clicks"
  printf '{"type":"state","data":{"title":"{{name}} in Linux","clicks":%d}}\n' "$clicks"
done
```

`bin/plugin-templates/Panel.xaml`:

```xml
<StackPanel xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
            xmlns:omawin="using:Omawin.Plugins"
            Width="280" Padding="16" Spacing="12">
  <TextBlock Style="{ThemeResource OmawinTitle}" omawin:Bind.Text="title"/>
  <TextBlock FontSize="40" Foreground="{ThemeResource OmawinAccent}" omawin:Bind.Text="clicks"/>
  <TextBlock Style="{ThemeResource OmawinCaption}" Text="An odd number" omawin:Bind.Visible="odd"/>
  <StackPanel Orientation="Horizontal" Spacing="8">
    <Button Content="Add one" omawin:Action.Click="add">
      <Button.KeyboardAccelerators><KeyboardAccelerator Key="A" omawin:Action.Invoked="add"/></Button.KeyboardAccelerators>
    </Button>
    <Button Content="Reset" omawin:Action.Click="reset"/>
  </StackPanel>
</StackPanel>
```

`widget.py` is the `.ts` one in Python (a `for line in sys.stdin:` loop, `print(json.dumps(...), flush=True)`), with a PEP 723 header so `uv run` brings its dependencies. `README.md`: what the files are, the protocol's five messages it uses, `omawin plugin log`, how to publish (topic `omawin-plugin`).

### 8.4 `omawin plugin scan`: `bin/plugin-scan.ps1`

Reads a plugin's QML imports and the commands it runs; says **runs as is**, **runs with stand-ins**, or **needs a port**, with the reasons. `-Catalog <file>` (one git URL a line, Okomart's `plugins.txt` format) scans many and counts which stand-ins and modules would unlock the most.

```powershell
# What an Omarchy plugin needs from omawin (docs/plugins.md, "Knowing which plugins will run").
$ScanRules = @{
    # QML imports: the module name's start -> verdict
    Imports = [ordered]@{
        'QtQuick' = 'as-is'; 'QtQml' = 'as-is'; 'Qt5Compat' = 'as-is'; 'QtCore' = 'as-is'
        'qs.Commons' = 'as-is'; 'qs.Ui' = 'as-is'
        'Quickshell.Io' = 'as-is'; 'Quickshell.Widgets' = 'as-is'
        'Quickshell.Hyprland' = 'stand-in:hyprland'; 'Quickshell.Wayland' = 'stand-in:wayland'
        'Quickshell.Services.Mpris' = 'port:media'; 'Quickshell.Services.UPower' = 'port:battery'; 'Quickshell.Services.Notifications' = 'port:notifications'
        'Quickshell.Services.Pipewire' = 'port:audio'; 'Quickshell.Services.SystemTray' = 'port:tray'; 'Quickshell.Services.Pam' = 'port:pam'
        'Quickshell.Services.Greetd' = 'port:greetd'; 'Quickshell.Bluetooth' = 'port:bluetooth'; 'Quickshell.Networking' = 'port:network'
        'Quickshell' = 'as-is'   # core: last, so the specific ones above win
        'qs.' = 'port:omarchy-service'   # other Omarchy modules (qs.services, qs.Bar ...): the host would have to provide them
    }
    # Commands (the first word of a Process command, execDetached, or a sh -c string)
    Commands = @{
        'hyprctl' = 'stand-in:hyprctl'; 'omarchy-notification-send' = 'stand-in:notify'; 'notify-send' = 'stand-in:notify'; 'omarchy-shell' = 'stand-in:omarchy-shell'
        'xdg-open' = 'stand-in:xdg-open'; 'wl-copy' = 'stand-in:clipboard'; 'wl-paste' = 'stand-in:clipboard'
        'grim' = 'port:screen-capture'; 'slurp' = 'port:screen-capture'; 'wf-recorder' = 'port:screen-capture'; 'hyprpicker' = 'port:screen-capture'
        'swww' = 'port:wallpaper'; 'swaybg' = 'port:wallpaper'; 'brightnessctl' = 'port:brightness'; 'wpctl' = 'port:audio'; 'pactl' = 'port:audio'
        'nmcli' = 'port:network'; 'bluetoothctl' = 'port:bluetooth'; 'loginctl' = 'port:session'; 'systemctl' = 'linux'; 'playerctl' = 'port:media'
    }
}

function Get-ScanVerdict([string]$Dir) {
    $needs = [Collections.Generic.HashSet[string]]::new()
    $why = [Collections.Generic.List[string]]::new()
    foreach ($f in Get-ChildItem $Dir -Recurse -Include *.qml, *.js -File | Where-Object FullName -notmatch '\\(\.git|tests?|node_modules)\\') {
        $text = Get-Content $f.FullName -Raw
        $rel = [IO.Path]::GetRelativePath($Dir, $f.FullName)
        foreach ($m in [regex]::Matches($text, '(?m)^\s*import\s+([A-Za-z][\w.]*)')) {
            $mod = $m.Groups[1].Value
            $rule = $ScanRules.Imports.Keys | Where-Object { $mod -eq $_ -or $mod.StartsWith("$_.") -or ($_ -eq 'qs.' -and $mod.StartsWith('qs.')) } | Select-Object -First 1
            $v = if ($rule) { $ScanRules.Imports[$rule] } else { "port:unknown-module" }
            if ($v -ne 'as-is') { [void]$needs.Add($v); $why.Add("$rel imports $mod ($v)") }
        }
        # command: ["hyprctl", ...]   execDetached(["notify-send", ...])   "sh", "-c", "grim ..."
        foreach ($m in [regex]::Matches($text, '(?:command\s*:\s*|exec(?:Detached)?\s*\(\s*)\[\s*"([^"]+)"(?:\s*,\s*"(-c|-lc)"\s*,\s*"([^"]*)")?')) {
            $words = @($m.Groups[1].Value)
            if ($m.Groups[3].Success) { $words += [regex]::Matches($m.Groups[3].Value, '(?:^|[;&|]\s*)([\w.-]+)') | ForEach-Object { $_.Groups[1].Value } }
            foreach ($w in $words) {
                $name = Split-Path $w -Leaf
                if ($ScanRules.Commands.ContainsKey($name)) { $v = $ScanRules.Commands[$name]; [void]$needs.Add($v); $why.Add("$rel runs $name ($v)") }
            }
        }
    }
    $verdict = if ($needs | Where-Object { $_ -like 'port:*' }) { 'needs a port' } elseif ($needs | Where-Object { $_ -like 'stand-in:*' }) { 'runs with stand-ins' } else { 'runs as is' }
    $hasOmawin = (Test-Path (Join-Path $Dir 'manifest.json')) -and ((Get-Content (Join-Path $Dir 'manifest.json') -Raw | ConvertFrom-Json -AsHashtable).omawin)
    [pscustomobject]@{ Verdict = $(if ($hasOmawin) { 'ported' } else { $verdict }); Needs = @($needs); Why = @($why) }
}

function Invoke-Scan {
    $targets = if ($Catalog) { Get-Content $Catalog | Where-Object { $_ -match '^\s*https://' } | ForEach-Object { $_.Trim() } } else { @($Args1[0]) }
    $results = foreach ($t in $targets) {
        $dir = $t; $tmp = $null
        if ($t -match '^https://') {
            $tmp = Join-Path ([IO.Path]::GetTempPath()) "omawin-scan-$([guid]::NewGuid().ToString('n').Substring(0, 8))"
            git clone --quiet --depth 1 $t $tmp 2>$null
            if ($LASTEXITCODE) { [pscustomobject]@{ Plugin = $t; Verdict = 'unreachable'; Needs = @(); Why = @() }; continue }
            $dir = $tmp
        }
        $v = Get-ScanVerdict $dir
        [pscustomobject]@{ Plugin = $t; Verdict = $v.Verdict; Needs = $v.Needs; Why = $v.Why }
        if ($tmp) { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue } # our own temp clone
    }
    if ($Json) { $results | ConvertTo-Json -Depth 5; return }
    foreach ($r in $results) {
        Say "$($r.Plugin): $($r.Verdict)" $(switch ($r.Verdict) { 'runs as is' { 'Green' } 'runs with stand-ins' { 'Cyan' } 'ported' { 'Green' } default { 'Yellow' } })
        if ($targets.Count -eq 1) { $r.Why | ForEach-Object { Say "  $_" } }
    }
    if ($targets.Count -gt 1) {
        Say ""; Say "Verdicts:" White
        $results | Group-Object Verdict | Sort-Object Count -Descending | ForEach-Object { Say ("  {0,5}  {1}" -f $_.Count, $_.Name) }
        Say "What would unlock the most (plugins that need only this one thing to move up):" White
        $results | Where-Object { $_.Needs.Count -eq 1 } | Group-Object { $_.Needs[0] } | Sort-Object Count -Descending |
            ForEach-Object { Say ("  {0,5}  {1}" -f $_.Count, $_.Name) }
    }
}
```

The scan is a heuristic (regexes over QML), good enough to rank work; it errs towards "needs a port". The QML host's `Process` also logs every program a plugin actually runs, so real use corrects the rules.

## 9. First-party plugins: the SDK and admin verbs (8.0)

### 9.1 Layout

```
plugins/
  index.txt                          curated: id | name | description | git url | reviewed commit | runs (as-is|ported|omarchy-only)
  sdk/
    Omawin.Plugin/Omawin.Plugin.csproj
    Omawin.Plugin/PluginHost.cs      the protocol loop, typed messages, --selftest
    Omawin.Plugin/Messages.cs        records + source-generated JSON context
    Omawin.Plugin.Tests/             protocol tests (xUnit, run on Linux CI too: no Windows APIs)
    ps/Omawin.Plugin.ps1, ps/pwsh-host.ps1   (§3.6)
    Directory.Build.props            shared: net9.0-windows, AOT, analyzers
  omawin.top/                        8.17: processes in the launcher (the example below)
    manifest.json
    src/Omawin.Top.csproj, Program.cs, NativeMethods.txt, NativeMethods.json
shared/Omawin.Windows/               the library the menu app and plugins share (Restart Manager, display config, event log); 8.0
```

The release workflow builds each `plugins/<id>/src/*.csproj` for win-x64 and win-arm64 and publishes `omawin-<id>-<arch>.zip`; `plugin enable` of a first-party plugin with no exe downloads it (as the menu app is, checked against `SHA256SUMS.txt`) into `plugins/<id>/bin/`, which is git-ignored. Its manifest's command is then `["bin/omawin-top.exe"]`.

### 9.2 `plugins/sdk/Omawin.Plugin/Omawin.Plugin.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <!-- omawin's plugin SDK (docs/plugins.md, docs/plan/05-plugins.md §9): the JSON-lines protocol
       for first-party plugins written in C# and compiled with Native AOT. No WinUI, no Windows App
       SDK: plugins are console processes the shell draws for. -->
  <PropertyGroup>
    <TargetFramework>net9.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsAotCompatible>true</IsAotCompatible>
    <IsTrimmable>true</IsTrimmable>
    <RootNamespace>Omawin.Plugin</RootNamespace>
  </PropertyGroup>
</Project>
```

### 9.3 `plugins/sdk/Omawin.Plugin/Messages.cs`

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Omawin.Plugin;

public sealed record InitMessage(JsonObject Settings, string Theme, JsonObject? Palette, string OmawinVersion, string Id, string Instance, string PluginDir, string DataDir, string Side);
public sealed record ProviderItem(string Id, string Title, string? Subtitle = null, string? Icon = null, string? Badge = null, List<ProviderAction>? Actions = null);
public sealed record ProviderAction(string Name, string Title, string? Key = null, bool Confirm = false);
public sealed record FoundItem(string Path, string Name, long Size, DateTimeOffset Modified, string Kind);
public sealed record AdminResult(string Verb, bool Ok, string Message);

// Outbound shapes, written by the source generator (no reflection under AOT).
internal sealed record BarOut(string? Text, string? Tooltip, string? Class, double? Percentage, string? Alt);
internal sealed record StateOut(string Type, JsonNode Data, bool? Merge);
internal sealed record NotifyOut(string Type, string Title, string? Body, string? Icon, string? Open);
internal sealed record LogOut(string Type, string Level, string Message);
internal sealed record ResultsOut(string Type, int Id, List<ProviderItem> Items, bool Done);
internal sealed record FoundOut(string Type, int Id, List<FoundItem> Items, bool Done);
internal sealed record ReplyOut(string Type, int Req, bool Ok, string? Message, JsonNode? Data);
internal sealed record AdminOut(string Type, string Verb, string[] Args, string Why);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(InitMessage))]
[JsonSerializable(typeof(BarOut))] [JsonSerializable(typeof(StateOut))] [JsonSerializable(typeof(NotifyOut))] [JsonSerializable(typeof(LogOut))]
[JsonSerializable(typeof(ResultsOut))] [JsonSerializable(typeof(FoundOut))] [JsonSerializable(typeof(ReplyOut))] [JsonSerializable(typeof(AdminOut))]
internal partial class SdkJson : JsonSerializerContext { }
```

### 9.4 `plugins/sdk/Omawin.Plugin/PluginHost.cs`

```csharp
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Omawin.Plugin;

// A first-party plugin: override what it handles, call Bar / State / Notify / Results to talk.
// Messages are handled one at a time on one thread (no locking in plugin code); Tick runs on the
// same thread. Run with --selftest to run SelfTest() and exit 0/1 (CI).
public abstract class PluginBase
{
    public InitMessage Init { get; private set; } = null!;
    public JsonObject Settings { get; private set; } = [];
    protected string DataDir => Init.DataDir;
    // A tick while running: null for none. Changing it takes effect after the current message.
    protected TimeSpan? TickEvery { get; set; }

    protected virtual void OnInit() { }
    protected virtual void OnSettings() { }
    protected virtual void OnClick(string button) { }
    protected virtual void OnWheel(int delta) { }
    protected virtual void OnPanel(bool open, string name) { }
    protected virtual void OnAction(string name, JsonObject args) { }
    protected virtual void OnQuery(int id, string text) { }
    protected virtual void OnCancel(int id) { }
    protected virtual void OnFind(int id, string query, int limit, JsonObject filters) { }
    protected virtual void OnEvent(string source, string name, JsonNode? data) { }
    protected virtual void OnTheme(string name, JsonObject? palette) { }
    protected virtual void OnAdminResult(AdminResult r) { }
    protected virtual void OnTick() { }
    protected virtual void OnShutdown() { }
    public virtual int SelfTest() => 0;

    // --- to the shell ------------------------------------------------------------------------------
    static readonly object writing = new();
    static readonly Stream stdout = Console.OpenStandardOutput();
    static void Write<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, type);
        lock (writing) { stdout.Write(bytes); stdout.WriteByte((byte)'\n'); stdout.Flush(); }
    }
    protected void Bar(string text, string? tooltip = null, string? cls = null, double? percentage = null, string? alt = null) => Write(new BarOut(text, tooltip, cls, percentage, alt), SdkJson.Default.BarOut);
    protected void State(JsonNode data, bool merge = false) => Write(new StateOut("state", data, merge ? true : null), SdkJson.Default.StateOut);
    protected void Notify(string title, string? body = null, string? icon = null, string? open = null) => Write(new NotifyOut("notify", title, body, icon, open), SdkJson.Default.NotifyOut);
    protected void Log(string message, string level = "info") => Write(new LogOut("log", level, message), SdkJson.Default.LogOut);
    protected void Results(int id, List<ProviderItem> items, bool done = true) => Write(new ResultsOut("results", id, items, done), SdkJson.Default.ResultsOut);
    protected void Found(int id, List<FoundItem> items, bool done) => Write(new FoundOut("found", id, items, done), SdkJson.Default.FoundOut);
    protected void Reply(int req, bool ok, string? message = null, JsonNode? data = null) => Write(new ReplyOut("reply", req, ok, message, data), SdkJson.Default.ReplyOut);
    // Admin work: a fixed verb from omawin's list (the manifest's adminVerbs); the shell asks you
    // and answers with OnAdminResult. Never elevate yourself.
    protected void Admin(string verb, string why, params string[] args) => Write(new AdminOut("admin", verb, args, why), SdkJson.Default.AdminOut);

    // --- the loop ----------------------------------------------------------------------------------
    public static int Run<T>(string[] args) where T : PluginBase, new()
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        var plugin = new T();
        if (args.Contains("--selftest")) { try { return plugin.SelfTest(); } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; } }
        var inbox = new System.Collections.Concurrent.BlockingCollection<string?>();
        new Thread(() =>
        {
            using var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            string? line;
            while ((line = stdin.ReadLine()) != null) inbox.Add(line);
            inbox.Add(null); // the shell went away
        }) { IsBackground = true }.Start();

        DateTime? due = null; // the next tick, while TickEvery is set
        while (true)
        {
            if (plugin.TickEvery is { } every) due ??= DateTime.UtcNow + every; else due = null;
            int wait = due is { } d ? Math.Max(0, (int)(d - DateTime.UtcNow).TotalMilliseconds) : -1;
            if (inbox.TryTake(out var line, wait))
            {
                if (line == null) { plugin.OnShutdown(); return 0; }
                try { if (!plugin.Dispatch(line)) return 0; }
                catch (Exception ex) { Console.Error.WriteLine(ex); } // the shell logs stderr; keep going
                continue;
            }
            due = null; // timed out: tick, and schedule the next from now
            try { plugin.OnTick(); } catch (Exception ex) { Console.Error.WriteLine(ex); }
        }
    }

    bool Dispatch(string line)
    {
        if (JsonNode.Parse(line) is not JsonObject m) return true;
        string S(string k) => m[k]?.GetValue<string>() ?? "";
        int I(string k) => m[k]?.GetValue<int>() ?? 0;
        JsonObject O(string k) => m[k] as JsonObject ?? [];
        switch (S("type"))
        {
            case "init":
                Init = JsonSerializer.Deserialize(line, SdkJson.Default.InitMessage)!;
                Settings = Init.Settings;
                Directory.CreateDirectory(Init.DataDir);
                OnInit(); break;
            case "settings": Settings = O("settings"); OnSettings(); break;
            case "click": OnClick(S("button")); break;
            case "wheel": OnWheel(I("delta")); break;
            case "panel": OnPanel(m["open"]?.GetValue<bool>() ?? false, S("name")); break;
            case "action": OnAction(S("name"), O("args")); break;
            case "query": OnQuery(I("id"), S("text")); break;
            case "cancel": OnCancel(I("id")); break;
            case "find": OnFind(I("id"), S("query"), I("limit"), O("filters")); break;
            case "event": OnEvent(S("source"), S("name"), m["data"]); break;
            case "theme": OnTheme(S("name"), m["palette"] as JsonObject); break;
            case "admin-result": OnAdminResult(new AdminResult(S("verb"), m["ok"]?.GetValue<bool>() ?? false, S("message"))); break;
            case "shutdown": OnShutdown(); return false;
        }
        return true;
    }
}
```

`Omawin.Plugin.Tests` feeds lines through `Dispatch` (made `internal` + `InternalsVisibleTo`) and asserts the written lines (redirect `stdout` to a `MemoryStream` through an internal setter).

### 9.5 The example: `plugins/omawin.top/` (8.17, processes in the launcher)

`plugins/omawin.top/manifest.json`:

```json
{
  "schemaVersion": 1,
  "id": "omawin.top",
  "name": "Processes",
  "version": "0.1.0",
  "author": "omawin",
  "license": "MIT",
  "description": "Type `top` in the launcher: what's running, by memory or CPU; Enter focuses it, Ctrl+K ends it.",
  "kinds": ["launcher-provider"],
  "omawin": {
    "command": ["bin/omawin-top.exe"],
    "provider": { "prefix": "top", "title": "Processes", "icon": "󰍛" },
    "permissions": ["processes"],
    "memoryMb": 64
  }
}
```

`plugins/omawin.top/src/Omawin.Top.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>omawin-top</AssemblyName>
    <PublishAot>true</PublishAot>
    <InvariantGlobalization>true</InvariantGlobalization>
    <OptimizationPreference>Size</OptimizationPreference>
    <StripSymbols>true</StripSymbols>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <RuntimeIdentifier Condition="'$(RuntimeIdentifier)' == ''">win-x64</RuntimeIdentifier>
    <!-- bin/ next to the manifest, where its command points (git-ignored). -->
    <PublishDir>..\bin\</PublishDir>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\sdk\Omawin.Plugin\Omawin.Plugin.csproj" />
    <PackageReference Include="Microsoft.Windows.CsWin32" Version="0.3.183" PrivateAssets="all" /> <!-- (check) current version -->
  </ItemGroup>
</Project>
```

`plugins/omawin.top/src/NativeMethods.txt` (CsWin32: one Win32 name per line, typed P/Invoke generated at build):

```
GlobalMemoryStatusEx
GetSystemTimes
OpenProcess
GetProcessTimes
QueryFullProcessImageName
SetForegroundWindow
EnumWindows
GetWindowThreadProcessId
IsWindowVisible
```

`plugins/omawin.top/src/NativeMethods.json`:

```json
{ "$schema": "https://aka.ms/CsWin32.schema.json", "allowMarshaling": false, "public": false }
```

`plugins/omawin.top/src/Program.cs`:

```csharp
using System.Diagnostics;
using System.Text.Json.Nodes;
using Omawin.Plugin;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;

return PluginBase.Run<Top>(args);

// `top` in the launcher: processes by memory (default) or CPU ("top cpu"), filtered by what's typed.
sealed class Top : PluginBase
{
    readonly Dictionary<int, (long Cpu, DateTime At)> lastCpu = [];

    protected override void OnQuery(int id, string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        bool byCpu = words.Remove("cpu");
        var filter = string.Join(' ', words);
        var rows = new List<(Process P, long Mem, double Cpu)>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (filter.Length > 0 && !p.ProcessName.Contains(filter, StringComparison.OrdinalIgnoreCase)) { p.Dispose(); continue; }
                rows.Add((p, p.PrivateMemorySize64, CpuPercent(p)));
            }
            catch { p.Dispose(); } // gone, or not ours to read
        }
        var items = rows.OrderByDescending(r => byCpu ? r.Cpu : r.Mem).Take(40).Select(r => new ProviderItem(
            Id: "pid:" + r.P.Id,
            Title: r.P.ProcessName,
            Subtitle: $"{r.Mem / 1048576} MB · {r.Cpu:0.0}% CPU · pid {r.P.Id}",
            Icon: "󰍛", // 󰍛
            Actions: [new("activate", "Focus"), new("kill", "End task", "ctrl+k", Confirm: true)])).ToList();
        foreach (var r in rows) r.P.Dispose();
        Results(id, items);
    }

    protected override void OnAction(string name, JsonObject args)
    {
        if (args["id"]?.GetValue<string>() is not { } id || !id.StartsWith("pid:") || !int.TryParse(id[4..], out var pid)) return;
        if (name == "kill") { try { using var p = Process.GetProcessById(pid); p.Kill(); } catch (Exception ex) { Notify("Couldn't end it", ex.Message); } }
        else if (name == "activate") Focus(pid);
    }

    // CPU % since the last look at this process (first look: 0).
    unsafe double CpuPercent(Process p)
    {
        using var h = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)p.Id); // (check) generated name
        if (h.IsInvalid) return 0;
        FILETIME c, e, k, u;
        if (!PInvoke.GetProcessTimes(h, out c, out e, out k, out u)) return 0;
        long cpu = Ticks(k) + Ticks(u);
        var now = DateTime.UtcNow;
        double pct = lastCpu.TryGetValue(p.Id, out var before) ? (cpu - before.Cpu) / (double)(now - before.At).Ticks / Environment.ProcessorCount * 100 : 0;
        lastCpu[p.Id] = (cpu, now);
        return Math.Max(0, pct);
    }
    static long Ticks(FILETIME f) => ((long)f.dwHighDateTime << 32) | (uint)f.dwLowDateTime;

    // The first visible top-level window of that process, brought forward (GlazeWM follows focus).
    static unsafe void Focus(int pid)
    {
        HWND found = default;
        PInvoke.EnumWindows((hwnd, _) =>
        {
            uint owner;
            PInvoke.GetWindowThreadProcessId(hwnd, &owner);
            if (owner == pid && PInvoke.IsWindowVisible(hwnd)) { found = hwnd; return false; }
            return true;
        }, 0);
        if (found != default) PInvoke.SetForegroundWindow(found);
    }

    // CI: the provider answers a query for itself.
    public override int SelfTest()
    {
        var me = Process.GetCurrentProcess().ProcessName;
        var mem = new Windows.Win32.System.SystemInformation.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Windows.Win32.System.SystemInformation.MEMORYSTATUSEX>() };
        if (!PInvoke.GlobalMemoryStatusEx(ref mem) || mem.ullTotalPhys == 0) { Console.Error.WriteLine("GlobalMemoryStatusEx"); return 1; }
        using var p = Process.GetCurrentProcess();
        CpuPercent(p);
        Console.Error.WriteLine($"ok: {me}, {mem.ullTotalPhys / 1048576} MB physical");
        return 0;
    }
}
```

*(check)* CsWin32's generated overload names (`OpenProcess_SafeHandle`, the `EnumWindows` delegate shape with `allowMarshaling: false`, `GlobalMemoryStatusEx(ref …)` vs pointer). Fix the calls to what it generates; the point is no hand-written `DllImport`.

### 9.6 Admin verbs (8.0, 2.9)

A plugin asks; the shell queues and shows; you approve once; one short elevated process runs the batch and exits. No standing service, no plugin ever elevating.

**The verb list** (`menu-app/Plugins/AdminQueue.cs`, the single source; `bin/admin-verbs.ps1` mirrors it and is the one that acts):

| Verb | Args (validated) | What it does | Used by |
|---|---|---|---|
| `hosts write` | content hash (content in the batch file) | replaces `%SystemRoot%\System32\drivers\etc\hosts` (old one moved aside `.backup-<timestamp>`) | 8.19 |
| `dns set` | interface index; 1-4 IPv4/IPv6 addresses | `Set-DnsClientServerAddress` | 8.19 |
| `dns reset` | interface index | back to DHCP | 8.19 |
| `defender exclude` | a folder under the user's profile or a Dev Drive | `Add-MpPreference -ExclusionPath` | 8H |
| `feature enable` | one of `VirtualMachinePlatform`, `Microsoft-Windows-Subsystem-Linux`, `Containers-DisposableClientVM` | `Enable-WindowsOptionalFeature -NoRestart` | 3.1, 7.x |
| `longpaths on` | none | `HKLM\…\FileSystem\LongPathsEnabled = 1` | 8H |
| `devmode on` | none | `HKLM\…\AppModelUnlock\AllowDevelopmentWithoutDevLicense = 1` | 8H |
| `usn-index install` / `remove` | none | registers / removes 8.2's index service (the one standing exception, only when you turn 8.2 on) | 8.2 |

```csharp
namespace SystemMenu.Plugins;

// Admin work for first-party plugins: queued, shown, approved together, run by one elevated
// bin\admin-verbs.ps1 that exits when done (docs/roadmap.md 2.9). Never during an update.
public static class AdminQueue
{
    public static readonly Dictionary<string, string> Verbs = new()
    {
        ["hosts write"] = "Replace the hosts file",
        ["dns set"] = "Set a network adapter's DNS servers",
        ["dns reset"] = "Give a network adapter's DNS back to DHCP",
        ["defender exclude"] = "Exclude a folder from Defender's real-time scan",
        ["feature enable"] = "Turn on a Windows feature (may need a restart)",
        ["longpaths on"] = "Allow long file paths",
        ["devmode on"] = "Turn on developer mode",
        ["usn-index install"] = "Install the whole-disk index service",
        ["usn-index remove"] = "Remove the whole-disk index service",
    };

    public sealed record Item(Plugin Plugin, string Verb, string[] Args, string Why, string? Content, Action<string> Reply);
    static readonly List<Item> queue = [];
    public static IReadOnlyList<Item> Pending => queue;

    public static void Request(Plugin p, AdminRequest a, Action<string> reply)
    {
        if (!PluginRegistry.IsFirstPartyDir(p.Dir) || !p.Manifest.Omawin!.AdminVerbs.Contains(a.Verb) || !Verbs.ContainsKey(a.Verb))
        { reply(Protocol.AdminResult(a.Verb, false, "not allowed for this plugin")); return; }
        if (Updater.Running) { reply(Protocol.AdminResult(a.Verb, false, "waiting: not during an update")); return; } // 2.9: an update never prompts (check: how the shell knows)
        // hosts write carries its content in args[0]; the batch file holds it, the command line only its hash.
        string? content = a.Verb == "hosts write" ? a.Args.FirstOrDefault() : null;
        queue.Add(new Item(p, a.Verb, a.Verb == "hosts write" ? [] : a.Args, a.Why, content, reply));
        // The launcher's "Needs admin" page lists the queue: what, why, from which plugin; Enter runs all.
        Bar.Request(["--page", "admin"]);
    }

    // From the "Needs admin" page's Enter: one UAC prompt for the whole queue.
    public static void RunAll()
    {
        if (queue.Count == 0) return;
        var dir = Path.Combine(MenuModel.StateDir, "admin");
        Directory.CreateDirectory(dir);
        var batch = Path.Combine(dir, $"batch-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        var status = batch.Replace(".json", ".status");
        var json = new System.Text.Json.Nodes.JsonArray(queue.Select(i => (System.Text.Json.Nodes.JsonNode)new System.Text.Json.Nodes.JsonObject
        {
            ["verb"] = i.Verb,
            ["args"] = new System.Text.Json.Nodes.JsonArray(i.Args.Select(a => (System.Text.Json.Nodes.JsonNode)a).ToArray()),
            ["content"] = i.Content,
            ["plugin"] = i.Plugin.Id,
        }).ToArray());
        File.WriteAllText(batch, json.ToJsonString());
        // The hash goes on the elevated command line, which the UAC dialog shows: the elevated
        // script refuses a batch file changed after you approved.
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(batch)));
        var items = queue.ToList();
        queue.Clear();
        try
        {
            var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell.exe",
                $"-NoProfile -ExecutionPolicy Bypass -File \"{Path.Combine(MenuModel.Repo, @"bin\admin-verbs.ps1")}\" -Batch \"{batch}\" -Sha256 {hash} -Status \"{status}\"")
            { UseShellExecute = true, Verb = "runas", WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden })!;
            Task.Run(() => { p.WaitForExit(); Bar.Request(["--admin-done", status]); }); // HandleRequest -> Finish(status, items)
        }
        catch (System.ComponentModel.Win32Exception) // declined: everything else keeps working
        {
            foreach (var i in items) i.Reply(Protocol.AdminResult(i.Verb, false, "declined"));
        }
    }

    // Each status line: "<index>\t<ok|fail>\t<text>", as install/admin-batch.ps1 writes its own.
    public static void Finish(string status, List<Item> items)
    {
        var lines = File.Exists(status) ? File.ReadAllLines(status) : [];
        for (int n = 0; n < items.Count; n++)
        {
            var row = lines.Select(l => l.Split('\t')).FirstOrDefault(c => c.Length >= 3 && c[0] == n.ToString());
            items[n].Reply(Protocol.AdminResult(items[n].Verb, row?[1] == "ok", row?[2] ?? "didn't run"));
        }
    }
}
```

(Keep `items` alongside the status path in a field rather than passing it through a request string; the sketch's `--admin-done` hop is only to get back on the UI thread, which `DispatcherQueue.TryEnqueue` does more simply.)

`bin/admin-verbs.ps1` (Windows PowerShell 5.1, like `install/admin-batch.ps1`):

```powershell
<#
.SYNOPSIS
    Runs an approved batch of admin verbs for omawin's first-party plugins, elevated, then exits.
.DESCRIPTION
    Started by the menu app (Plugins/AdminQueue.cs) with one UAC prompt. Takes nothing but the
    batch file, its SHA-256 (from the command line the prompt showed) and a status file. Every
    verb is from a fixed list, every argument checked against its shape; anything else is refused.
    May run as another account than the one that asked (a standard user's prompt takes an admin's
    password), so it only does what's machine-wide. Status lines: "<index>`t<ok|fail>`t<text>".
#>
param([Parameter(Mandatory)][string]$Batch, [Parameter(Mandatory)][string]$Sha256, [Parameter(Mandatory)][string]$Status)
$ErrorActionPreference = 'Stop'

function Say([int]$I, [string]$State, [string]$Text) { Add-Content -LiteralPath $Status -Value "$I`t$State`t$($Text -replace '[\r\n\t]+', ' ')" }

if ((Get-FileHash -LiteralPath $Batch -Algorithm SHA256).Hash -ne $Sha256) { Say -1 'fail' 'the batch changed after it was approved'; exit 1 }
$items = Get-Content -LiteralPath $Batch -Raw | ConvertFrom-Json
$profileRoot = [Environment]::GetFolderPath('UserProfile')  # the elevated account's; folders are checked to exist and be local, below
$i = -1
foreach ($item in @($items)) {
    $i++
    $a = @($item.args)
    try {
        switch ($item.verb) {
            'hosts write' {
                $hosts = "$env:SystemRoot\System32\drivers\etc\hosts"
                $text = [string]$item.content
                # Only hosts lines: comments, blanks, "<ip> <name>..."; nothing else.
                foreach ($line in $text -split "`n") { if ($line.Trim() -and $line -notmatch '^\s*(#.*|[0-9a-fA-F:.]+\s+[\w.-]+(\s+[\w.-]+)*\s*(#.*)?)$') { throw "not a hosts line: $line" } }
                Copy-Item -LiteralPath $hosts "$hosts.backup-$(Get-Date -Format yyyyMMddHHmmss)"
                [IO.File]::WriteAllText($hosts, $text, [Text.Encoding]::ASCII)
            }
            'dns set' {
                if ($a[0] -notmatch '^\d+$') { throw 'interface index' }
                $servers = @($a | Select-Object -Skip 1)
                if ($servers.Count -lt 1 -or $servers.Count -gt 4) { throw '1 to 4 servers' }
                foreach ($s in $servers) { $ip = $null; if (-not [Net.IPAddress]::TryParse($s, [ref]$ip)) { throw "not an address: $s" } }
                Set-DnsClientServerAddress -InterfaceIndex ([int]$a[0]) -ServerAddresses $servers
            }
            'dns reset' { if ($a[0] -notmatch '^\d+$') { throw 'interface index' }; Set-DnsClientServerAddress -InterfaceIndex ([int]$a[0]) -ResetServerAddresses }
            'defender exclude' {
                $path = [IO.Path]::GetFullPath($a[0])
                if (-not (Test-Path -LiteralPath $path -PathType Container)) { throw 'not a folder' }
                if ($path -match '^[A-Za-z]:\\?$' -or $path -like "$env:SystemRoot*" -or $path -like "$env:ProgramFiles*") { throw 'a drive root or a system folder: refused' }
                Add-MpPreference -ExclusionPath $path
            }
            'feature enable' {
                if ($a[0] -notin 'VirtualMachinePlatform', 'Microsoft-Windows-Subsystem-Linux', 'Containers-DisposableClientVM') { throw "feature $($a[0]) isn't on the list" }
                $r = Enable-WindowsOptionalFeature -Online -FeatureName $a[0] -NoRestart -All
                if ($r.RestartNeeded) { Say $i 'ok' 'done; needs a restart'; continue }
            }
            'longpaths on' { Set-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem' LongPathsEnabled 1 -Type DWord }
            'devmode on' {
                $k = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock'
                New-Item $k -Force | Out-Null; Set-ItemProperty $k AllowDevelopmentWithoutDevLicense 1 -Type DWord
            }
            'usn-index install' { throw 'not built yet (8.2)' }
            'usn-index remove' { throw 'not built yet (8.2)' }
            default { throw "unknown verb '$($item.verb)'" }
        }
        Say $i 'ok' 'done'
    }
    catch { Say $i 'fail' $_.Exception.Message }
}
```

*(check)* Running an elevated script from the user-writable repo: the user (or anything running as them) could edit `bin/admin-verbs.ps1` before the prompt. That's the same trust as the user approving any UAC prompt for their own files; when CI signing lands (release-signing.md), the verbs move into a signed `omawin-admin.exe` in the bundle and the menu app checks its signature before starting it.

## 10. Omarchy plugins unchanged: the compatibility layer (4.9)

### 10.1 Layer 1: Omarchy's commands, in Arch (`wsl/omarchy-shims/`)

Installed by `wsl/setup.sh` into `/usr/local/lib/omawin/shims/` and put first on `PATH` (`/etc/profile.d/omawin-shims.sh`), so they win over nothing (Omarchy's own commands aren't in our Arch) and never over a real `hyprctl` (there's no Hyprland in WSL). LF line endings (`.gitattributes` keeps `wsl/` scripts LF: *(check)* it covers `wsl/omarchy-shims/*` without an extension; add a rule if not).

`wsl/omarchy-shims/_omawin` (sourced by the others):

```bash
#!/bin/bash
# Shared by omawin's stand-ins for Omarchy's commands: find omawin.exe and glazewm.exe on the
# Windows side (WSL interop), cached, since asking cmd.exe costs ~100 ms.

omawin_cache="${XDG_CACHE_HOME:-$HOME/.cache}/omawin"

omawin_win_home() {
  local cached="$omawin_cache/windows-home"
  if [[ -s $cached ]]; then
    cat "$cached"
  else
    mkdir -p "$omawin_cache"
    wslpath "$(cmd.exe /c 'echo %USERPROFILE%' 2>/dev/null | tr -d '\r')" | tee "$cached"
  fi
}

omawin_exe() {
  printf '%s/.local/bin/omawin.exe' "$(omawin_win_home)"
}

omawin_glazewm() {
  local found
  if found=$(command -v glazewm.exe); then
    printf '%s' "$found"
  elif [[ -x "$(omawin_win_home)/AppData/Local/glazewm/cli/glazewm.exe" ]]; then  # the per-user build (2.9) (check: its path)
    printf '%s' "$(omawin_win_home)/AppData/Local/glazewm/cli/glazewm.exe"
  else
    printf '%s' "/mnt/c/Program Files/glzr.io/cli/glazewm.exe"
  fi
}

# omawin shell <method> <args...>: one argument per line in the request, so spaces survive.
omawin_shell() {
  "$(omawin_exe)" shell "$@"
}
```

`wsl/omarchy-shims/omarchy-notification-send` (notify-send's options, as Omarchy's wrapper takes them):

```bash
#!/bin/bash
# omawin's stand-in for Omarchy's omarchy-notification-send: a Windows toast from omawin's shell.
source /usr/local/lib/omawin/shims/_omawin

summary=""
body=""
while (($#)); do
  case $1 in
    -u | --urgency | -a | --app-name | -i | --icon | -t | --expire-time | -c | --category | -h | --hint | -g | --glyph | -r | --replace-id)
      shift 2
      ;;
    --urgency=* | --app-name=* | --icon=* | --expire-time=* | --category=* | --hint=*)
      shift
      ;;
    -e | --transient | -p | --print-id | -w | --wait)
      shift
      ;;
    --)
      shift
      break
      ;;
    *)
      break
      ;;
  esac
done
summary=${1:-}
body=${2:-}
[[ -z $summary ]] && exit 0
omawin_shell notify "$summary" "$body"
```

`notify-send` gets the same script (a symlink), since several plugins call it directly.

`wsl/omarchy-shims/omarchy-shell`:

```bash
#!/bin/bash
# omawin's stand-in for omarchy-shell: Omarchy's shell IPC methods, answered by omawin's shell.
source /usr/local/lib/omawin/shims/_omawin

[[ ${1:-} == "shell" ]] && shift
method=${1:-}
shift || true

case $method in
  summon) omawin_shell summon "$@" ;;
  toggle) omawin_shell toggle "$@" ;;
  call) omawin_shell call "$@" ;;
  rescanPlugins) omawin_shell reload ;;
  listPlugins) "$(omawin_exe)" plugin list --json ;;
  enablePlugin) "$(omawin_exe)" plugin enable "$@" --yes ;;
  disablePlugin) "$(omawin_exe)" plugin disable "$@" ;;
  *)
    echo "omarchy-shell (omawin): '$method' isn't supported here" >&2
    exit 1
    ;;
esac
```

`wsl/omarchy-shims/xdg-open`:

```bash
#!/bin/bash
# omawin's stand-in for xdg-open: https links and existing files open on Windows; nothing else.
source /usr/local/lib/omawin/shims/_omawin

target=${1:-}
if [[ $target =~ ^https:// ]]; then
  omawin_shell open "$target"
elif [[ -e $target ]]; then
  omawin_shell open "$(wslpath -w "$(realpath "$target")")"
else
  echo "xdg-open (omawin): only https links and files: $target" >&2
  exit 1
fi
```

Only installed when Arch has no real `xdg-open` (a desktop's `xdg-utils` in WSLg works on its own; *(check)* whether ours should win anyway, since WSLg's opens a Linux browser).

`wsl/omarchy-shims/hyprctl` (the common reads, answered from GlazeWM; a few dispatches; binds become proposed keys):

```bash
#!/bin/bash
# omawin's stand-in for hyprctl: the reads plugins use, answered from GlazeWM's own queries, in
# Hyprland's JSON shapes. Keys are never bound here: `keyword bind` becomes a proposed key you
# accept in omawin (AGENTS.md: keys never change by guesswork).
source /usr/local/lib/omawin/shims/_omawin

json=0
args=()
for a in "$@"; do
  if [[ $a == "-j" || $a == "--json" ]]; then
    json=1
  else
    args+=("$a")
  fi
done
set -- "${args[@]}"

glaze() {
  # glazewm query <what> prints {"data": {...}, "success": true, ...} (check: the CLI's exact output)
  "$(omawin_glazewm)" query "$@" | tr -d '\r'
}

# Hyprland numbers workspaces; GlazeWM names them. Numeric names map to ids; others get 100+.
monitors_json() {
  glaze monitors | jq '[.data.monitors | to_entries[] | .key as $i | .value | {
      id: $i, name: ("MON-" + ($i | tostring)), description: "", make: "", model: "",
      width, height, x, y, scale: .scaleFactor, focused: (.hasFocus // false),
      activeWorkspace: ((.children[] | select(.isDisplayed) | {id: ((.name | tonumber?) // 100), name}) // {id: 0, name: ""}),
      reserved: [0, 0, 0, 0], transform: 0, disabled: false }]'
}

workspaces_json() {
  glaze monitors | jq '[.data.monitors | to_entries[] | .key as $i | .value.children[] | select(.type == "workspace") | {
      id: ((.name | tonumber?) // 100), name, monitor: ("MON-" + ($i | tostring)), monitorID: $i,
      windows: ([.. | objects | select(.type? == "window")] | length), hasfullscreen: false,
      lastwindow: "", lastwindowtitle: "" }]'
}

activewindow_json() {
  glaze focused | jq '.data.focused | if .type == "window" then {
      address: ("0x" + (.handle | tostring)), mapped: true, hidden: false,
      at: [.x, .y], size: [.width, .height], workspace: {id: 0, name: ""},
      floating: (.state.type == "floating"), fullscreen: (.state.type == "fullscreen"),
      class: (.processName // ""), title: (.title // ""), initialClass: (.processName // ""), initialTitle: (.title // ""), pid: 0 }
    else {} end'
}

# getoption: colours from omawin's palette (the same file the theme switch writes for Omarchy).
getoption_json() {
  local theme="$HOME/.config/omarchy/current/theme/colors.toml"
  local accent
  accent=$(sed -n 's/^accent *= *"#\?\([0-9a-fA-F]\{6\}\)".*/\1/p' "$theme" 2>/dev/null)
  case $1 in
    general:col.active_border) printf '{"option":"%s","custom":"ff%s ff%s 45deg","set":true}\n' "$1" "$accent" "$accent" ;;
    general:col.inactive_border) printf '{"option":"%s","custom":"ff595959","set":true}\n' "$1" ;;
    *) printf '{"option":"%s","int":0,"float":0.0,"str":"","set":false}\n' "$1" ;;
  esac
}

dispatch() {
  local what=$1
  shift
  case $what in
    workspace) "$(omawin_glazewm)" command focus --workspace "$1" ;;
    movetoworkspace | movetoworkspacesilent) "$(omawin_glazewm)" command move --workspace "${1%%,*}" ;;
    killactive) "$(omawin_glazewm)" command close ;;
    togglefloating) "$(omawin_glazewm)" command toggle-floating ;;
    fullscreen) "$(omawin_glazewm)" command toggle-fullscreen ;;
    exec) setsid -f sh -c "$*" >/dev/null 2>&1 ;;  # runs in Linux, where the plugin is
    *)
      echo "hyprctl (omawin): dispatch $what isn't supported" >&2
      return 1
      ;;
  esac
  echo ok
}

case ${1:-} in
  monitors) ((json)) && monitors_json || monitors_json | jq -r '.[] | "Monitor \(.name) (ID \(.id)):\n\t\(.width)x\(.height) at \(.x)x\(.y)\n\tfocused: \(if .focused then "yes" else "no" end)\n"' ;;
  workspaces) ((json)) && workspaces_json || workspaces_json | jq -r '.[] | "workspace ID \(.id) (\(.name)) on monitor \(.monitor):\n\twindows: \(.windows)\n"' ;;
  activeworkspace) workspaces_json | jq '.[0]' ;;  # (check) pick the focused one from `glaze focused`'s parent
  activewindow) ((json)) && activewindow_json || activewindow_json | jq -r '"Window \(.address // "none") -> \(.title // ""):\n\tclass: \(.class // "")\n"' ;;
  getoption) getoption_json "$2" ;;
  dispatch) shift; dispatch "$@" ;;
  keyword)
    if [[ ${2:-} == bind* ]]; then
      # "bind = SUPER SHIFT, W, exec, foo" -> a proposal in omawin's Plugins page
      omawin_shell propose-key "${*:3}" "linux-exec"
      echo ok
    else
      echo ok  # other keywords (decoration, animations) mean nothing here
    fi
    ;;
  version) ((json)) && echo '{"branch":"omawin","commit":"","version":"0.0.0-omawin","tag":"omawin"}' || echo "Hyprland stand-in (omawin)" ;;
  reload) echo ok ;;
  *)
    echo "hyprctl (omawin): '${1:-}' isn't supported" >&2
    exit 1
    ;;
esac
```

`wsl/omarchy-shims/wl-copy` / `wl-paste`: only if WSLg's real ones are missing; then `clip.exe` (copy; UTF-16 needs `iconv -t UTF-16LE` *(check)*) and `powershell.exe -NoProfile -Command Get-Clipboard` (paste).

**The theme where Omarchy plugins read it.** `themes/set-theme.ps1` adds, after its other steps (only when Arch is set up; skipped quietly otherwise, and never blocking the switch: started with `Start-Process -WindowStyle Hidden`):

```powershell
# Omarchy plugins in Linux read the current theme from ~/.config/omarchy/current/theme/ (Omarchy's
# own layout): give them omawin's palette there, in Omarchy's format, which is ours already.
$distro = (Get-Content "$Repo\bin\defaults.txt" | Where-Object { $_ -match '^linux\s' }) -replace '^linux\s+', ''  # (check) defaults.txt format
if ($distro -and (wsl.exe -l -q | ForEach-Object { $_ -replace "`0", '' }) -contains $distro) {
    $colors = Get-Content (Join-Path $Repo "themes\colors\$Name.toml") -Raw
    $script = "mkdir -p ~/.config/omarchy/current/theme && cat > ~/.config/omarchy/current/theme/colors.toml.tmp && mv ~/.config/omarchy/current/theme/colors.toml.tmp ~/.config/omarchy/current/theme/colors.toml && printf %s '$Name' > ~/.config/omarchy/current/theme.name"
    $colors | wsl.exe -d $distro -- sh -c $script
}
```

`wsl/setup.sh` (the shims step):

```bash
install_omarchy_shims() {
  local from="$OMAWIN_REPO/wsl/omarchy-shims" to=/usr/local/lib/omawin/shims
  sudo mkdir -p "$to"
  sudo install -m 0755 "$from"/* "$to"/
  sudo ln -sf "$to/omarchy-notification-send" "$to/notify-send"
  printf 'case ":$PATH:" in *":%s:"*) ;; *) PATH="%s:$PATH" ;; esac\n' "$to" "$to" | sudo tee /etc/profile.d/omawin-shims.sh >/dev/null
}
```

*(check)* whether `wsl.exe -d <distro> -- cmd` (non-login) picks up `/etc/profile.d`; plugins run that way, so `PluginProcess` may need `bash -lc` for `side: linux`, or the shims dir also added to `/etc/environment`.

### 10.2 Layer 2A: the WSLg prototype (4.9.3)

No new code beyond a `shell.qml`: Quickshell from Omarchy's repositories in Arch (`pacman -S quickshell` *(check: package name in Omarchy's repo)*), Omarchy's `shell/Commons` and `shell/Ui` copied into `~/.config/omawin-qs/`, and a `shell.qml` that does what `PluginRegistry.qml` does for one plugin (load its `entryPoints`, inject `shell` and `manifest`, answer `IpcHandler` calls). A `PanelWindow` falls back to a plain window under WSLg; a GlazeWM window rule floats `omawin-qs` windows and places them under the bar. Its purpose is to prove Omadoro and Omastonk run unchanged, then it's replaced by 10.3.

### 10.3 Layer 2B: `omawin-qs.exe`, the native QML host (4.9.4)

A Qt 6 program providing the module subset plugins import, implemented for Windows. **It is itself a plugin process**: the menu app starts `omawin-qs.exe --plugin <dir>` with the usual Job Object and JSON lines, so registry, enable/disable, hot reload, logs, `profile ram` and panels-from-the-bar all come for free. A QML plugin's manifest needs no `omawin` block; the registry treats "has `entryPoints`, no `omawin.command`, kind `bar-widget`/`panel`/`overlay`/`service`" as "run in the QML host" once the host is installed (downloaded on first need by `bin/install-binaries.ps1`).

Protocol additions it uses (already in §3.1): `slot` / `slot-rect` (the bar reserves a cell, `CommandWidget` style `slot`, and tells the host where it is; the host keeps a borderless widget window exactly over it), `panel` open/close (from the bar cell's click), `theme`, `settings`.

```
qs-host/
  CMakeLists.txt
  src/main.cpp                 the app: reads JSON lines, loads the plugin, routes messages
  src/Protocol.h/.cpp          stdin reader thread -> Qt signals; stdout writer
  src/quickshell/Quickshell.h/.cpp        `Quickshell` singleton (env, execDetached, shellDir, screens)
  src/quickshell/PanelWindow.h/.cpp       PanelWindow / FloatingWindow: frameless, topmost, anchored
  src/quickshell/Scope.h                  Scope, ShellRoot, Singleton (QObject containers), Variants (per screen)
  src/io/Process.h/.cpp                   Quickshell.Io Process (+ SplitParser, StdioCollector)
  src/io/FileView.h/.cpp                  Quickshell.Io FileView
  src/hyprland/Hyprland.h/.cpp            Quickshell.Hyprland, from GlazeWM's websocket IPC
  qml/qs/Commons/, qml/qs/Ui/             Omarchy's MIT QML, vendored at a pinned commit (NOTICE), with generated qmldir
  qml/Host.qml                            Omarchy's plugin host behaviour for one plugin
  cmake/qmldir.cmake                      generates qmldir files for qs/Commons and qs/Ui
```

`qs-host/CMakeLists.txt`:

```cmake
cmake_minimum_required(VERSION 3.24)
project(omawin-qs VERSION 0.1 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
find_package(Qt6 6.6 REQUIRED COMPONENTS Core Gui Qml Quick WebSockets)
qt_standard_project_setup(REQUIRES 6.6)

qt_add_executable(omawin-qs WIN32 src/main.cpp src/Protocol.cpp src/Protocol.h)

# Each Quickshell module name plugins import, as a static QML module linked into the exe.
qt_add_qml_module(qs_core
    URI Quickshell
    VERSION 1.0
    STATIC
    SOURCES src/quickshell/Quickshell.h src/quickshell/Quickshell.cpp
            src/quickshell/PanelWindow.h src/quickshell/PanelWindow.cpp
            src/quickshell/Scope.h
)
qt_add_qml_module(qs_io
    URI Quickshell.Io
    VERSION 1.0
    STATIC
    SOURCES src/io/Process.h src/io/Process.cpp src/io/FileView.h src/io/FileView.cpp
)
qt_add_qml_module(qs_hyprland
    URI Quickshell.Hyprland
    VERSION 1.0
    STATIC
    SOURCES src/hyprland/Hyprland.h src/hyprland/Hyprland.cpp
)
target_link_libraries(qs_hyprland PRIVATE Qt6::WebSockets)

target_link_libraries(omawin-qs PRIVATE
    Qt6::Quick Qt6::Qml Qt6::WebSockets
    qs_coreplugin qs_ioplugin qs_hyprlandplugin)

# Omarchy's qs.Commons / qs.Ui: plain QML folders. Quickshell maps `qs.X` to <shell dir>/X and
# writes qmldir files itself; plain Qt needs them, generated from the folders at build time
# (`pragma Singleton` files become `singleton Name 1.0 Name.qml`).
include(cmake/qmldir.cmake)
omawin_generate_qmldir(${CMAKE_CURRENT_SOURCE_DIR}/qml/qs/Commons qs.Commons ${CMAKE_CURRENT_BINARY_DIR}/qml/qs/Commons)
omawin_generate_qmldir(${CMAKE_CURRENT_SOURCE_DIR}/qml/qs/Ui qs.Ui ${CMAKE_CURRENT_BINARY_DIR}/qml/qs/Ui)

install(TARGETS omawin-qs RUNTIME DESTINATION .)
install(DIRECTORY ${CMAKE_CURRENT_BINARY_DIR}/qml/ DESTINATION qml)
install(FILES qml/Host.qml DESTINATION qml)
# CI runs windeployqt on the installed exe (Qt's DLLs, LGPL, shipped as DLLs: NOTICE).
```

`cmake/qmldir.cmake` (sketch): `file(GLOB *.qml)`; for each, `file(STRINGS … REGEX "^pragma Singleton")` → `singleton <Name> 1.0 <Name>.qml`, else `<Name> 1.0 <Name>.qml`; `.js` files → `<Name> 1.0 <Name>.js` *(check: Omarchy's `qs.Commons` may also expect `qs.services`-style imports between its folders; the scan of Commons' own imports lists what else must exist)*.

`src/io/Process.h` (the parts plugins use; Quickshell's names):

```cpp
#pragma once
#include <QObject>
#include <QProcess>
#include <QQmlEngine>
#include <QVariantMap>

class DataStreamParser : public QObject {
  Q_OBJECT
  QML_ANONYMOUS
public:
  using QObject::QObject;
  virtual void feed(const QByteArray& data) = 0;
  virtual void finish() {}
};

// SplitParser { splitMarker: "\n"; onRead: data => ... }
class SplitParser : public DataStreamParser {
  Q_OBJECT
  QML_ELEMENT
  Q_PROPERTY(QString splitMarker MEMBER m_marker NOTIFY splitMarkerChanged)
public:
  using DataStreamParser::DataStreamParser;
  void feed(const QByteArray& data) override;   // buffer, split on marker, emit read(QString) per piece
  void finish() override;                       // flush the remainder
signals:
  void read(QString data);
  void splitMarkerChanged();
private:
  QString m_marker = "\n";
  QByteArray m_buffer;
};

// StdioCollector { onStreamFinished: text }
class StdioCollector : public DataStreamParser {
  Q_OBJECT
  QML_ELEMENT
  Q_PROPERTY(QString text READ text NOTIFY streamFinished)
public:
  using DataStreamParser::DataStreamParser;
  void feed(const QByteArray& data) override { m_data += data; }
  void finish() override { emit streamFinished(); }
  QString text() const { return QString::fromUtf8(m_data); }
signals:
  void streamFinished();
private:
  QByteArray m_data;
};

// Quickshell.Io Process. Windows programs run directly; anything else (sh, mkdir, hey, python3)
// runs in the user's Arch: wsl.exe -d <distro> -- <command...>. Every program run is logged
// (it feeds `omawin plugin scan`'s rules).
class Process : public QObject {
  Q_OBJECT
  QML_ELEMENT
  Q_PROPERTY(QStringList command READ command WRITE setCommand NOTIFY commandChanged)
  Q_PROPERTY(bool running READ running WRITE setRunning NOTIFY runningChanged)
  Q_PROPERTY(QString workingDirectory MEMBER m_cwd NOTIFY workingDirectoryChanged)
  Q_PROPERTY(QVariantMap environment MEMBER m_env NOTIFY environmentChanged)
  Q_PROPERTY(bool clearEnvironment MEMBER m_clearEnv NOTIFY clearEnvironmentChanged)
  Q_PROPERTY(bool stdinEnabled MEMBER m_stdin NOTIFY stdinEnabledChanged)
  Q_PROPERTY(DataStreamParser* stdout READ stdoutParser WRITE setStdoutParser NOTIFY stdoutChanged)
  Q_PROPERTY(DataStreamParser* stderr READ stderrParser WRITE setStderrParser NOTIFY stderrChanged)
  Q_PROPERTY(qint64 processId READ processId NOTIFY runningChanged)
public:
  explicit Process(QObject* parent = nullptr);
  Q_INVOKABLE void write(const QString& data);
  Q_INVOKABLE void signal(int sig);              // SIGTERM/SIGKILL -> terminate/kill
  Q_INVOKABLE void startDetached();
  // ... getters/setters ...
signals:
  void started();
  void exited(int exitCode, int exitStatus);
  void commandChanged(); void runningChanged(); void workingDirectoryChanged(); void environmentChanged();
  void clearEnvironmentChanged(); void stdinEnabledChanged(); void stdoutChanged(); void stderrChanged();
private:
  void start();
  static bool isWindowsProgram(const QString& program); // ends in .exe/.cmd/.bat, or QStandardPaths::findExecutable finds it on Windows' PATH and not a Linux name
  QProcess m_process;
  QStringList m_command;
  QString m_cwd;
  QVariantMap m_env;
  bool m_clearEnv = false, m_stdin = false, m_running = false;
  DataStreamParser* m_stdoutParser = nullptr;
  DataStreamParser* m_stderrParser = nullptr;
};
```

`src/io/Process.cpp` (the routing):

```cpp
void Process::start() {
  if (m_command.isEmpty()) return;
  QString program = m_command.first();
  QStringList args = m_command.mid(1);
  if (!isWindowsProgram(program)) {
    // Linux: in the plugin's distro, from the plugin's folder mapped into Linux.
    QStringList wsl{"-d", Host::distro()};
    if (!m_cwd.isEmpty()) wsl << "--cd" << Paths::toWindows(m_cwd);   // a Linux path from the plugin, as Windows sees it
    wsl << "--" << program << args;
    if (!m_env.isEmpty()) {                                          // env vars: through `env`, since WSLENV can't carry arbitrary names
      QStringList envArgs{"env"};
      for (auto it = m_env.cbegin(); it != m_env.cend(); ++it) envArgs << it.key() + "=" + it.value().toString();
      wsl = QStringList{"-d", Host::distro(), "--"} + envArgs + QStringList{program} + args;
    }
    program = "wsl.exe";
    args = wsl;
  }
  Host::log("process", program + " " + args.join(' '));
  m_process.setProcessChannelMode(QProcess::SeparateChannels);
  m_process.start(program, args);
}
```

`src/io/FileView.h`:

```cpp
// Quickshell.Io FileView: path, text(), setText(), watchChanges, atomicWrites. Linux paths
// ($HOME, $XDG_STATE_HOME/...) map into Arch's home over \\wsl.localhost\<distro>\ (slow but
// small files), so a plugin's state lands where it would on Omarchy.
class FileView : public QObject {
  Q_OBJECT
  QML_ELEMENT
  Q_PROPERTY(QString path READ path WRITE setPath NOTIFY pathChanged)
  Q_PROPERTY(bool watchChanges MEMBER m_watch NOTIFY watchChangesChanged)
  Q_PROPERTY(bool atomicWrites MEMBER m_atomic NOTIFY atomicWritesChanged)
  Q_PROPERTY(bool printErrors MEMBER m_printErrors NOTIFY printErrorsChanged)
  Q_PROPERTY(bool blockLoading MEMBER m_block NOTIFY blockLoadingChanged)
  Q_PROPERTY(bool preload MEMBER m_preload NOTIFY preloadChanged)
public:
  Q_INVOKABLE QString text();
  Q_INVOKABLE QByteArray data();
  Q_INVOKABLE void setText(const QString& text);   // QSaveFile when atomicWrites
  Q_INVOKABLE void reload();
signals:
  void loaded();
  void loadFailed(int error);
  void fileChanged();       // QFileSystemWatcher (over \\wsl.localhost: polling fallback, check)
  void saved();
  void pathChanged(); void watchChangesChanged(); void atomicWritesChanged(); void printErrorsChanged(); void blockLoadingChanged(); void preloadChanged();
  // ...
};
```

`src/quickshell/PanelWindow.h`:

```cpp
// PanelWindow: on Wayland a layer-shell surface; here a frameless, topmost, tool window
// (WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE until focused), placed by its anchors and margins on its
// screen, under the bar's height (exclusiveZone is the bar's job, ignored). GlazeWM leaves it
// alone by a window rule on the process name omawin-qs.exe.
class Anchors : public QObject {
  Q_OBJECT
  QML_ANONYMOUS
  Q_PROPERTY(bool top MEMBER top NOTIFY changed) Q_PROPERTY(bool bottom MEMBER bottom NOTIFY changed)
  Q_PROPERTY(bool left MEMBER left NOTIFY changed) Q_PROPERTY(bool right MEMBER right NOTIFY changed)
public:
  bool top = false, bottom = false, left = false, right = false;
signals:
  void changed();
};

class PanelWindow : public QQuickWindow {
  Q_OBJECT
  QML_ELEMENT
  Q_PROPERTY(Anchors* anchors READ anchors CONSTANT)
  Q_PROPERTY(Margins* margins READ margins CONSTANT)
  Q_PROPERTY(int exclusiveZone MEMBER m_exclusive NOTIFY exclusiveZoneChanged)
  Q_PROPERTY(qreal implicitWidth MEMBER m_iw NOTIFY implicitSizeChanged)
  Q_PROPERTY(qreal implicitHeight MEMBER m_ih NOTIFY implicitSizeChanged)
  Q_PROPERTY(QObject* screen READ screenObject WRITE setScreenObject NOTIFY screenChanged) // Quickshell's ShellScreen
  // QQuickWindow already has `data` (the default property), `color` and `visible`. (check)
public:
  explicit PanelWindow(QWindow* parent = nullptr);
protected:
  void showEvent(QShowEvent*) override;   // SetWindowLongPtr ex-styles, then place()
private:
  void place();                           // anchors + margins + implicit size -> geometry on the screen; top-anchored panels start below the bar
  // ...
};
```

`src/hyprland/Hyprland.h`:

```cpp
// Quickshell.Hyprland from GlazeWM's IPC (ws://127.0.0.1:6123, as the menu app's GlazeWm.cs):
// workspaces, monitors, the focused ones, dispatch() translated to GlazeWM commands, and
// GlobalShortcut as a proposed key in omawin (never bound silently).
class HyprlandWorkspace : public QObject {
  Q_OBJECT
  QML_ANONYMOUS
  Q_PROPERTY(int id MEMBER id NOTIFY changed) Q_PROPERTY(QString name MEMBER name NOTIFY changed)
  Q_PROPERTY(bool active MEMBER active NOTIFY changed) Q_PROPERTY(bool focused MEMBER focused NOTIFY changed)
  Q_PROPERTY(HyprlandMonitor* monitor MEMBER monitor NOTIFY changed)
public:
  Q_INVOKABLE void activate();   // focus --workspace <name>
  int id = 0; QString name; bool active = false, focused = false; HyprlandMonitor* monitor = nullptr;
signals:
  void changed();
};

class Hyprland : public QObject {
  Q_OBJECT
  QML_ELEMENT
  QML_SINGLETON
  Q_PROPERTY(QQmlListProperty<HyprlandWorkspace> workspaces READ workspaces NOTIFY workspacesChanged) // Quickshell exposes an ObjectModel with .values (check: plugins use `Hyprland.workspaces.values`)
  Q_PROPERTY(QQmlListProperty<HyprlandMonitor> monitors READ monitors NOTIFY monitorsChanged)
  Q_PROPERTY(HyprlandWorkspace* focusedWorkspace READ focusedWorkspace NOTIFY focusedWorkspaceChanged)
  Q_PROPERTY(HyprlandMonitor* focusedMonitor READ focusedMonitor NOTIFY focusedMonitorChanged)
public:
  Q_INVOKABLE void dispatch(const QString& request);  // "workspace 3" -> "focus --workspace 3", as the hyprctl stand-in
  Q_INVOKABLE void refreshWorkspaces();
  Q_INVOKABLE void refreshMonitors();
signals:
  void rawEvent(QObject* event);                     // GlazeWM events mapped to Hyprland's names where one exists
  void workspacesChanged(); void monitorsChanged(); void focusedWorkspaceChanged(); void focusedMonitorChanged();
private:
  QWebSocket m_socket;                               // sub --events ..., query monitors / focused, as GlazeWm.cs
};

class GlobalShortcut : public QObject {
  Q_OBJECT
  QML_ELEMENT
  Q_PROPERTY(QString name MEMBER name) Q_PROPERTY(QString appid MEMBER appid) Q_PROPERTY(QString description MEMBER description)
public:
  // On completion: {"type":"run","command":["omawin","shell","propose-key",...]} to the shell; when the
  // accepted key fires, the shell sends {"type":"action","name":"shortcut:<name>"} and this emits pressed().
signals:
  void pressed();
  void released();
};
```

`src/main.cpp`:

```cpp
#include <QGuiApplication>
#include <QQmlApplicationEngine>
#include <QQmlContext>
#include "Protocol.h"

// omawin-qs --plugin <dir>: one Omarchy QML plugin, unchanged, as an omawin plugin process.
int main(int argc, char* argv[]) {
  QGuiApplication app(argc, argv);
  app.setQuitOnLastWindowClosed(false);
  const QString dir = app.arguments().value(app.arguments().indexOf("--plugin") + 1);

  Protocol protocol;                       // stdin lines -> signals (init, panel, settings, theme, slot-rect, action, shutdown)
  QQmlApplicationEngine engine;
  engine.addImportPath(QCoreApplication::applicationDirPath() + "/qml"); // qs.Commons, qs.Ui
  engine.rootContext()->setContextProperty("omawinProtocol", &protocol);
  engine.rootContext()->setContextProperty("pluginDir", QUrl::fromLocalFile(dir));
  QObject::connect(&protocol, &Protocol::shutdown, &app, &QCoreApplication::quit);
  QObject::connect(&protocol, &Protocol::stdinClosed, &app, &QCoreApplication::quit);
  engine.load(QUrl::fromLocalFile(QCoreApplication::applicationDirPath() + "/qml/Host.qml"));
  if (engine.rootObjects().isEmpty()) return 1;
  return app.exec();
}
```

`qml/Host.qml` (what Omarchy's `PluginRegistry.qml` does for one plugin; sketch):

```qml
import QtQuick
import Quickshell

ShellRoot {
  id: host
  property var manifest: JSON.parse(omawinProtocol.readFile(pluginDir + "/manifest.json"))
  property var shellFacade: QtObject {                 // the capability-scoped `shell` Omarchy injects (check: its members in PluginRegistry.qml)
    property string fontFamily: "JetBrainsMono Nerd Font"
    function summon(id, payload) { omawinProtocol.send({ type: "open-panel", name: id }) }
  }

  Loader { id: service; active: !!manifest.entryPoints.service; source: pluginDir + "/" + manifest.entryPoints.service
    onLoaded: { item.shell = host.shellFacade; item.manifest = host.manifest; if (item.configure) item.configure(omawinProtocol.settings) } }

  // The bar widget: a PanelWindow kept over the slot omawin's bar reserved (slot-rect messages).
  PanelWindow {
    id: slot
    visible: !!manifest.entryPoints.barWidget && omawinProtocol.slotRect.width > 0
    x: omawinProtocol.slotRect.x; y: omawinProtocol.slotRect.y
    implicitWidth: widget.item ? widget.item.implicitWidth : 0
    implicitHeight: omawinProtocol.slotRect.height
    color: "transparent"
    Loader { id: widget; anchors.fill: parent; source: manifest.entryPoints.barWidget ? pluginDir + "/" + manifest.entryPoints.barWidget : ""
      onLoaded: { item.shell = host.shellFacade; item.manifest = host.manifest; if ("timerService" in item) item.timerService = service.item
                  omawinProtocol.send({ type: "slot", width: item.implicitWidth }) } }
  }

  Connections {
    target: omawinProtocol
    function onPanel(open, name) { if (widget.item && widget.item.toggle) open ? widget.item.open() : widget.item.close() }
    function onSettings(s) { if (service.item && service.item.configure) service.item.configure(s) }
  }
}
```

Omadoro's `BarWidget.qml` creates its own `Panel` (anchored to its button) and hands it the service; the host only has to give the widget `shell`, `manifest` and the `timerService`. *(check)* every injected property name against `PluginRegistry.qml` and Omarchy's `Bar` host (`bar`, `hostWidget`, `anchorItem`).

**Done when** (roadmap 4.9): Omadoro and Omastonk run unchanged on the native host, widgets in the bar's slots, panels opening under them in the theme; Calendar for Omarchy runs with the stand-ins; `omawin plugin scan -Catalog` over the marketplace reports the share that runs as is.

## 11. Worked port: Omadoro (the whole thing)

[Omadoro](https://github.com/brianblakely/omadoro) v0.0.5 (MIT, Brian Blakely). Its files stay; three are added, so one repo serves Omarchy and omawin. Offered upstream as a pull request.

### 11.1 `manifest.json`: the `omawin` block added

Everything above `omawin` is Omadoro's, unchanged:

```json
{
  "schemaVersion": 1,
  "id": "b.omadoro",
  "name": "Omadoro",
  "version": "0.0.5",
  "author": "Brian Blakely",
  "license": "MIT",
  "description": "A Pomodoro timer for the Omarchy bar.",
  "kinds": ["service", "bar-widget"],
  "entryPoints": { "service": "Service.qml", "barWidget": "BarWidget.qml" },
  "barWidget": { "displayName": "Omadoro", "category": "Time", "allowMultiple": false, "defaultSection": "right",
    "defaults": { "workMinutes": 25, "shortBreakMinutes": 5, "longBreakMinutes": 15, "workPhasesPerLongBreak": 4 },
    "schema": [ "… unchanged …" ] },
  "omawin": {
    "minVersion": "0.9.0",
    "command": ["bun", "omawin/service.ts"],
    "panel": "omawin/Panel.xaml",
    "widget": {
      "style": "ring",
      "click": "panel",
      "rightClick": "action playStop",
      "classes": { "work": "accent", "shortBreak": "green", "longBreak": "cyan", "stopped": "muted" }
    },
    "permissions": ["notifications"]
  }
}
```

`kinds` keeps `service`, so it starts with the shell (`StartsAtStartup`) and a phase that ends while the bar is hidden still notifies, as on Omarchy.

### 11.2 `omawin/service.ts`

`Service.qml`'s job over the JSON-lines protocol, with `TimerModel.js` loaded unchanged.

```ts
// Omadoro on omawin: what Service.qml does on Omarchy, over omawin's plugin protocol (JSON lines
// on stdin / stdout; omawin's docs/plugins.md). The timer logic is TimerModel.js, loaded
// unchanged, so a fix there fixes both desktops.
import { readFileSync, writeFileSync, renameSync, mkdirSync } from "node:fs";
import { join } from "node:path";
import { createInterface } from "node:readline";

const pluginDir = join(import.meta.dir, "..");

// TimerModel.js is a QML JavaScript library: plain vars and functions, no exports.
const M = new Function(
  readFileSync(join(pluginDir, "TimerModel.js"), "utf8") +
    "\nreturn { StatusStopped, StatusRunning, StatusPaused, PhaseWork, normalizeConfig, stoppedState," +
    " startNewCycle, pause, resume, addSeconds, advance, recoverInterrupted, sanitizeState, serializableState," +
    " remainingSeconds, elapsedProgress, formatRemaining, phaseLabel, durationSeconds };",
)();

type Timer = { status: string; phase: string; completedWorkPhases: number; deadlineMs: number; [k: string]: unknown };

const out = (o: object) => process.stdout.write(JSON.stringify(o) + "\n");

let config = M.normalizeConfig({});
let state: Timer = M.stoppedState(config, Date.now());
let dataDir = "";
let initialized = false;
let lastTickMs = 0;
let lastShown = "";
let ticker: ReturnType<typeof setInterval> | null = null;

const statePath = () => join(dataDir, "omadoro.json");

function load(): unknown {
  try {
    return JSON.parse(readFileSync(statePath(), "utf8"));
  } catch {
    return null; // none yet, or unreadable: TimerModel.sanitizeState starts stopped
  }
}

// Atomic, as Service.qml's FileView (atomicWrites: true).
function save() {
  mkdirSync(dataDir, { recursive: true });
  const tmp = statePath() + ".tmp";
  writeFileSync(tmp, JSON.stringify(M.serializableState(state, Date.now()), null, 2) + "\n");
  renameSync(tmp, statePath());
}

function notifyStarted(phase: string) {
  const minutes = Math.round(M.durationSeconds(phase, config) / 60);
  out({
    type: "notify",
    title: `${M.phaseLabel(phase)} started`,
    body: phase === M.PhaseWork ? `${minutes} minutes of focus.` : `Take ${minutes} minutes.`,
    icon: "\u{f13ab}", // 󱎫, Omadoro's own glyph
  });
}

// The bar line and the panel's state. The ring empties as the phase runs, as Omadoro's does.
function render() {
  const now = Date.now();
  const stopped = state.status === M.StatusStopped;
  const paused = state.status === M.StatusPaused;
  const time = M.formatRemaining(M.remainingSeconds(state, now));
  const progress = stopped ? 0 : M.elapsedProgress(state, now); // 0..1, elapsed
  const label = M.phaseLabel(state.phase);
  const done = state.completedWorkPhases % config.workPhasesPerLongBreak;
  lastShown = time + state.status;
  out({
    text: stopped ? "\u{f13ab}" : time,
    tooltip: stopped
      ? "Omadoro · click to open, right click to start"
      : `${label}${paused ? " (paused)" : ""} · ${done} of ${config.workPhasesPerLongBreak} before a long break`,
    class: stopped ? "stopped" : state.phase,
    percentage: stopped ? 0 : Math.round((1 - progress) * 1000) / 10,
  });
  out({
    type: "state",
    data: {
      phase: label,
      time,
      progress: Math.round((1 - progress) * 1000) / 10, // the panel's ring, 0..100, emptying
      running: state.status === M.StatusRunning,
      stopped,
      paused,
      playGlyph: stopped ? "" : "", // play / stop
      pauseGlyph: paused ? "" : "", // resume / pause
      round: `${done} of ${config.workPhasesPerLongBreak}`,
    },
  });
}

// Ticks only while running (a stopped or paused timer prints once and waits: no wasted work).
function schedule() {
  const running = state.status === M.StatusRunning;
  if (running && !ticker) ticker = setInterval(tick, 250);
  if (!running && ticker) {
    clearInterval(ticker);
    ticker = null;
  }
}

function setState(next: Timer) {
  state = next;
  save();
  render();
  schedule();
}

function tick() {
  const now = Date.now();
  if (state.status !== M.StatusRunning) return;
  // The PC slept: catch up as Service.qml does (a phase that ended meanwhile advances, and says so).
  if (lastTickMs > 0 && now - lastTickMs > 5000) {
    const r = M.recoverInterrupted(state, config, now);
    lastTickMs = now;
    setState(r.state);
    if (r.notifyPhase !== "") notifyStarted(r.notifyPhase);
    return;
  }
  lastTickMs = now;
  if (now >= Number(state.deadlineMs || 0)) {
    setState(M.advance(state, config, now));
    notifyStarted(state.phase);
    return;
  }
  // Print only when the shown second changes: 1 line a second, not 4.
  if (M.formatRemaining(M.remainingSeconds(state, now)) + state.status !== lastShown) render();
}

// The panel's buttons, as Service.qml's playOrStop / togglePause / addFiveMinutes / skip.
const actions: Record<string, () => void> = {
  playStop() {
    const now = Date.now();
    lastTickMs = now;
    setState(state.status === M.StatusStopped ? M.startNewCycle(config, now) : M.stoppedState(config, now));
  },
  pause() {
    if (state.status === M.StatusStopped) return;
    const now = Date.now();
    lastTickMs = now;
    setState(state.status === M.StatusRunning ? M.pause(state, now) : M.resume(state, now));
  },
  add5() {
    if (state.status === M.StatusStopped) return;
    setState(M.addSeconds(state, 5 * 60, Date.now()));
  },
  skip() {
    if (state.status === M.StatusStopped) return;
    const now = Date.now();
    lastTickMs = now;
    setState(M.advance(state, config, now));
  },
};

function configure(settings: Record<string, unknown>) {
  const next = M.normalizeConfig(settings ?? {});
  const changed = JSON.stringify(next) !== JSON.stringify(config);
  config = next;
  // A stopped timer takes the new durations; a running one finishes its phase first.
  if (changed && initialized && state.status === M.StatusStopped) setState(M.stoppedState(config, Date.now()));
}

for await (const line of createInterface({ input: process.stdin })) {
  let m: { type?: string; [k: string]: any };
  try {
    m = JSON.parse(line);
  } catch {
    continue;
  }
  switch (m.type) {
    case "init": {
      dataDir = m.dataDir;
      configure(m.settings);
      const r = M.recoverInterrupted(load(), config, Date.now()); // sanitizes as it goes, as Service.qml relies on
      initialized = true;
      lastTickMs = Date.now();
      setState(r.state);
      if (r.notifyPhase !== "") notifyStarted(r.notifyPhase);
      break;
    }
    case "settings":
      configure(m.settings);
      render();
      break;
    case "action":
      if (initialized) actions[m.name]?.();
      break;
    case "panel":
      if (m.open) render(); // fresh numbers the moment it opens
      break;
    case "shutdown":
      if (initialized) save();
      process.exit(0);
  }
}
// stdin closed: the shell is gone (the Job Object would end us anyway).
if (initialized) save();
process.exit(0);
```

*(check)* `elapsedProgress`'s range is 0..1 (it is in TimerModel.js: `elapsed / duration`, clamped) and `remainingSeconds` with a paused state uses `remainingSec` (it does).

### 11.3 `omawin/Panel.xaml`

What `Panel.qml` draws: the phase, a big ring with the time, four buttons. Arrows move between buttons (WinUI's XY focus), h/j/k/l too (the panel host), Enter/Space press the focused one, and letters for each.

```xml
<Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
      xmlns:omawin="using:Omawin.Plugins"
      Width="330" Padding="24" RowSpacing="18">
  <Grid.RowDefinitions>
    <RowDefinition Height="Auto"/>
    <RowDefinition Height="Auto"/>
    <RowDefinition Height="Auto"/>
    <RowDefinition Height="Auto"/>
  </Grid.RowDefinitions>
  <Grid.KeyboardAccelerators>
    <KeyboardAccelerator Key="S" omawin:Action.Invoked="playStop"/>
    <KeyboardAccelerator Key="P" omawin:Action.Invoked="pause"/>
    <KeyboardAccelerator Key="A" omawin:Action.Invoked="add5"/>
    <KeyboardAccelerator Key="N" omawin:Action.Invoked="skip"/>
  </Grid.KeyboardAccelerators>

  <TextBlock Style="{ThemeResource OmawinTitle}" HorizontalAlignment="Center" omawin:Bind.Text="phase"/>

  <Grid Grid.Row="1" Width="220" Height="220" HorizontalAlignment="Center">
    <ProgressRing Width="220" Height="220" IsIndeterminate="False" Minimum="0" Maximum="100"
                  Foreground="{ThemeResource OmawinAccent}" Background="{ThemeResource OmawinSelection}"
                  omawin:Bind.Value="progress"/>
    <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center" Spacing="2">
      <TextBlock HorizontalAlignment="Center" FontSize="46" FontFamily="{ThemeResource OmawinMonoFont}"
                 Foreground="{ThemeResource OmawinForeground}" omawin:Bind.Text="time"/>
      <TextBlock HorizontalAlignment="Center" Style="{ThemeResource OmawinCaption}" Text="paused" omawin:Bind.Visible="paused"/>
    </StackPanel>
  </Grid>

  <StackPanel Grid.Row="2" Orientation="Horizontal" HorizontalAlignment="Center" Spacing="14" XYFocusKeyboardNavigation="Enabled">
    <Button Style="{ThemeResource OmawinIconButton}" ToolTipService.ToolTip="Start or stop (S)"
            omawin:Bind.Content="playGlyph" omawin:Action.Click="playStop"/>
    <Button Style="{ThemeResource OmawinIconButton}" ToolTipService.ToolTip="Pause or resume (P)"
            omawin:Bind.Content="pauseGlyph" omawin:Bind.Enabled="!stopped" omawin:Action.Click="pause"/>
    <Button Style="{ThemeResource OmawinIconButton}" ToolTipService.ToolTip="Five more minutes (A)"
            Content="&#xF0415;" omawin:Bind.Enabled="!stopped" omawin:Action.Click="add5"/>
    <Button Style="{ThemeResource OmawinIconButton}" ToolTipService.ToolTip="Next phase (N)"
            Content="&#xF051;" omawin:Bind.Enabled="!stopped" omawin:Action.Click="skip"/>
  </StackPanel>

  <TextBlock Grid.Row="3" HorizontalAlignment="Center" Style="{ThemeResource OmawinCaption}" omawin:Bind.Text="round" omawin:Bind.Visible="!stopped"/>
</Grid>
```

*(check)* `&#xF0415;` (a supplementary-plane code point) in XAML: XAML is XML, so it's legal; make sure the Nerd Font renders it in a `Button` (it's `OmawinIconButton`'s mono font). The ProgressRing's `Background` draws the track in WinUI 2.6+ determinate mode *(check in WinUI 3)*; if not, put an `Ellipse` with `Stroke="{ThemeResource OmawinSelection}"` behind it.

### 11.4 Tests and the try-out

- `bun test` runs Omadoro's own `tests/timer-model.test.js` unchanged (TimerModel is untouched).
- A protocol smoke test, `omawin/service.test.ts` (Bun): spawn `bun omawin/service.ts`, write `init` with a temp `dataDir` and `workMinutes: 1`, expect a stopped bar line; send `action playStop`, expect `class: "work"`, `percentage` near 100; `action skip` → `shortBreak` plus a `notify`; `shutdown` → `omadoro.json` written. Runs on Linux CI too (nothing Windows in it).
- `omawin plugin check .` (manifest and panel through the shell's own rules).
- Live (on the PC; ask before driving the desktop): `omawin plugin add <fork URL>`, `omawin plugin enable b.omadoro`; click the ring → the panel; S starts it; edit `service.ts` → the widget restarts; `omawin plugin log b.omadoro`. Screenshot the bar and the panel (AGENTS.md).

## 12. Testing the plugin system

Proportionate (AGENTS.md: a test earns its place by catching a regression nothing else would):

| What | Test | Where it runs |
|---|---|---|
| `Protocol.Parse` (Waybar lines, class as string or list, bad lines, unknown types) | xUnit over a table of lines | any OS (pure C#; a `menu-app.Tests` project that links `Plugins/Protocol.cs`) |
| `ManifestCheck` (Omadoro's, Omastonk's, Calendar's manifests as fixtures; escapes like `../x`) | xUnit | any OS |
| `ShellConfig` round-trip (string entries, `{id, settings}`, inline settings, unknown fields kept) | xUnit | any OS |
| `PluginProcess` backoff and interval (a fake transport, a fake clock) | xUnit | Windows CI |
| Job Object kill-on-close (start `ping -t`, dispose the job, the process is gone) | xUnit | Windows CI |
| Panels: `--plugin-check` on the templates and Omadoro's panel | `SystemMenu.exe --plugin-check` | Windows CI (AOT build) |
| SDK protocol (`Dispatch`, writes) | xUnit | any OS |
| First-party plugins | each exe's `--selftest` | Windows CI |
| `plugin.ps1` add/enable/disable/clone on a temp account folder (`OMAWIN_USER_LAYER`) | Pester | Windows CI |
| Stand-ins (`hyprctl -j monitors` against a recorded `glazewm query monitors` output) | bash with a fake `glazewm.exe` on PATH printing the recording | Linux CI (the jq mapping is the real logic) |
| scan rules on the four example plugins (two as is, two with stand-ins) | Pester | any OS with pwsh |

## 13. Order of work, as a checklist

1. §1 request format (+ `omawin.cs`), `omawin shell` with `reload` and `notify`.
2. §2 manifest, check, `shell.json` (Bar reads it), registry without processes; `plugin list|enable|disable|check`.
3. §3 processes (job, protocol, log, backoff, interval) + §4 command widget; `plugin new --lang ts`; hot reload.
4. Move the music and agents widgets to plugins (4.6): `plugins/omawin.music` (`command: ["omawin-music.exe", "watch"]`), `plugins/omawin.agents`. Delete Zebar's copies when the native bar replaces Zebar (1.6).
5. §6 menu layering and fragments, script commands; `plugin add|update|remove|clone|log`.
6. §5.0 the AOT proof; then theme resources, `Bind`/`Action`, `PanelCheck`, `PanelHost`; Omadoro port end to end (§11).
7. Settings page from `schema`, secrets, event relay, `set`.
8. §7 providers (launcher first, with `omawin.top`, §9), then the calendar popup and its provider contract.
9. §9 SDK, CI for first-party plugins, admin verbs (with 8.19's hosts editor as the first user).
10. §10.1 stand-ins, `plugin scan`, the catalog run; then §10.2, then §10.3.
11. `plugins/index.txt` and Setup › Plugins › Browse (4.10).
12. `docs/plugins.md` turned from design into reference; a `manual/plugins.md` page for users.
