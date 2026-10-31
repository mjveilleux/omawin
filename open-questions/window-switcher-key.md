# Which key opens the window switcher?

**Question.** One Search's Windows filter is now the window switcher of roadmap 8.5: every window GlazeWM manages, typed to filter, a live picture of the selected one in the preview (DWM's thumbnail), Enter goes to it (switching workspace), Ctrl+K closes it. It opens with `omawin windows` (or `omawin search windows`, or typing `windows` + Tab in the launcher). Which key, if any, should open it?

**Context.**
- AGENTS.md: keys never change by guesswork; nothing is bound until you say.
- Taken by omawin today (`config/glazewm/config.yaml`): Win+Space, Win+Alt+Space, Win+Enter, Win+F, Win+Shift+F, Win+Shift+S, Win+Shift+T, Win+Shift+V, Win+M, Win+A, Win+H, Win+Ctrl+Space, Win+1-9, and Alt+ letters for tiling.
- Omarchy cycles windows with Alt+Tab; Windows' Alt+Tab shows thumbnails of everything, in no useful order (the roadmap's complaint).
- GlazeWM binds keys through its own hook, so a key it takes from Windows (Win+Tab, Alt+Tab) stops doing Windows' thing while GlazeWM runs.

**Options.**
1. **Win+Tab**: replaces Task View (thumbnails plus Windows' virtual desktops, which GlazeWM's workspaces already replace). Easy to remember, nothing of omawin's moves.
2. **Win+W**: free in omawin; Windows 11 uses it for Widgets (no-ads turns Widgets off, so it's often idle).
3. **Alt+Tab**: what Omarchy uses; replaces Windows' own switcher entirely. The roadmap says Alt+Tab stays Windows' unless you choose.
4. No key: `omawin windows` from the launcher's Commands, or a dock entry.

**Default taken: 4** (nothing bound). Option 1 is the suggestion: one entry in the template, `- commands: ['emit omawin windows']` with `bindings: ['lwin+tab']`, and a row in `manual/03-keys.md`.

Delete this file once answered.
