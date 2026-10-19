# Should the admin step install WSL itself for the wsl preset?

**Question:** when the `wsl` preset is picked on a PC without WSL, should install.ps1's one admin step run `wsl --install --no-distribution`, then restart and carry on?

**Context:** the `wsl` preset (`wsl/install.ps1`) installs Ubuntu for the account, which needs no admin, but only once WSL itself is on the PC. Installing WSL takes admin and a restart. omawin's admin step has no restart-and-resume yet; [inspiration.md](../docs/inspiration.md) (item 5) describes how Microsoft's and Try Omarchy's installers do it.

**Options:**
1. The admin step installs WSL when the preset is picked, and install.ps1 learns to restart and resume once (the larger change, also useful for other features that need a restart).
2. The admin step installs WSL, and the install ends with "restart, then `omawin preset add wsl`".
3. By hand, as now.

**Default for now:** 3. `wsl/install.ps1` stops with "WSL isn't on this PC: in an admin PowerShell run `wsl --install --no-distribution`, restart, then `omawin preset add wsl`".
