# Plugins (design)

**Status: planned** ([roadmap.md](roadmap.md), phase 4). How omawin gets a plugin ecosystem like Omarchy's: what a plugin is, how the shell runs it, how it's installed, shared and kept safe. Nothing here is built yet; when it is, this file becomes the reference.

## What Omarchy does

Omarchy 4's shell is one Quickshell process; almost everything in it (bar, menu, OSD, lock, notifications, clipboard, panels) is a plugin ([`docs/omarchy-shell.md`](https://github.com/basecamp/omarchy) in Omarchy, `shell/services/PluginRegistry.qml`):

- A plugin is a **git repo** with a `manifest.json` at its root (`schemaVersion`, `id`, `name`, `version`, `author`, `license`, `description`, `kinds`, `activation`, `entryPoints`, `keepLoaded`, and a `barWidget` block with `displayName`, `category`, `allowMultiple`, `defaultSection`, `defaults` and a settings `schema`).
- **Kinds:** `bar-widget`, `bar` (a whole replacement bar), `panel`, `overlay`, `menu`, `service` (headless).
- **Entry points are QML** loaded into the shell's own process. Panels expose `open(payloadJson)` / `close()`; the host injects the shell, the manifest and registries. Third-party plugins get capability-scoped facades instead of the real host objects, but Omarchy is explicit that this is an API boundary, **not a sandbox**: plugins run with the user's rights.
- `omarchy plugin add <git-url>` clones into `~/.config/omarchy/plugins/<id>/`; **new plugins land disabled** so you can read the code; `update` fetches, shows a diff, then fast-forwards; built-ins can be **cloned** to `<username>.<name>` to customise. Saving a file hot-reloads the plugin (inotify).
- `~/.config/omarchy/shell.json` places bar widgets (`bar.layout.<section>[]` with inline settings) and lists `plugins[]` and `disabledPlugins[]`.
- The shell answers IPC (`summon`, `toggle`, `call`, `rescanPlugins`, `listPlugins`, `enablePlugin`, …) through `omarchy-shell`.

## Why omawin can't copy it as is

1. **No QML.** Quickshell is Qt on Wayland; it doesn't run on Windows. Omarchy plugins' visual code can't be loaded.
2. **No loadable code in our shell.** The menu app is compiled with **Native AOT**: there's no JIT, so it can't load a plugin's .NET DLL at run time, and we don't want to give that up (it halves start-up and memory).
3. **Windows has no Wayland layer-shell**; our shell draws its own windows (WinUI 3, Win32 where XAML is too heavy).

So omawin keeps **Omarchy's model and manifest** (git repos, ids, kinds, land-disabled, diff-on-update, clone-to-customise, the same settings schema) and replaces **how the code runs**: plugin logic runs **in its own process**, and anything visual is **inert markup the shell draws**.

## The model in one picture

```
plugin repo (git)            omawin's shell (menu app, Native AOT)
├─ manifest.json  ─────────▶ PluginRegistry: discover, validate, enable, hot-reload
├─ widget.exe / .ps1 / .ts ◀─▶ PluginProcess: stdin/stdout JSON lines, one per plugin
├─ Panel.xaml     ─────────▶ PanelHost: XamlReader.Load, wire omawin:Bind / omawin:Action
├─ menu.txt       ─────────▶ MenuModel: rows appended under the plugin's id
└─ README.md                 Bar: CommandWidget draws the latest state in the theme
```

Logic in a process you can write in anything; looks in markup the shell themes and animates; menus and launcher items as plain text rows like omawin's own.

## What a plugin can contain

