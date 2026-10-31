# omawin's recommended Windows settings: their own file, and which are set without asking?

**Question:** roadmap 8G says every "faster and calmer" tweak is a settings catalog entry marked *recommended*, and `omawin tune-up` shows what's applied. Plan 07 puts the mark in the catalog line itself (a `recommended=<value>` note, plus `+` to join several values in one line). Where should the recommended value and its "why" live, and which ones does omawin set for you?

**Context:** the catalog parser (`shell/menu-app/Settings/Catalog.cs`) rejects notes it doesn't know, the settings handlers were being built in parallel, and a catalog line has no room for the sentence 8G asks for ("what it does and why").

**Options:**
1. `recommended=<value>` in each catalog line's notes (plan 07), the why in a comment above it.
2. A file of its own, `default/launcher/tune-up.txt`: `id | value | auto/ask | why`, layered like `settings.txt`, checked against the catalog by a test.

**Default:** 2. The catalog stays one grammar, the why is shown to the user (not a comment), and the shared and account folders can change the set without touching the catalog. `auto` is only allowed for a setting that needs no admin and no restart (`TuneUp.Check` refuses otherwise): the tiling-manager conflicts (8.26), menu and Alt+Tab speed (8.27), the accidental shortcuts (8.29), Explorer's slow paths (8.46), web results in search (8.48), and the no-admin privacy values. Everything with a trade-off, admin or your files (Edge, Widgets, OneDrive and Teams at sign-in, sleep, the indexer, drivers, the AI policies, diagnostic data) is `ask`.

**Still open:** which ones belong in the set at all, once each is measured on the PC (8G: anything that shows no difference is dropped). Install and `omawin update` now apply the `auto` ones themselves (the look step runs `omawin tune-up apply -Auto`), each once per PC (`%LOCALAPPDATA%\launcher\tune-up-auto.txt` remembers which), so one you turned back is never set again; an update says in its summary what it set. If that should rather be asked on a new PC too, the look step's call is the one line to change.
