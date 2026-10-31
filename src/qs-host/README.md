# omawin-qs: the plugins' QML host

Plugins in Omarchy's format are QML written for [Quickshell](https://quickshell.org), which only runs on Linux. `omawin-qs` runs them on Windows: it gives a plugin the parts of Quickshell and of Omarchy's shell that plugins use, drawn in omawin's theme ([roadmap](../../plans/roadmap.md) 4.9.4, [plan](../../plans/05-plugins.md) 10.3).

| Part | What |
|---|---|
| `src/main.rs` | the program: finds `qml/`, loads `Host.qml`; `--parent <pid>` ends it with the bar |
| `src/io.rs` | `Omawin.Native`: `ProcessCore` (a program, its output as it comes) and `IoHost` (start and let go, environment, files) |
| `src/bar.rs` | `Omawin.Native`'s `BarLink`: what the bar tells the host over stdin (`focused 1\|0`: its monitor is GlazeWM's focused one), handed to the widget as `bar.screenFocused` |
| `qml/Host.qml` | one plugin: its `manifest.json` read, its bar widget loaded with what Omarchy's bar hands a widget (`bar`, `settings`), in a borderless window at `--at x,y,w,h`, on the monitor those pixels are on (mapped with that monitor's scale) |
| `qml/Quickshell/` | `Quickshell` (`execDetached`, `env`) and `Quickshell.Io` (`Process`, `SplitParser`, `StdioCollector`, `FileView`) |
| `qml/qs/Commons/`, `qml/qs/Ui/` | Omarchy's `qs.Commons` (colours from omawin's palette, style, i18n) and `qs.Ui` (bar widget, buttons, panels, sliders) |

```
omawin-qs --plugin-dir <folder>                 its bar widget, in a small window
          --at x,y,w,h                          ... over the bar's cell for it, in screen pixels
          --parent <pid>                        ... ending when that process does
          --check                               load it, print QML errors, exit 0 if it loaded
          --shot <png> [--open]                 a picture of the widget (and its panel), then quit
```

The bar (`shell/menu-app/Bar/PluginSlot.cs`) keeps a cell for each plugin in `%LOCALAPPDATA%\launcher\plugins.txt` on every monitor's bar and starts a host over each, so a plugin on two monitors runs in two hosts (as Omarchy draws a widget once per monitor). A panel opens under its widget, on that widget's monitor. The bar writes `focused 1` or `focused 0` to each host's stdin as GlazeWM's focus moves between monitors; a key that opens a plugin's panel (the music plugin's Win+M) uses it to pick the focused monitor's. A host started by hand counts as focused.

`deploy/Qt.qml` is never loaded: it lists the Qt modules the host ships beyond its own needs (Controls, Layouts, Shapes, Effects: what community plugins import), for `windeployqt --qmldir` and for the plugin gallery's verdicts (`src/plugin-gallery`). The host lives in `%LOCALAPPDATA%\omawin\tools\qs-host`: an optional tools bundle entry, downloaded the first time a plugin is turned on (`omawin plugin add`).

## Building

Rust and Qt 6 (QtQuick). On Windows CI builds it (`release.yml`'s `qs-host` job: Qt 6.8, `cargo build --release`, `windeployqt`, the music plugin loaded with `--check`). On Linux, for working on it:

```bash
QMAKE=qmake6 cargo build --release
./target/release/omawin-qs --plugin-dir ../../shell/plugins/omawin.music --check
./target/release/omawin-qs --plugin-dir ../../shell/plugins/omawin.music --shot /tmp/music.png --open
```
