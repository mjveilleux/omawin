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

## Worked example: porting Omadoro

[Omadoro](https://github.com/brianblakely/omadoro) (MIT, by Brian Blakely; listed in the [Okomart](https://github.com/brianblakely/omarchy-plugins) catalog) is a typical community Omarchy plugin: a Pomodoro timer with a bar widget, a panel, settings and notifications. What it's made of (v0.0.5):

| File | Lines | What it does | On omawin |
|---|---|---|---|
| `manifest.json` | | id `b.omadoro`, kinds `service` + `bar-widget`, four integer settings | **kept as is**, plus an `omawin` block |
| `TimerModel.js` | 244 | all the timer logic as plain functions (start, pause, resume, add time, advance, recover after a restart, format) | **reused unchanged**, run by Bun |
| `Service.qml` | 208 | holds the state, ticks every second, saves to `$XDG_STATE_HOME/omarchy/omadoro.json`, notifies through `omarchy-notification-send` | rewritten as `service.ts` (~120 lines) |
| `BarWidget.qml` + `CircularProgress.qml` | 146 | a ring that empties as the phase runs, the time beside it | **none**: the shell's command widget with its `ring` style |
| `Panel.qml` | 224 | phase name, a big ring and time, four buttons (play/stop, pause, +5 min, skip), arrow keys and hjkl | rewritten as `Panel.xaml` (~70 lines) |
| `tests/` | | tests for `TimerModel.js` | run with `bun test` |

### Steps

1. **Fork or branch the repo.** Better still, open a pull request to the author: the omawin files sit beside the QML ones and change nothing for Omarchy, so one repo serves both.
2. **Add the `omawin` block** to `manifest.json`:
   ```json
   "omawin": {
     "command": ["bun", "omawin/service.ts"],
     "panel": "omawin/Panel.xaml",
     "widget": { "style": "ring" },
     "permissions": []
   }
   ```
   The settings `schema` already there gives omawin's settings page for free (work, short break, long break, phases per long break).
3. **Reuse the logic.** `TimerModel.js` is a QML JavaScript library: plain `var`s and functions, no exports. `service.ts` loads it without changing it: `const M = new Function(readFileSync(join(pluginDir, "TimerModel.js"), "utf8") + "; return { normalizeConfig, stoppedState, startNewCycle, pause, resume, addSeconds, advance, recoverInterrupted, sanitizeState, serializableState, remainingSeconds, elapsedProgress, formatRemaining, phaseLabel };")()`. The same file now runs on both desktops, and a fix to it fixes both.
4. **Write `service.ts`** (what `Service.qml` did, over the JSON-lines protocol):
   - Read stdin lines. On `init`: `config = M.normalizeConfig(settings)`, load `<dataDir>/omadoro.json` if there, `M.sanitizeState`, then `M.recoverInterrupted` (a phase that ended while the PC was off advances, and notifies).
   - On `action`: `playStop`, `pause`, `add5`, `skip` call the model's functions, as `Service.qml`'s `playOrStop()`, `togglePause()`, `addFiveMinutes()`, `skip()` did; save with `M.serializableState` to `<dataDir>/omadoro.json` (instead of `$XDG_STATE_HOME`).
   - On `settings`: reconfigure (a stopped timer takes the new durations).
   - Every second while running, and on each change, print the bar line and the panel state:
     ```json
     {"text":"24:13","tooltip":"Work · 2 of 4 before a long break","class":"work","percentage":3.1}
     {"type":"state","data":{"phase":"Work","time":"24:13","progress":0.031,"playGlyph":"","pauseGlyph":"","running":true}}
     ```
     `class` is `work` / `shortBreak` / `longBreak`, which the theme maps to colours (accent, green, cyan). `percentage` drives the ring.
   - When a phase ends: `M.advance`, then `{"type":"notify","title":"Break time","body":"Short break · 5 minutes"}` (a Windows toast from the shell) instead of calling `omarchy-notification-send`.
   - While stopped, print one line and nothing more: no wasted ticks.
5. **Write `Panel.xaml`**: a `TextBlock` with `omawin:Bind.Text="phase"`, a WinUI `ProgressRing` (`IsIndeterminate="False"`, `omawin:Bind.Value="progress"`, `Foreground="{ThemeResource OmawinAccent}"`) with the time over it, and four `Button`s with `omawin:Bind.Content` for the glyphs and `omawin:Action.Click="playStop|pause|add5|skip"`. The arrow keys move between buttons on their own (WinUI's XY focus); hjkl comes from the shell's panel host, which maps h/j/k/l to the arrows for every panel (Omarchy users expect it).
6. **Test the logic** with `bun test` on the existing tests; check the files with `omawin plugin check .`.
7. **Try it live**: `omawin plugin add <your fork's URL>`, then `omawin plugin enable b.omadoro`. Edit `service.ts` or `Panel.xaml` and the shell reloads it. `omawin plugin log b.omadoro` shows errors.
8. **Publish**: the GitHub topic `omawin-plugin`, and a line in omawin's `plugins/index.txt` with the reviewed commit.

### What omawin needs first

Omadoro uses nearly every part of the design, so it's a good first outside plugin to aim for:

- v1: the registry, the process host, the command widget **with a `ring` style** (alongside plain text and a meter; custom drawn widgets like `CircularProgress.qml` aren't possible, so the shell offers a few stock looks), and `notify` as Windows toasts.
- v2: the panel host with `omawin:Bind` / `omawin:Action`, `ProgressRing` and `Button` allowed, theme resources, hjkl in panels, the settings page from `schema`.

### Other kinds of plugin

- **A bar widget fed by the web** ([Omastonk](https://github.com/brianblakely/omastonk), a stock symbol's price): the same steps with an `interval` command that fetches and prints one line. `allowMultiple: true` (one widget per symbol) means one process per widget instance, each with its own settings.
- **A plugin store** (Okomart, which reads a `plugins.txt` of git URLs and installs through `omarchy plugin`): on omawin that's Setup › Plugins › Browse, built in; Okomart's catalog format (one git URL a line) is a good one to copy for `plugins/index.txt`.
- **Plugins that drive the window manager** (a window overview like Exposé): these need Hyprland's APIs; on omawin they become features of the GlazeWM fork or the shell, reached through GlazeWM's IPC, not ports.

## Worked example: porting Calendar for Omarchy

[Calendar for Omarchy](https://github.com/crmne/omarchy-calendar) (MIT, by Carmine Paolino; id `crmne.omacal`, v1.2.1) is a big plugin and a useful stress test: Omarchy's clock and calendar popup with HEY, Google Calendar and `.ics` feeds laid over it; a month grid with per-calendar chips, a day view, creating and editing events with loosely typed dates and times and time zones, place suggestions, reminders as notifications, the next event in the bar, HEY time tracking, live sync, and a quick-add card on Alt+Shift+Space. Kinds: `service`, `bar-widget`, `overlay`.

### What it's made of (~8,600 lines)

| Part | Files | Lines | On omawin |
|---|---|---|---|
| Logic in plain JavaScript | `Calendar.js` (1,815), `TimeZones.js` (347), `Model.js` (296), `Places.js` (196), `KeyEdit.js`, `ShortcutModel.js` | ~2,850 | **reused**, run by Bun; `TimeZones.js` reads `/usr/share/zoneinfo`, so on Windows it either runs in Linux or switches to `Intl`'s time-zone data |
| Talking to services | `Source.qml`, `Writer.qml`, `ApiWriter.qml`, `Service.qml` | ~670 | rewritten as `service.ts`: the same calls to `hey`, to the two Python helpers, and to HEY's API |
| Helpers | `backends/google.py`, `backends/feeds.py` (Python standard library only), the `hey` CLI, the `gws` CLI | | **reused as they are** |
| Bar | `BarWidget.qml` | 752 | the command widget: `󰃭 Team standup · in 12m` is text, the hover list a tooltip |
| Panel | `Panel.qml`, `EventCard.qml`, `TimeTrackRow.qml`, `FocusRing.qml` | ~1,900 | XAML, or (better) omawin's own calendar drawing the data (below) |
| Event form and quick add | `EventForm.qml`, `QuickAdd.qml`, `ZoneField.qml`, `PlaceSearch.qml` | ~1,400 | one XAML form used in the panel and in a quick-add overlay |
| Its own settings | `SettingsView.qml`, `KeyField.qml` | ~990 | the generated settings page for the schema; a small XAML page for feeds and API keys, stored as `secret`s |
| Desktop calls | notifications over D-Bus (Python), `xdg-open` for links, `hyprctl` to bind the shortcut | | protocol messages: `notify`, `run` (https links only), and a proposed key |

### The shortcut: run it in Linux

Everything this plugin runs — `hey`, `gws`, `python3`, `/usr/share/zoneinfo` — is Linux, and omawin has Arch in WSL by default. So the service runs there (`"side": "linux"` in the `omawin` block): `service.ts` under Bun in Arch, calling `hey`, `gws` and the Python helpers exactly as on Omarchy, with time zones from Arch's own zoneinfo. Only the three desktop calls change, and they were already the narrowest part of the plugin:

| On Omarchy | On omawin |
|---|---|
| a Python script sends the reminder over D-Bus | `{"type":"notify","title":…,"body":…,"open":"https://…"}`: a Windows toast from the shell; clicking it opens the link |
| `xdg-open <https link>` | `{"type":"run","open":"https://…"}`; the shell opens only `https` links, as the plugin itself insists |
| `hyprctl` binds Alt+Shift+Space while loaded | `keys` in the manifest proposes `alt+shift+space` → `overlay quick-add`; bound in GlazeWM only when you accept, never over a key already in use (the plugin's own rule, and AGENTS.md's) |

Its secrets stay where they are: the HEY token from `hey auth token`, Google's sign-in through `gws`, the feed list in `~/.config/omacal/feeds.json` (mode 600) — all inside Linux, never on a command line, exactly as the README promises. The Google Maps key, if you set one, goes in the plugin's settings as a `secret`.

### The panel: let omawin's calendar draw it

Porting ~3,300 lines of QML panel and form into loose XAML would work but costs the most and gives a second calendar beside omawin's own (the bar's clock gets a calendar popup: [mission-todo.md](mission-todo.md), track 4). The plugin itself is "the stock calendar with your calendars laid over it", so do the same here:

- omawin's clock widget gets its native calendar popup (month grid, day view, Today, the theme's colours, the motion tokens).
- It accepts **calendar providers**: a plugin kind (`calendar-provider`) whose service sends events (`{"type":"events","from":…,"to":…,"items":[{"id","title","start","end","allDay","calendar","color","place","link","reminders"}]}`) and calendars, and receives `create`, `update`, `delete` and `join` actions with the event's id and fields.
- The shell draws the chips, the day view, the event card and the form; the provider does the reading, writing and loose date parsing (`Calendar.js` already has it).

The port then shrinks to `service.ts` plus a mapping from the plugin's model to omawin's event shape: on the order of 500 lines, with ~2,850 reused, instead of ~1,500 new lines of XAML on top. And any other calendar (Outlook through Microsoft Graph, Fastmail's CalDAV) can be a provider of the same popup.

HEY time tracking has no place in a generic calendar: it stays a small XAML section the provider adds under the day view (an extension slot in the popup), or its own panel.

### What this example adds to the design

| Need | Addition |
|---|---|
| a list where each row has its own actions (click an event, delete it) | `omawin:Action.Arg="id"` on templated items: the action carries that item's value |
| typing that the plugin reacts to (dates, places) | `omawin:Action.TextChanged` with a debounce, and `AutoSuggestBox` allowed, its suggestions bound to state |
| keys inside a panel (N, E, T, J, 1-9) | `KeyboardAccelerator`s in the XAML carrying `omawin:Action` |
| a card in the middle of the screen from anywhere (quick add) | the `overlay` kind: a panel shown centred, summoned by a key or `omawin shell summon <id> <name>` |
| a plugin that replaces a built-in widget (the clock) | `"replaces": "clock"` in the `omawin` block, or better, providers that extend the built-in |
| shared surfaces several plugins can fill | provider kinds the shell draws: `calendar-provider` now; launcher search providers and notification actions later |
| Linux-only plugins | `side: linux` (already in the design) is what makes this port small |

### Steps, in order

1. omawin builds its native calendar popup on the clock widget, and the `calendar-provider` contract (v2).
2. A fork or pull request adds `omawin/service.ts` and the `omawin` block (`side: linux`, `kinds` gains `calendar-provider`, the `keys` proposal), reusing `Calendar.js`, `Model.js`, `TimeZones.js` and `backends/` untouched.
3. In Arch: `hey` and `gws` installed (from their releases, or mise), signed in once (`hey auth login`, `gws auth login -s calendar`) in the terminal.
4. `omawin plugin add https://github.com/<fork>/omarchy-calendar.git`, `omawin plugin enable crmne.omacal`; accept the quick-add key on the Plugins page.
5. Check: events from HEY, Google and a feed in the popup; a reminder arrives as a toast; Alt+Shift+Space opens quick add; an edit made in HEY shows within seconds (`hey watch`).
6. Upstream: offer the author the `omawin/` folder, so one repo serves both desktops.
