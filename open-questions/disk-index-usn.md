# Whole-disk search: the NTFS journal (admin) or the walk (no admin)?

**Question.** Should whole-disk search (roadmap 8.2) ever read the NTFS change journal, which needs admin, or stay on the no-admin walk it has now?

**Context.**
- Built: `omawin everywhere on` walks every fixed drive at idle (background priority) and keeps the names current from Windows' change notifications (`ReadDirectoryChangesW` through `FileSystemWatcher`), all in the menu app, no admin (`shell/menu-app/DiskIndex.cs`, `Everywhere.cs`). Its costs: a walk takes a minute or two of background disk time (on each start, once a day, and after Windows says it dropped changes), and changes made while the menu app isn't running are only seen at the next walk.
- The plan (plans/07-fixing-windows.md 8.2) reads the USN journal instead: `FSCTL_ENUM_USN_DATA` lists a drive's names in seconds and `FSCTL_READ_USN_JOURNAL` picks up exactly what changed while nothing was watching. Both need a volume handle (`\\.\C:`), which needs admin.
- The plan does that with a standing SYSTEM service. AGENTS.md now says **no standing elevated service**, so that design is out.
- What's left for the journal is a one-shot: inside the one admin prompt, an elevated process reads the journal once and writes the index into this account's folder, then exits; the no-admin watcher keeps it current from there. But the elevated program would be omawin's own exe, in a user-writable folder (`~/.local/bin`): anything that can write there could swap it and get admin at the next prompt. That's the same reason the admin step runs Windows' own `powershell.exe` and not a per-user `pwsh.exe`. Doing it safely means copying a hash-checked exe to `%ProgramFiles%\omawin\` (admin-writable) first, as the plan's service did, or doing the journal read in PowerShell 5.1 inside `install/admin-batch.ps1` (`DeviceIoControl` through `Add-Type`, slow over millions of records).
- It also only saves time on the first walk and after the menu app was off: the watcher does the rest either way.

**Options.**
1. Stay on the walk (no admin, ever). Simple; costs a background walk per sign-in.
2. A one-shot journal snapshot as an opt-in admin verb (`disk-index-snapshot`), its exe copied to `%ProgramFiles%\omawin\disk-index\` and hash-checked in the same prompt; the walk stays the fallback.
3. The plan's service (would need AGENTS.md's rule changed).

**Default taken: 1.** Built without admin; nothing elevated. If sign-in walks turn out to be noticeable on a big disk (🖥: measure with `omawin everywhere status` and Task Manager's disk column), option 2 is the next step (it speeds the first index; later sign-ins still walk, since reading the journal again would need admin again).

Delete this file once answered.
