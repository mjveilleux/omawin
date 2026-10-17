# Our GlazeWM fork (it starts before Explorer, so the desktop is up at sign-in), signed on this PC
# (install/glazewm-fork.ps1). Asks for admin once; declining keeps the official GlazeWM.
# A failure (offline, GitHub's rate limit, admin declined) only warns and still counts as run:
# otherwise every `omawin update` would ask for admin again and hold up the migrations after
# this one. The official GlazeWM works the same, and the desktop step (install.ps1 -Step
# desktop) installs the fork whenever it's run.
try { & (Join-Path (Split-Path $PSScriptRoot) 'install\glazewm-fork.ps1') }
catch { Write-Warning "GlazeWM fork not installed: $_ (the official GlazeWM stays; install.ps1 -Step desktop tries again)" }
