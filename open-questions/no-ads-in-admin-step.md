# Should install's one admin prompt also do `omawin no-ads`' admin part?

**Context.** Since 2026-10-17 the install asks for admin once, at the start (install/admin.ps1), for the whole-PC packages and the GlazeWM and Zebar forks. `omawin no-ads` still has its own admin part, run separately. That part does more than switch settings: it uninstalls Lenovo Now and McAfee's Security Scan Plus and WebAdvisor, removes Lenovo's notification tasks, turns off Lenovo's UDC service, and sets the Windows policy against promoted apps.

**Options.**
1. Leave it out (as now): the one prompt covers only what omawin itself needs; `omawin no-ads` stays a choice you make.
2. Put it in the one prompt, with a line on the admin screen saying what it removes.
3. Put in only the Windows policy (no uninstalls), and leave the PC maker's software to `omawin no-ads`.

**Default went with:** 1. Uninstalling a PC maker's software is a bigger thing than an installer should do without asking for it by name.
