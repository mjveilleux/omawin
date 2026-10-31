# Should Enter through the install's questions raise the WSL admin prompt?

**Context.** Two roadmap items pull against each other. 2.9 (no admin) says a fresh install, Enter through every question, raises no admin prompt, and WSL's one prompt comes when you pick Linux. 3.1 (Linux on by default) says Enter through the questions gives you Arch, with WSL in the one admin prompt. Today the install asks "Linux in WSL: arch, ubuntu, none? (Enter for arch)", so on a PC without WSL, Enter picks Arch and the admin prompt follows (WSL itself, `wsl --install --no-distribution`, then a restart). Everything else in a default install now needs no admin: the tools come from the bundle, and omawin's whole-PC GlazeWM is opt-in (`omawin glazewm admin`).

**Options.**
1. Keep Enter = Arch (3.1): the one prompt on a PC without WSL is the price of Linux by default. 2.9's "Done when" gets an exception for it.
2. Enter = Arch only when WSL is already on the PC (no prompt needed); otherwise the question says "(needs admin once and a restart)" and Enter means none, and `omawin preset add wsl` adds it later.
3. Keep the default, but move the prompt out of the first minute: the install finishes and starts the desktop first, and WSL's prompt comes after, from the bar or the welcome page (needs the background queue, install-speed.md).

**Default went with:** 1, unchanged (3.1 is the maintainer's explicit choice). CI's install test picks no presets, so it still checks that nothing else asks for admin.
