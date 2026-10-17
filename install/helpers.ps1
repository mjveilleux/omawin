# install.ps1 step: omawin and the other small exes in ~/.local/bin, btop, noctty, the menu app
# and the music player.
# Dot-sourced by install.ps1, so its functions (install/lib.ps1) and $Repo are in scope.

Write-Step 'omawin and the helpers'
# Small exes into ~/.local/bin (on PATH): omawin (the setup's commands, bin/commands.txt), the
# bar's helpers, and the music player. bin/install-binaries.ps1 takes a release's prebuilt files
# when there is one for this PC and builds them here otherwise (docs/releases.md).
$bin = "$HOME\.local\bin"
New-Item -ItemType Directory -Force -Path $bin | Out-Null
if (Add-UserPath $bin -Prepend) { Write-Note "added $bin to PATH" }
& "$Repo\bin\install-binaries.ps1" -What helpers
# `btop` (btop4win, the task manager), like on Linux: a shim in ~/.local/bin, which is on
# the PATH GlazeWM and the launcher already have (winget's own PATH entry needs a sign-out).
$btop = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\aristocratos.btop4win*\btop4win\btop4win.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
if ($btop) { Set-Content "$bin\btop.cmd" "@`"$($btop.FullName)`" %*" -Encoding ascii; Write-Note "ok  btop -> $($btop.FullName)" }
else { Write-Warn 'btop4win not installed (packages.ps1); no btop' }

Write-Step 'noctty'
# The default terminal, per-user (bin/noctty.ps1; `omawin update terminal` takes a newer one).
try { & "$Repo\bin\noctty.ps1" -IfMissing } catch { Write-Warn "noctty not installed: $_" }

Write-Step 'Win+Alt+Space system menu'
# A WinUI 3 app (menu-app/), into %LOCALAPPDATA%\system-menu: the release's ZIP, or a build here
# when there is none (it needs the .NET SDK, packages.ps1).
& "$Repo\bin\install-binaries.ps1" -What menu

Write-Step 'Music player (the bar)'
# omawin-music (music/): one exe in ~/.local/bin, the release's, or built here.
& "$Repo\bin\install-binaries.ps1" -What music
