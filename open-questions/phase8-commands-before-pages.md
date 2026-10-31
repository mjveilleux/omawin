# Phase 8: terminal commands first, pages later?

**Question.** Roadmap 8.0 puts the occasional fixes (8.15 Health, 8.16 Startup, 8.20 Connections, 8.17 `top`) in first-party plugins as launcher pages, and the shared building blocks in `shared/Omawin.Windows`. The plugin kinds they need (`page`, `launcher-provider`) and the plugin SDK don't exist yet. Should the fixes wait for them, or ship as commands now?

**Context.**
- AGENTS.md: every action is an `omawin` command; no new top-level folders; compiled C# first.
- The plugin host today is the QML host for bar widgets (4.9.4); pages in the launcher aren't a plugin kind yet.
- A command that answers in a terminal works today, from a key's menu row too (`task omawin health`).

**Options.**
1. Wait for the page plugin kind, then build each fix as its page.
2. Build the shared library now with the fixes as terminal commands (`omawin-windows.exe`), and put the pages on top of it later.
3. Build them as pages inside the menu app (core), against the 8.0 rule.

**Default taken: 2.** `src/windows/` is the shared library (`Omawin.Windows`, under `src/` rather than a new top-level `shared/`) and `omawin-windows.exe`, shipped in the release like the music player. Its parsing and wording are pure and tested; the pages, when the plugin kinds land, link the same files and draw what the commands print. The one core piece (8.11's microphone mute) is in the menu app, as 8.0 says.

Delete this file once answered.