| Part | Omarchy kind | In omawin | Runs code? |
|---|---|---|---|
| A bar widget | `bar-widget` | a **command** that prints state as JSON lines; the shell draws it | in its own process |
| A service | `service` | a long-running **command** with no widget (a watcher, a sync, a notifier) | in its own process |
| A panel or drop-down | `panel` | a loose **`.xaml`** file, fed by the plugin's command | no: markup only |
| Menu rows | (Omarchy's menu is data, not plugins) | a **`menu.txt`** fragment, same row format as `launcher/menu.txt` | no |
| Launcher items | | an **`items.txt`** fragment | no |
| Script commands | | scripts in `commands/`, each one a launcher row (Raycast's script commands) | when run |
| Proposed keys | | `keys` in the manifest: shown on the Plugins page, **bound only when you accept** (AGENTS.md: keys never change by guesswork) | no |
| A theme | Omarchy installs themes from git too | a `colors.toml` in Omarchy's format; appears in the theme picker | no |

A replacement for the whole bar (`bar` kind) isn't in v1.

## The manifest

Omarchy's schema, unchanged, plus an `omawin` block for what runs on Windows. Omarchy fields omawin doesn't use (QML entry points) are ignored, so one repo can carry both.

```json
{
  "schemaVersion": 1,
  "id": "acme.weather",
  "name": "Weather",
  "version": "1.2.0",
  "author": "acme",
  "license": "MIT",
  "description": "Temperature in the bar, a forecast panel.",
  "kinds": ["bar-widget", "panel"],
  "activation": "on-demand",
  "keepLoaded": false,
  "barWidget": {
    "displayName": "Weather",
    "category": "Info",
    "allowMultiple": false,
    "defaultSection": "right",
    "defaults": { "city": "", "units": "metric", "refreshIntervalSec": 900 },
    "schema": [
      { "key": "city", "type": "string", "label": "City", "defaultValue": "" },
      { "key": "units", "type": "enum", "label": "Units", "options": ["metric", "imperial"], "defaultValue": "metric" },
      { "key": "refreshIntervalSec", "type": "integer", "label": "Refresh (seconds)", "min": 60, "max": 3600, "defaultValue": 900 }
    ]
  },
  "omawin": {
    "minVersion": "0.9.0",
    "command": ["bun", "widget.ts"],
    "panel": "Panel.xaml",
    "menu": "menu.txt",
    "side": "windows",
    "permissions": ["network"],
    "keys": [{ "binding": "lwin+shift+w", "action": "panel", "description": "Weather forecast" }]
  }
}
```

- `command`: what to run, as an argument list (no shell parsing). A `.ps1` runs with `pwsh -NoProfile -File`, a `.ts`/`.js` with Bun (AGENTS.md: JavaScript runs on Bun), a `.py` with `uv run`, an `.exe` as is.
- `side`: `windows` (default) or `linux`: run the command inside your WSL distro (`wsl.exe -d <distro> -- …`), so a bash plugin written for Omarchy runs unchanged in Arch.
- `permissions`: what the plugin says it does (`network`, `files:<folder>`, `processes`, `clipboard`). **Shown at install, not enforced in v1** (see Safety).
- `minVersion`: the omawin release it needs; older shells skip it with a message.

## How a plugin runs: the process protocol

Each enabled plugin with a `command` gets **one process**, started by the shell (`activation: "startup"`) or when its widget is first shown / its panel first opened (`"on-demand"`, the default). The shell and the plugin talk in **JSON lines**: one JSON object per line, UTF-8.

### Plugin → shell (stdout)

The common case is **Waybar's format**, which Omarchy's `type: "command"` bar modules already use, so existing scripts work:

```json
{"text": "󰖐 14°", "tooltip": "Light rain, 14° (feels 12°)", "class": "rain", "percentage": 60}
```

- `text` is drawn in the bar with the theme's font and Nerd Font glyphs; `class` picks a colour from the theme (`warning`, `critical`, `good`, `muted`, `accent`, or a theme colour name); `percentage` drives a meter if the widget wants one; `tooltip` shows on hover.
- **Panel state**: `{"type": "state", "data": {...}}`: any JSON the plugin's panel binds to (below).
- **Requests to the shell**: `{"type": "notify", "title": "…", "body": "…"}`, `{"type": "open-panel"}`, `{"type": "close-panel"}`, `{"type": "run", "command": ["omawin", "launch", "terminal"]}`, `{"type": "log", "level": "warn", "message": "…"}`.
- Lines that aren't JSON go to the plugin's log, so a stray `print` never breaks the bar.

### Shell → plugin (stdin)

```json
{"type": "init", "settings": {"city": "Lisbon", "units": "metric"}, "theme": "tokyo-night", "omawinVersion": "0.9.1", "pluginDir": "…", "dataDir": "…"}
{"type": "click", "button": "left"}
{"type": "wheel", "delta": -1}
{"type": "panel", "open": true}
{"type": "action", "name": "refresh", "args": {}}
{"type": "settings", "settings": {...}}
{"type": "theme", "name": "gruvbox"}
{"type": "event", "source": "glazewm", "name": "focus_changed", "data": {...}}
```

- `init` comes first, with the widget's settings (manifest `defaults` merged with yours), a writable `dataDir` (`%LOCALAPPDATA%\launcher\plugins\<id>\`, never the repo), and context.
- `event`: plugins that ask for them (`"events": ["glazewm.focus_changed", "theme", "power", "audio"]` in the `omawin` block) get the shell's own event stream relayed, so they don't each open a websocket to GlazeWM.

### Two shapes of plugin

- **Long-running** (default): the process stays up, prints a line whenever its state changes. Best for anything that watches (Docker containers, GitHub notifications, an agent's usage).
- **Interval** (`"interval": 30` in the `omawin` block): the shell runs the command, reads its last line, and runs it again after the interval. Exactly Waybar's `exec` + `interval`, for one-shot scripts. The shell staggers intervals so plugins don't all run on the same second.

### Lifecycle and resources

- Every plugin process is put in a **Windows Job Object** owned by the shell: it dies with the shell, can't leave orphans, and gets limits (memory cap, low CPU priority).
- Crashes restart with backoff (1 s, 5 s, 30 s, then give up and show the widget as failed, with the last stderr lines on hover).
- `stderr` and non-JSON stdout go to `%LOCALAPPDATA%\launcher\plugins\<id>\plugin.log` (rotated); `omawin plugin log <id>` tails it.
- `omawin profile ram` lists each plugin process with its memory, against a budget per plugin.
- **One host for PowerShell plugins.** A PowerShell process costs ~60 MB and ~0.3 s to start. When several PowerShell plugins are enabled, the shell runs them in **one** `pwsh` host, each in its own runspace, with the same JSON-lines protocol per runspace. Compiled and Bun plugins get their own small process.

## How a panel works: inert XAML

A panel (a drop-down under its widget, or a page in the launcher) is a **loose `.xaml` file**. The shell loads it with `XamlReader.Load`. Loose XAML can't contain code: `XamlReader` rejects `x:Class` and event handler attributes, so **a panel is markup only**: all logic stays in the plugin's process. That's both an AOT necessity and a safety property.

```xml
<StackPanel xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
            xmlns:omawin="using:Omawin.Plugins" Spacing="8" Padding="16">
  <TextBlock Style="{ThemeResource OmawinTitle}" omawin:Bind.Text="city"/>
  <TextBlock FontSize="40" Foreground="{ThemeResource OmawinAccent}" omawin:Bind.Text="now.temp"/>
  <ItemsControl omawin:Bind.Items="days">
    <ItemsControl.ItemTemplate>
      <DataTemplate>
        <Grid ColumnDefinitions="*,Auto">
          <TextBlock omawin:Bind.Text="name"/>
          <TextBlock Grid.Column="1" Foreground="{ThemeResource OmawinMuted}" omawin:Bind.Text="range"/>
        </Grid>
      </DataTemplate>
    </ItemsControl.ItemTemplate>
  </ItemsControl>
  <Button Content="Refresh" omawin:Action.Click="refresh"/>
  <ToggleSwitch Header="Imperial" omawin:Bind.IsOn="settings.imperial" omawin:Action.Toggled="units"/>
</StackPanel>
```

- **Data**: the plugin prints `{"type":"state","data":{"city":"Lisbon","now":{"temp":"14°"},"days":[…]}}`.
- **Binding without reflection**: WinUI's `{Binding}` reads CLR properties by reflection, which trimming and Native AOT make unreliable. So panels don't use `{Binding}`. They use omawin's attached properties (`omawin:Bind.Text`, `.Items`, `.IsOn`, `.Value`, `.Visible`, `.Source` for images) holding a path into the JSON state. After loading, the shell walks the tree once, records each bound element, and on every state line sets the values itself. AOT-safe and fast.
- **Actions**: `omawin:Action.Click="refresh"` sends `{"type":"action","name":"refresh"}` to the plugin (with the control's value for toggles, sliders and text boxes).
- **Theme**: the shell puts the current theme into `Application.Resources` as brushes and styles with fixed keys (`OmawinBackground`, `OmawinSurface`, `OmawinForeground`, `OmawinMuted`, `OmawinAccent`, `OmawinRed`, …, `OmawinTitle`, `OmawinBody`), updated live on a theme switch, so a plugin never hard-codes a colour (the theme rule in AGENTS.md).
- **Motion**: the panel opens and closes with the shell's motion tokens; plugins don't animate their own windows.
- **Allowed elements**: the standard WinUI controls. The shell rejects a panel that references unknown namespaces or types, with the error in the plugin log.
- **Fallback if `XamlReader` can't be made reliable under AOT**: the same panels expressed as declarative JSON (`rows`, `toggles`, `sliders`, `lists`), drawn by the shell. The process protocol doesn't change, so plugins only swap their panel file.

## Menus, launcher items and script commands

- `menu.txt`: rows in `launcher/menu.txt`'s format (`id | icon | label | action`). Ids are prefixed with the plugin's id so they can't collide, and appended under a section the plugin names (`setup.plugins.acme-weather`, or a top-level section if it declares one). Actions are the usual kinds (`omawin …`, `run …`, `@page`), plus `plugin <action>`, which sends an action to the plugin's process.
- `items.txt`: launcher items, same format as `launcher/items.txt`.
- `commands/`: script commands. Each file is a launcher row; a header comment gives its name, icon and whether it shows output (`# @name Restart Docker`, `# @icon 󰡨`, `# @mode silent|terminal`). The smallest possible plugin is one folder with a manifest and one script.

While building this, `launcher/menu.txt` itself becomes layered like `items.txt` (repo, then the PC's shared folder, then the account folder), which users want anyway.

## Where plugins live

| Where | What |
|---|---|
| `~/omawin/plugins/<id>/` (the repo) | **built-in** plugins: omawin's own pieces moved to the format (music, agents, dictation, reports) |
| `~/omawin-account/plugins/<id>/` | plugins you added, for this Windows account (git clones) |
| `C:\ProgramData\omawin\shared\plugins\<id>/` | plugins for every account on the PC; **each account still enables them itself** (the shared folder is writable only by the account that made it, and another account's code shouldn't start silently) |
| `%LOCALAPPDATA%\launcher\plugins\<id>\` | each plugin's data and log; never in a repo |

Which plugins are on and where widgets sit: the account's `shell.json` (Omarchy's format: `bar.layout.left|centerBegin|center|centerEnd|right[]` entries `{ "id": "acme.weather", "settings": {…} }`, `plugins[]`, `disabledPlugins[]`), with omawin's `bar.json` as the default when it's absent. Built-ins are on unless listed in `disabledPlugins[]`; added plugins are off until enabled.

## Installing, updating, sharing

`bin/commands.txt` gains a `plugin` group, and Setup › Plugins in the system menu does the same with the mouse or keys:

| Command | What it does |
|---|---|
| `omawin plugin add <git-url> [--ref <tag>]` | shows the manifest (what it runs, its permissions, proposed keys), asks, clones into the account folder, **leaves it disabled** |
| `omawin plugin enable <id> [--section right]` | turns it on and places its widget (the manifest's `defaultSection` if not given) |
| `omawin plugin disable <id>` / `remove <id>` | off / deleted (remove asks; built-ins can only be disabled) |
| `omawin plugin update [id]` | fetches, **shows the diff**, asks, fast-forwards; a plugin pinned with `--ref` stays on its tag |
| `omawin plugin list` | every plugin: built-in or added, on or off, version, memory |
| `omawin plugin clone <id>` | copies a built-in to `<you>.<name>` to change it (Omarchy's clone) |
| `omawin plugin new <name> [--lang ps1\|ts\|py\|bash]` | scaffolds a working plugin (manifest, widget script, panel, README) in the account folder, enabled, hot-reloading |
| `omawin plugin check [path]` | validates a manifest and panel without enabling anything (for authors and CI) |
| `omawin plugin log <id>` | the plugin's log |

All of them take `--yes` for scripts and agents, as Omarchy's do.

**Hot reload**: a `FileSystemWatcher` on each plugin folder (debounced). A changed `manifest.json` re-registers the plugin; a changed panel reloads it the next time it opens; a changed script restarts the process. Authors edit and see.

**Finding plugins**:
1. A curated `plugins/index.txt` in the omawin repo (id, name, description, git URL, the commit reviewed), shown as a gallery page in the launcher (Setup › Plugins › Browse), installable with Enter.
2. GitHub's topic `omawin-plugin` for everything else, searchable from the same page.
3. Any git URL.

## Running Omarchy's plugins

- **Bar modules of `type: "command"`** (a script printing Waybar JSON): run as is, in Linux (`side: linux`), since they're usually bash. An Omarchy user's custom modules carry straight over.
- **Full Omarchy plugins** (QML): the manifest, settings schema and data logic carry over; the QML doesn't. Porting means adding an `omawin` block, moving the QML's data logic into a script (often it's already a command the QML calls), and writing a `Panel.xaml`. A repo can hold both, so one plugin serves Omarchy and omawin.
- **Themes** from git (Omarchy's `colors.toml` format) install the same way and appear in the theme picker.

## Safety

The same honesty as Omarchy's docs: **a plugin is code you run with your own rights.** omawin can't make an untrusted plugin safe; it can make running one a deliberate choice and limit the damage of a broken one.

- **Deliberate**: plugins land disabled; `add` shows what will run and the permissions it declares; `update` shows the diff before applying; the curated index records the reviewed commit, and a plugin from the index installs that commit.
- **Contained crashes**: every plugin is its own process in a Job Object: a crash or a hang never takes the shell down, and memory and CPU are capped.
- **Inert panels**: panels are markup with no code; a plugin can't run anything inside the shell.
- **No secrets by default**: plugins don't get the shell's environment beyond the basics; anything they need (an API token) they ask for through a settings field of type `secret`, stored with Windows' Credential Manager (DPAPI), not in `shell.json`.
- **Later**: run plugins that declare no `files` permission in an **AppContainer** or a restricted token (the approach OpenAI took for Codex's Windows sandbox), so `permissions` become enforced, not just shown. Out-of-process plugins make that possible without changing the protocol.

## Building it (order)

1. **v1: commands and menus.** `PluginRegistry` (discover, validate, `shell.json`), `PluginProcess` (Job Object, JSON lines, restart, logs), `CommandWidget` in the bar, `menu.txt` / `items.txt` fragments, script commands, `omawin plugin add|enable|disable|remove|update|list|log|new|check`, hot reload. Move the music and agents widgets to it as the proof.
2. **v2: panels and settings.** Theme resources in `Application.Resources`, `PanelHost` with `omawin:Bind` / `omawin:Action`, the settings page generated from each manifest's `schema`, `secret` settings, the event relay.
3. **v3: ecosystem.** The gallery and index, `side: linux` for Omarchy's command modules, the shared PowerShell host, `omawin shell` IPC for scripts, restricted-token sandboxing, proposed keys.

Files: `menu-app/Plugins/` (`PluginRegistry.cs`, `PluginProcess.cs`, `PanelHost.cs`, `Bind.cs`), `menu-app/Bar/CommandWidget.cs`, `menu-app/MenuModel.cs` (layered menu and fragments), `bin/plugin.ps1` and `bin/commands.txt`, `plugins/` (built-ins and `index.txt`), `docs/plugins.md` (this file, turned into the reference), `manual/` (a page for users).
