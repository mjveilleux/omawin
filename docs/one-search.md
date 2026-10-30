# One Search (design)

**Status: proposed** ([roadmap.md](roadmap.md), phase 1C). One search bar for everything omawin can find or do, every result tagged with what it is, filters by tag, and a bento home when you haven't typed yet. It replaces the separate launcher home, system menu tree and pages as places you have to know about; they become filters and views of the same window. Clickable mockup: [omawin One Search](https://claude.ai/artifact/FCAA1kuXE7H6BjjfVzrN8z).

## What it is

- **One window, one bar.** Type anything: an app, a setting, a file, a command, an open window, a project, something you copied, a theme, a sum.
- **Every result has a tag** (a coloured label and icon): Apps, Settings, Commands, Files, Windows, Projects, Clipboard, Themes; plus Calculator, Packages and Web as special rows.
- **Filter by tag** without leaving the bar: Tab moves through the filter chips; typing a filter's name and Tab locks it ("set" Tab → Settings); `/` searches files and `>` commands; Backspace on an empty bar removes the filter. The active filter shows as a token in the bar, so you always know what you're searching.
- **Empty bar: the bento home.** Big Terminal tile, Projects and Clipboard showing what's in them, quick switches (Wi-Fi, Bluetooth, Dark mode, Microphone), the theme, your most used apps. For browsing and one-key habits.
- **Typing: a ranked list with a preview.** Results on the left, the selected one's details and its main action on the right.
- **Settings act in place.** A switch shows On / Off and Enter flips it; a choice steps through its values; Ctrl+Z undoes (phase 1B).
- **Ctrl+K: actions for the selected result**, the same idea for every tag (open, run as admin, show in folder, copy path, terminal here, who has it open, pin to home, …).

## Guardrails (the decisions that keep it A+)

1. **Bento for the empty state, a list for results.** Mixed-size tiles are great for a fixed home you learn by position, and bad for ranked results that change on every key: the eye can't scan them in order. So tiles never show search results.
2. **The home doesn't move.** Tiles keep their places (muscle memory); only their contents update (the latest project, the last copy). No "suggested for you" shuffling, nothing promoted.
3. **Few tags, always the same.** Eight kinds at most in the chip row, each with a fixed colour **and** an icon and a word (never colour alone). Rare kinds (Packages, Web, Calculator) appear as rows, not chips. A plugin's results join an existing tag or bring one tag of their own, never several.
4. **"All" is capped per tag.** Without a filter, each tag shows its best few (apps 4, settings 4, commands 3, files 3, the rest 2), and a "Show all 12 Settings" row jumps into that filter. One tag can't flood the list (files would, by sheer number).
5. **Ranking is explainable.** Name match first (start of name, start of a word, then letters in order), then what you've picked for this query before, then how often you use it, then a small prior by tag: for one or two letters, apps and commands come before files. Files need three letters or `/`. The same query gives the same order until your habits change.
6. **The selected row never jumps.** Slow sources (files, packages) arrive later and are added below what's on screen, so Enter never opens something that slid under the cursor.
7. **Enter does the obvious thing; nothing happens without a key.** Enter opens, switches or runs; anything that removes or needs admin says so first. Space types a space (no surprise toggles while you type). The web is never searched as you type: "Search the web for …" is a row you pick, last.
8. **Honest labels.** A setting Windows won't let us change from outside is a link that says *Opens Settings*, never a fake switch.
9. **Typing is never lost.** Changing the filter keeps what you typed; Esc steps back one thing at a time (actions, then the text, then the filter, then the window).
10. **Keys stay as they are** (AGENTS.md). Win+Space opens it; Win+Alt+Space opens it on Settings and commands (the system menu's job); Win+F on Files; Win+Shift+V on Clipboard. Same window, different starting filter: no key changes meaning, so nothing to relearn.
11. **Speed is a feature.** Under 50 ms from key to window, under 16 ms per keystroke (phase 1); typing never waits on a source.
12. **One look.** One row height, one type scale (Inter-like UI text, a monospace for paths and keys), the theme's palette, the shell's motion tokens; crisp icons (1.8). No gradients, no shadows-on-shadows, no emoji.

## What happens to today's pieces

| Today | In One Search |
|---|---|
| The launcher's tile home (Win+Space) | the bento home, kept and refined |
| The system menu's tile home and tree (Win+Alt+Space) | the Settings and Commands filters; the tree's sections become a *Browse* view under Commands (Install, Update, Style, System…) for discovery, still searchable |
| Launcher pages (Projects, Clipboard, Localhost, Audio, Quick settings, Packages, About, Shortcuts) | filters (Projects, Clipboard) or views opened from a result (Audio, Quick settings, Packages, Localhost) |
| The file finder (Win+F) | the Files filter, with its preview, chips for places, and content search (`#`, phase 6) |
| The theme picker | the Themes filter, as a grid of pictures (themes are pictures; a grid is right there) |
| Settings search (phase 1B) | the Settings filter |

## Open questions (to decide with the maintainer)

- **Pinned items on the home:** which tiles are fixed by omawin and which are yours to pin (Ctrl+P on any result)?
- **Grid views inside filters:** themes yes; apps as a grid when filtered to Apps with an empty query, or always a list?
- **Window size:** fixed (as today) or growing with results?
- **Previews on narrow screens:** hide the preview pane below a width, or show it under the list?

## Building it

Mostly a re-organisation of the menu app (`MainWindow.xaml.cs` modes become filters over one result model):
1. **A result model and sources.** `SearchSource` per tag (apps from `Launcher.cs`, settings from phase 1B's catalog, commands from `bin/commands.txt` and `launcher/menu.txt`, files from `FileSearch.cs`, windows from GlazeWM's IPC, projects and clipboard from `Pages.cs` and `Background.cs`, themes from `MenuModel`), each returning ranked items with a tag, name, detail, actions and a preview.
2. **The merger**: scores (guardrail 5), caps (4), stable insertion of late results (6), the filter token and prefixes.
3. **The window**: bar with token, chip row, list + preview, actions panel, footer of keys; the bento home for the empty state (`MainWindow.Tiles.cs` exists).
4. **Plugins** add sources (`launcher-provider`, [plugins.md](plugins.md)) under one tag each.
5. **Measured** with `omawin profile menu` (1.1) before and after.
