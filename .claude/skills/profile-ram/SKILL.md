---
name: profile-ram
description: Profile RAM and CPU on this omawin PC and say what to make leaner. Use when the user asks to profile memory or RAM, asks what's using memory or the processor, says the PC or omawin feels slow or heavy, or asks whether omawin is efficient; also after changing a long-running part of omawin (menu app, bar, music player, agent) to check it didn't grow.
---

# Profile RAM (and CPU)

omawin should cost little while idle. This measures it, finds what's heavy, and turns that into fixes: omawin's own first, then the rest of the PC.

## 1. Measure

```powershell
omawin profile ram -Save          # the table, saved to the history, with what changed since last time
pwsh -NoProfile -File bin/profile-ram.ps1 -Json -Save   # the same as JSON, to read
```

`bin/profile-ram.ps1` (`omawin profile ram`) takes one snapshot plus a 5 s processor sample (`-Seconds` to watch longer, `-Quick` for none). It groups processes into apps: WebView2 and other helper processes count toward the app that started them (the Suno window, Copilot, Slack), so a "26 msedgewebview2" pile becomes the apps behind it; a big `svchost` is named by the services it hosts. Apps are then grouped for the whole machine: omawin, the WSL virtual machine, browsers, web-view apps, Defender, Windows Search, the Windows shell, OneDrive, Windows services, the PC maker's software (by the exe's company), the kernel, and other apps. History: `%LOCALAPPDATA%\launcher\ram-profile.tsv` (parts, groups, top apps, and the PC's in use and cached).

Which number to trust:
- **In use** (the first line; JSON `Memory.InUseMB`): what programs and the kernel hold, Task Manager's "In use". This is the PC's number to get down (roadmap 8K: under 4 GB at idle).
- **Cached** (`Memory.CachedMB`): the standby list, recently used files kept in RAM and handed back instantly. Free memory, not waste: never report it as a problem or try to shrink it.
- **MemoryMB** per app (private working set): RAM only that app holds; Task Manager's Memory. Judge apps by this.
- **CommitMB**: what it asked Windows for. Far above Memory means paged out or reserved; it matters only when the system's commit nears its limit.
- **Cpu**: % of all cores over the sample. Idle parts of omawin should be ~0; anything steadily above 1% while you do nothing is a timer or a poll to find.
- **Kernel** (`Kernel.NonPagedMB`): drivers' memory. Over ~1 GB usually means a driver leaking; name it and look for its update. `Memory.UnattributedMB` is in-use memory no single app holds (shared DLLs, mapped files, page tables).

Take two runs a few minutes apart before calling something a leak; growth across `-Save` runs (the Changed section) is the signal.

## 2. Read omawin's parts

The table lists omawin's parts against idle budgets (the `$Parts` table at the top of the script). The always-on desktop (GlazeWM, the shell with its bar, noctty, herdr, theme-watch, omawin listen) has one budget for all of it, 600 MB (`System.DesktopMB` against `DesktopBudgetMB`); agents, dictation and music are counted apart, as on demand. Over a budget is a finding to explain, not a number to raise. Where each part's weight comes from and what usually fixes it:

| Part | Process(es) | Look at |
|---|---|---|
| shell (menu app and bar) | `SystemMenu` (.NET, WinUI; the bar is inside it) | caches in `shell/menu-app/` kept forever (clipboard history, indexes, icons), images not released, background loops in `Background.cs` and `Bar/` |
| music player | `omawin-music`, `mpv`, the Suno window (`omawin-music --suno` + WebView2) | the Suno window holds a whole browser; it should exist only after the Suno tab was used. mpv's cache (`--demuxer-max-bytes`) |
| agents | `opencode` (one `serve --service`), `claude`, `codex`; `herdr` | exactly one `opencode serve`; every other `opencode` is an open interface (~100 MB each) on a request tab. Finished requests close their tab (`src/agent/live.ps1`); a pile of clients means that broke. Budget is per process (800 MB) |
| terminal | `noctty`, its spare `pwsh` | one spare shell is by design (fast Win+Enter); more are open tabs |
| dictation | `handy` | its speech model stays loaded (~300 MB) while Handy runs |
| GlazeWM, theme-watch, omawin listen | | should be tiny; any CPU while idle is a bug |

To dig into one process: `Get-Process -Id <pid> | Format-List *Memory*, Threads, Handles` (handles climbing over time = a leak); for .NET (`SystemMenu`, `omawin-music`), `dotnet-counters monitor -p <pid>` if installed; for a WebView2 app, its DevTools memory panel.

## 3. The rest of the PC

Read the **By group** table (JSON `Groups`), biggest first, and name what's in the big ones:
- **WSL virtual machine** (`vmmem` / `VmmemWSL`): Linux's memory as Windows sees it, which grows with Linux's file cache and gives back slowly. omawin's `~/.wslconfig` (`wsl/.wslconfig`) already drops Linux's cache once it's idle (`autoMemoryReclaim=dropCache`); a cap (`memory=6GB` under `[wsl2]`) goes in the account folder's copy (docs/work.md). `wsl --shutdown` frees it now (ask first: it closes every Linux terminal).
- **Browsers** and **web-view apps** (new Outlook, Teams, Widgets, Phone Link, Copilot): each is a browser. Widgets and Phone Link can be switched off in the system menu's Windows settings (roadmap 8.28).
- **Windows Search**: the indexer; the launcher and the finder replace it (8.31).
- **Defender**: keep it.
- **PC maker**: its services; `omawin no-ads` removes the upsells.
- **Windows services**: a big `svchost: …` row names its services.

Say which can go and which are the user's own apps they chose. Never stop, kill or uninstall anything without asking; closing an app the user has open loses their work.

## 4. Report

Lead with two lines: the PC's memory in use (and cached apart: free when needed), and omawin's desktop against its 600 MB with the biggest group named. Then each finding: what, how big, why, and the fix (a file to change, a tab to close). Changes to omawin's code follow AGENTS.md: measure again after the fix and show the before and after numbers.
