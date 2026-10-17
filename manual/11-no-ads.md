# No ads

**System menu › Setup › No ads** (`omawin no-ads`) stops the popups that come with the PC and with Windows.

**For you, no admin** (install does this part):
- Windows' suggestions off:
  - promoted apps installed silently
  - Start recommendations and tips toasts
  - the "finish setting up" screens
  - suggested content in Settings
  - lock screen fun facts
  - OneDrive promos in Explorer
  - account nags in Start
  - search highlights (Bing's promos in the search box)
- The advertising ID off.
- **Copilot** (when the app is installed): off the taskbar, out of Settings › Apps › Startup, and closed when it's running with no window (it keeps itself running in the background otherwise, ~70 MB). It still opens from Start.

Install runs this part first, before anything else is installed or started.

**With admin** (one Windows prompt, only when something's left to do):
- Sets Windows' "no consumer features" policy.

**The PC maker's software**, each part only when that software is on the PC (elsewhere it touches nothing):
- **Lenovo**: notifications from Vantage and its messaging plugin off, Vantage no longer starts at login; with admin, Lenovo Now uninstalled, its leftover shortcuts and promo tasks removed, the Universal Device Client (which runs the ads plugin) off. Lenovo Vantage (battery limits, firmware, warranty) and Lenovo Hotkeys (Fn keys) stay.
- **McAfee**: its notifications off; with admin, McAfee Security Scan Plus and WebAdvisor uninstalled, their leftover shortcuts removed.
- **Adobe**: Acrobat's sync no longer starts at login, Photoshop Express (preinstalled) removed; with admin, Adobe's updater only when Acrobat asks, its daily task off.
- **Visual Studio**: with admin, its installer no longer downloads updates in the background (it used ~450 MB and a core while it did); updates come when you open the Installer, as its own "Automatically download updates" setting does.

`omawin check` warns if any of that software or the suggestions come back.
