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

`bin/profile-ram.ps1` (`omawin profile ram`) takes one snapshot plus a 5 s processor sample (`-Seconds` to watch longer). It groups processes into apps: WebView2 and other helper processes count toward the app that started them (Zebar, the Suno window, Copilot, Slack), so a "26 msedgewebview2" pile becomes the apps behind it. History: `%LOCALAPPDATA%\launcher\ram-profile.tsv`.

Which number to trust:
- **MemoryMB** (private working set): RAM only that app holds; Task Manager's Memory. Judge by this.
- **CommitMB**: what it asked Windows for. Far above Memory means paged out or reserved; it matters only when the system's commit nears its limit.
- **Cpu**: % of all cores over the sample. Idle parts of omawin should be ~0; anything steadily above 1% while you do nothing is a timer or a poll to find.
- Free RAM low isn't a problem by itself: Windows fills free RAM with cache. Pressure shows as commit near the limit, a big Memory Compression, or the page file growing.

Take two runs a few minutes apart before calling something a leak; growth across `-Save` runs (the Changed section) is the signal.

## 2. Read omawin's parts

The table lists omawin's parts against idle budgets (the `$Parts` table at the top of the script). Over a budget is a finding to explain, not a number to raise. Where each part's weight comes from and what usually fixes it:

| Part | Process(es) | Look at |
|---|---|---|
| bar and dock | `zebar` + its WebView2 | `zebar/tokyo-islands/*.js`: intervals, `requestAnimationFrame` loops left running while nothing changes, listeners added on every render, big DOM rebuilt each tick |
| menu app | `SystemMenu` (.NET, WinUI) | caches in `menu-app/` kept forever (clipboard history, indexes), images not released, background loops in `Background.cs` |
| music player | `omawin-music`, `mpv`, the Suno window (`omawin-music --suno` + WebView2) | the Suno window holds a whole browser; it should exist only after the Suno tab was used. mpv's cache (`--demuxer-max-bytes`) |
| agent | `opencode` (one `serve --service`), `herdr` | exactly one `opencode serve`; every other `opencode` is an open interface (~100 MB each) on a request tab. Finished requests close their tab (`agent/live.ps1`); a pile of clients means that broke |
| terminal | `noctty`, its spare `pwsh` | one spare shell is by design (fast Win+Enter); more are open tabs |
| GlazeWM, theme-watch, omawin listen | | should be tiny; any CPU while idle is a bug |

To dig into one process: `Get-Process -Id <pid> | Format-List *Memory*, Threads, Handles` (handles climbing over time = a leak); for .NET (`SystemMenu`, `omawin-music`), `dotnet-counters monitor -p <pid>` if installed; for a WebView2 app, its DevTools memory panel.

## 3. The rest of the PC

Name the biggest non-omawin apps and what they are (Defender `MsMpEng`, Copilot, Slack, `svchost` groups, the PC maker's services such as Lenovo's). Say which can go (`omawin no-ads` removes the maker's upsells) and which are the user's own apps they chose. Never stop, kill or uninstall anything without asking; closing an app the user has open loses their work.

## 4. Report

Lead with one line: omawin's total and whether its parts are within budget. Then each finding: what, how big, why, and the fix (a file to change, a tab to close). Changes to omawin's code follow AGENTS.md: measure again after the fix and show the before and after numbers.
