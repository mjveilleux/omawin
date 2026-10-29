# The native bar (work in progress)

The bar moves out of Zebar, whose widgets are web pages in Edge's WebView2 (~2 s to draw, ~220 MB), into the menu app (`menu-app/`), the one always-running native process that already holds the launcher, the system menu and screenshots. Omarchy 4 did the same on Linux: one Quickshell process for its bar, launcher and notifications, no web engine. The code lives in `menu-app/Bar/`, since it's that process; the menu app compiles to one native program (Native AOT) so it is up seconds sooner at sign-in.

Until it matches Zebar, both exist: Zebar stays the default and the native bar is switched on to try it.

## Steps

0. **Native AOT for the menu app** (`build.ps1 -Aot`, `-p:OmawinAot=true`). Measured 2026-10-29 on an idle PC, against the current build: first start 0.34 s (was 1.04 s), ready 0.26 s (was 0.38 s), 114 MB (was 173 MB), ~95 files (was 309). Ported so the same code runs in both builds (`SystemMenu.exe --qs-selftest` prints the same, line for line, from each):
   - done: audio (`Audio.cs`, source-generated COM), the display's brightness (`Wmi.cs` in place of System.Management), the Apps folder (`AppsFolder.cs` in place of late-bound `Shell.Application`), app icons (`AppIcons.cs`), JSON (`Json.cs`, generated serializers), `SheenBrush` partial. The native build compiles with no AOT warnings.
   - left: the screenshot overlay (`Snip.cs`, WinForms), onto plain Win32; `Aot/SnipStub.cs` stands in until then
   - the build needs Visual Studio's C++ tools (link.exe) and `vswhere.exe` on the path; PCs without them get the release's build
1. **The bar's window and simple widgets**: one borderless, never-activated window per monitor, 26 px, `bar.json`'s layout, the theme's colors, Nerd Font icons. `omawin.exe`'s strip covers the start. Data straight from Windows and GlazeWM: workspaces and monitor (GlazeWM's IPC), clock, audio (`Audio.cs`), network, battery, Bluetooth, the update arrow, indicators, the menu button.
2. **The tray and media**: other apps' tray icons (taken the way Zebar does: a window that receives what apps tell the taskbar), and Windows' media controls.
3. **Switch over**: the native bar the default; Zebar, our Zebar fork and `zebar/build-client.ps1` go, with a migration; `omawin check`, `omawin profile ram`'s budgets, docs and manual follow.

Left for later: the agents panel and the music player drop-downs (Zebar's for now).
