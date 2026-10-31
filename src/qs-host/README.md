# omawin-qs: the plugins' QML host

Plugins in Omarchy's format are QML written for [Quickshell](https://quickshell.org), which only runs on Linux. `omawin-qs` runs them on Windows: it gives a plugin the parts of Quickshell and of Omarchy's shell that plugins use, drawn in omawin's theme ([roadmap](../../plans/roadmap.md) 4.9.4, [plan](../../plans/05-plugins.md) 10.3).

| Part | What |
|---|---|
| `src/main.rs` | the program: finds `qml/`, loads `Host.qml`; `--parent <pid>` ends it with the bar |
| `src/io.rs` | `Omawin.Native`, the only native code: `ProcessCore` (a program, its output as it comes) and `IoHost` (start and let go, environment, files) |
| `qml/Host.qml` | one plugin: its `manifest.json` read, its bar widget loaded with what Omarchy's bar hands a widget (`bar`, `settings`), in a borderless window at `--at x,y,w,h` |
| `qml/Quickshell/` | `Quickshell` (`execDetached`, `env`) and `Quickshell.Io` (`Process`, `SplitParser`, `StdioCollector`, `FileView`) |
| `qml/qs/Commons/`, `qml/qs/Ui/` | Omarchy's `qs.Commons` (colours from omawin's palette, style, i18n) and `qs.Ui` (bar widget, buttons, panels, sliders) |

```
omawin-qs --plugin-dir <folder>                 its bar widget, in a small window
          --at x,y,w,h                          ... over the bar's cell for it, in screen pixels
          --parent <pid>                        ... ending when that process does
          --check                               load it, print QML errors, exit 0 if it loaded
          --shot <png> [--open]                 a picture of the widget (and its panel), then quit
```

The bar (`shell/menu-app/Bar/PluginSlot.cs`) keeps a cell for each plugin in `%LOCALAPPDATA%\launcher\plugins.txt` and starts the host over it. The host lives in `%LOCALAPPDATA%\omawin\tools\qs-host`: an optional tools bundle entry, downloaded the first time a plugin is turned on (`omawin plugin add`).

## Building

Rust and Qt 6 (QtQuick). On Windows CI builds it (`release.yml`'s `qs-host` job: Qt 6.8, `cargo build --release`, `windeployqt`, the music plugin loaded with `--check`). On Linux, for working on it:

```bash
QMAKE=qmake6 cargo build --release
./target/release/omawin-qs --plugin-dir ../../shell/plugins/omawin.music --check
./target/release/omawin-qs --plugin-dir ../../shell/plugins/omawin.music --shot /tmp/music.png --open
```
