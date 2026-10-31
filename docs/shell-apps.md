# The shell and its apps

Windows puts the same surfaces on every PC: the taskbar, Start, quick settings, the clock's calendar, Settings, the power menu, the clipboard panel. omawin replaces the ones a developer touches all day with lightweight, keyboard-first versions that share one theme, and leaves the rest to Windows. This doc is the map: what each surface is, whether it can be replaced, how, and in what order. The bottom dock comes first.

Research behind it (2026-10-15): the ManagedShell, RetroBar, Seelen UI, YASB and Zebar sources; Windows App SDK release notes; the windows-rs `windows-reactor` benchmarks. Claims we haven't confirmed in code are marked *unverified*.

## Decisions

1. **One shell process draws everything.** The menu app becomes *the shell*: launcher, system menu, pages, snip, and next the dock, bar, calendar, quick settings, OSD and toasts. WinUI's floor is ~100 MB per process whatever the language (C# AOT 117 MB, Rust 101 MB private in Microsoft's own benchmark), so ten small WinUI apps would cost ~1 GB, while one process with ten windows costs little more than one. Windows stay pre-created and hidden, so every surface opens instantly.
2. **C# and WinUI 3**, upgraded: Windows App SDK 2.x (Mica / Acrylic per element), Native AOT and trimming (about half the cold start, less memory; needs `SelfContained=true` and AOT-safe interop). Rust with WinUI (`windows-reactor`, in windows-rs since May 2026) is watched, not adopted: same memory floor, and the shell's cold start is already hidden. Zig has no UI toolkit; not for this.
3. **Plain Win32 + Direct2D only where XAML is too heavy:** the snip overlay, the volume / brightness OSD, an image viewer. Anything that needs text input, forms or accessibility stays in XAML.
4. **Keep Explorer.** Replacing it as the shell (the Winlogon `Shell` key, as Cairo does) breaks Start, Action Center, quick settings, Win+X and packaged-app activation. We hide its taskbar and take over its keys instead.
5. **No code injected into Explorer.** No hook DLLs (Seelen's way), no Windhawk or ExplorerPatcher (both GPL). Everything goes through public window messages, app bars and keyboard hooks.
6. **Apps opened by a file type or link** (`.png`, `calculator:`, `ms-clock:`, `webcal:`) are tiny forwarder exes that hand the request to the running shell (the mutex and request file it already uses), so they cost nothing.
7. **Package identity** through a sparse package for the shell, without moving the exe: needed for the Copilot key, the notification listener (*unverified*) and cleaner toasts.
8. **Borrowed code keeps its license.** ManagedShell is Apache-2.0: we port its logic with attribution in a NOTICE file. Seelen UI (AGPL) and YASB are read, never copied.

## What Windows won't let us replace

- **Ctrl+Alt+Del** (secure attention) and **Win+L** (only a policy can disable it; no remap).
- **Start, quick settings, Action Center and toast rendering** belong to Explorer's experience hosts. We can open them (a synthesized Win key, as `win-keys.exe` does) but not theme or move them; they keep opening where the hidden taskbar would be.
- **Default apps can't be set by script** since the 2024 UserChoice protection (UCPD.sys): for `.png`, `mailto:`, `calculator:` and the like, the user picks omawin once in "Open with" or Settings › Default apps (we open the right page). `ms-settings:` and `microsoft-edge:` can't be redirected at all.
- **Win+key shortcuts Explorer owns** can be freed with the `DisabledHotkeys` registry value (needs an Explorer restart) or swallowed by a low-level keyboard hook, as GlazeWM does.
- **The Copilot key** sends Win+Shift+F23: a hook can take it; Windows' own remap only offers packaged apps registered as Copilot providers.
- **Reading other apps' notifications** needs `UserNotificationListener`, package identity and the user's consent.

## The map

| Surface | Windows today | Verdict | How | omawin |
|---|---|---|---|---|
| **Taskbar** | Explorer | **replace first** | the dock: see below | Zebar dock |
| Top bar | none (Windows has one bar) | ours | native bar in the shell (done 2026-10-31) | Zebar bar (removed) |
| Start, Search | Win, Win+S | replace | Win tap and Win+S open the launcher; the real Start stays one row away | launcher, Win+Space |
| Quick settings | Win+A (sound only; the switches are `launcher quick`) | replace | Wi-Fi, Bluetooth, volume, brightness, battery, power profile, do not disturb, night light, airplane as one panel; night light and Focus have no public API, so those two deep-link | Quick settings page (`launcher quick`, the system menu): Wi-Fi, Bluetooth, a brightness tile per monitor, power mode, night light, do not disturb, battery, and the sound under them |
| Calendar flyout | the clock | replace | month grid and agenda from the bar's clock; events from Microsoft Graph, ICS / webcal URLs, Google Calendar or CalDAV. Not the Windows Appointments API: new Outlook doesn't fill it | none |
| Clock, alarms, timers | `ms-clock:` | replace | timers and alarms in the shell, delivered as toasts; forwarder for `ms-clock:` | none |
| Power, Win+X menu | Win+X | replace | System › Power: lock, sign out, sleep, restart, shut down, Task Manager, admin terminal | system menu |
| Clipboard history | Win+V | replace | take plain Win+V too; images; skip password managers | Win+Shift+V |
| Snipping Tool | Win+Shift+S | replaced | add recording and annotation | snip, OCR, colour, QR |
| Settings | `ms-settings:` | partly | native pages for what developers change (sound, display, network, Bluetooth, power, theme, default apps); deep links for the rest | system menu |
| Emoji panel | Win+. | partly | emoji search in the launcher; Windows' panel stays the fallback (it types into any app, IME included) | none |
| Ads, Tips, Widgets, Copilot | Explorer, policies | remove | policies off; the Copilot key opens the launcher or a coding agent | no-ads |
| Alt+Tab, Task View | Explorer | later | Win+Tab as a GlazeWM workspace overview; leave Alt+Tab (hooks fight games and elevated windows) | none |
| Notifications | Action Center | later | do-not-disturb and our own toasts first; reading others' notifications needs identity | the Notifications page: a searchable history, codes copied (with identity) |
| Volume / brightness OSD | Explorer | later | a themed OSD when our keys change volume; Windows' flyout can't be suppressed cleanly | none |
| Task Manager | taskmgr | later | process list and "kill" in the launcher; taskmgr for the rest | none |
| Image viewer | Photos | done (2026-10-31) | a viewer window in the shell (WinUI's image, so WIC's codecs), `omawin-view.exe` forwarder; Markdown, JSON and logs too | omawin viewer (`omawin open-with`) |
| Calculator | Calculator | later | the launcher's math row; `calculator:` forwarder | launcher math |
| Weather | Widgets | later | bar widget from Open-Meteo (no key) | none |
| Notepad | Notepad | other app | the default editor (`omawin launch editor`) registered for `.txt` | Neovim |
| Media player | Media Player | other app | mpv as the default for video; music is the bar's | music |
| Virtual desktops | Explorer | GlazeWM | workspaces; Windows' virtual desktops left alone | GlazeWM |
| File Explorer | Explorer | keep | browsing stays Explorer's; Win+F finds files | file finder |
| Lock screen, Hello, UAC | Windows | keep | themed only | themed |
| Windows Update, Phone Link, Bluetooth pairing PINs | Windows | keep | deep links | |

## Phase 1: the dock

The plan for replacing the taskbar, from the research (ManagedShell's `ExplorerHelper`, `TasksService`, `TrayService`; Seelen's restore service).

**How it works**

- **Hiding the taskbar:** taskbar auto-hide on (as `install/look.ps1` sets today), then `Shell_TrayWnd`, the Start button and every `Shell_SecondaryTrayWnd` hidden, re-hidden whenever Explorer shows them again (ManagedShell polls every 100 ms; an event-driven check is worth trying). The original state is saved and always restored.
- **A watchdog:** a tiny separate process (or a scheduled check) that brings the Windows taskbar back if the shell dies, so a crash never leaves a PC without one.
- **Reserving space:** one app bar per monitor (`SHAppBarMessage` with the WinUI window's handle, subclassed with `SetWindowSubclass` for `ABN_POSCHANGED` / `ABN_FULLSCREENAPP`), in physical pixels, redone on DPI change and monitor hot-plug. GlazeWM tiles inside the work area and follows its changes, so nothing changes there.
- **Windows list:** top-level windows by the taskbar's rules (visible, unowned, `WS_EX_APPWINDOW` / `TOOLWINDOW`, not cloaked; UWP frame windows special-cased), grouped by AppUserModelID, kept current by `RegisterShellHookWindow` (created, destroyed, activated, flashing) and cloak events.
- **Progress bars, badges, fullscreen:** apps' `ITaskbarList3` calls reach us without injection: set the `TaskbandHWND` property on Explorer's `Shell_TrayWnd` to our window, and `SetProgressValue` / `SetProgressState` / overlay icons / `MarkFullscreenWindow` arrive as messages.
- **System tray:** our own `Shell_TrayWnd` window, topmost, receives apps' `Shell_NotifyIcon` messages, forwards everything (app-bar traffic included) to Explorer's, broadcasts `TaskbarCreated` so apps re-add their icons, and reads the icons already there through `ITrayNotify`. Zebar, YASB and RetroBar all ship this.
- **Win+1..9:** Explorer still answers them against its hidden buttons; GlazeWM or the shell swallows them and maps them to the dock's order.

**To do**

- [x] Spike (branch `dock-spike`, `spikes/dock/`), tested live 2026-10-15: hiding holds on 25H2 (Explorer re-showed the taskbar only at start and during an Explorer restart; no flicker from Start, quick settings, notifications, Win+T, Win+number), the app bar reserves the work area and GlazeWM tiles above it, Zebar's fake tray forwards app bar messages. Results in the spike's README
- [x] The watchdog that restores the taskbar if the shell dies (in the spike: restored within ~50 ms of a kill); `dock --restore` by hand
- [ ] Move the dock into the shell (the menu app), styled to match it; `omawin dock off` gives the taskbar back
- [ ] Win32 layer ported from ManagedShell (Apache-2.0, NOTICE file): window tracking, shell hook, AppUserModelID grouping, icons
- [ ] Dock UI at parity with Zebar's old dock: launcher buttons, open windows by workspace, focused highlighted, click to focus, hover title; themed from the palette
- [ ] Progress bars, attention flashing, overlay badges, fullscreen detection (auto-hide the dock for fullscreen apps)
- [ ] Pinned apps (in the account folder), right-click menu (close, pin, new window)
- [ ] Window previews on hover (`DwmRegisterThumbnail`)
- [ ] System tray in the dock (or the top bar): the fake `Shell_TrayWnd` with forwarding; the riskiest piece, so it lands last and behind a setting
- [ ] Win+1..9 to the dock's order (keys agreed first)
- [ ] Multi-monitor: a dock per monitor, or only on the main one (setting); DPI and hot-plug
- [ ] Zebar's dock retired; `omawin check` verifies the taskbar is hidden and the watchdog is running
- [ ] Measure: the dock adds under 30 MB to the shell

**Risks, highest first:** tray interception (races with Explorer's tray, message forwarding, icons lost when Explorer restarts, Windows updates); Explorer re-showing its taskbar and the flicker; Start and quick settings anchored where the old taskbar was; window-list edge cases (UWP, cloaked windows, GlazeWM's hidden workspaces); Win+number; DPI and hot-plug.

## Phase 2: the shell foundation

- [ ] Windows App SDK 2.x; Native AOT and trimming (`SelfContained=true`), checking Core Audio COM and the WinForms overlay are AOT-safe; move the snip overlay to Win32 / Direct2D
- [ ] Sparse package identity for the shell (2026-10-31: the manifest, `app.manifest`'s `<msix>` and a per-account registration, `omawin notifications identity on`, built; Windows takes it unsigned only in Developer Mode until releases sign it: `open-questions/notification-identity.md`)
- [x] Forwarder exe pattern for file types and protocols, and a Setup › Default apps row that opens the right Settings page (2026-10-31: `omawin-view.exe` for pictures and text, `omawin open-with`, which opens omawin's own Default apps page; protocols are left)
- [ ] A shared keyboard hook (in GlazeWM's fork or the shell) for Win tap, Win+S, Win+X, Win+V, Win+number and the Copilot key, so Explorer's own shortcuts are taken in one place (keys agreed first)
- [ ] Toasts (`AppNotificationManager`) as the shell's one way to notify

## Phase 3 onward: surfaces, in order

1. [x] Quick settings panel (grows from the Audio page) (2026-10-15: Wi-Fi, Bluetooth, a brightness tile per monitor, power mode, night light, do not disturb, battery, and the sound below them, on Win+A; airplane mode, text scale and per-monitor layouts are left)
2. [ ] Calendar flyout from the bar's clock, then accounts (Graph, ICS, Google / CalDAV)
3. [ ] Power section (Win+X)
4. [ ] Start and Search keys to the launcher
5. [ ] Clipboard on Win+V, with images
6. [ ] Timers and alarms with toasts
7. [ ] Settings pages: display, network, Bluetooth, power, default apps
8. [ ] Emoji in the launcher; the Copilot key
9. [ ] OSD, Win+Tab overview, process list, image viewer, weather, notifications

Each lands as an `omawin` command first, then a key or bar click, then a manual page.
