# The native bar

The bar moved out of Zebar, whose widgets were web pages in Edge's WebView2 (~2 s to draw, ~220 MB), into the menu app (`shell/menu-app/`), the one always-running native process that already holds the launcher, the system menu and screenshots. Omarchy 4 did the same on Linux: one Quickshell process for its bar, launcher and notifications, no web engine. The code lives in `shell/menu-app/Bar/`, since it's that process; the menu app compiles to one native program (Native AOT) so it is up seconds sooner at sign-in.

Zebar was removed on 2026-10-31 (`migrations/2026-10-31-zebar-out.ps1`): the native bar is the only one. Its layout is `default/bar/bar.json`.

## Steps

0. **Native AOT for the menu app**: `build.ps1` makes it wherever Visual Studio's C++ tools are (`-Jit` for the ordinary .NET build; `-p:OmawinAot=true` underneath; `build-kind.txt` says which is installed, and a switch starts the folder afresh). Measured 2026-10-29 on an idle PC, against the current build: first start 0.34 s (was 1.04 s), ready 0.26 s (was 0.38 s), 114 MB (was 173 MB), ~95 files (was 309). Ported so the same code runs in both builds (`SystemMenu.exe --qs-selftest` prints the same, line for line, from each):
   - done: audio (`Audio.cs`, source-generated COM), the display's brightness (`Wmi.cs` in place of System.Management), the Apps folder (`AppsFolder.cs` in place of late-bound `Shell.Application`), app icons (`AppIcons.cs`), JSON (`Json.cs`, generated serializers), `SheenBrush` partial. The native build compiles with no AOT warnings.
   - done: the screenshot overlay (`Snip.cs`): plain Win32 windows drawn with GDI+ (System.Drawing), its own clipboard calls; WinForms is gone from the app. `SystemMenu --snip-selftest` checks what can be checked without you (its windows, the screen copied, OCR, the note); dragging and clicking are tried by hand.
   - the build needs Visual Studio's C++ tools (link.exe) and `vswhere.exe` on the path; PCs without them get the release's build
1. **The bar's window and simple widgets**: one borderless, never-activated window per monitor, 26 px, `bar.json`'s layout, the theme's colors, Nerd Font icons. `omawin.exe`'s strip covers the start. Data straight from Windows and GlazeWM: workspaces and monitor (GlazeWM's IPC), clock, audio (`Audio.cs`), network, battery, Bluetooth, the update arrow, indicators, the menu button.
   Done (`omawin bar native`; `SystemMenu --bar-preview` shows it alone, beside the running menu app, and logs what it sees): every widget above, the fade-in, the theme followed live, a bar per monitor (made again when monitors change). Each window is a frameless popup: WinUI puts its frame styles back on show and move, so `WM_NCCALCSIZE` is answered with 0.
2. **The tray and media**: other apps' tray icons, and Windows' media controls.
   - done: the tray (`Bar/Tray.cs`), as Zebar's systray-util does it: a never-shown window of the taskbar's class, `Shell_TrayWnd`, kept in front of Explorer's, so `Shell_NotifyIcon` tells it about every icon; it hands every message on to Explorer's own taskbar (only Explorer's, so it can't loop with Zebar's copy). Its Win32 calls must be the Unicode ones (`CharSet.Unicode`): the ANSI default registered the class as "S". Clicks are sent as the taskbar sends them; not yet tried by hand.
   - left: media
3. **Switch over** (done 2026-10-31): the native bar the default and the only one; Zebar, our Zebar fork and `zebar/` gone, with a migration; `omawin check`, `omawin profile ram`'s budgets, docs and manual followed.

Left for later: the agents panel, and media in the bar. The music player is a plugin now (`shell/plugins/omawin.music/`, QML), which shows once the QML host is built (roadmap 4.9.4).
