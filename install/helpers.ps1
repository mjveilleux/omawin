# install.ps1 step: omawin and the other small exes in ~/.local/bin, noctty, the menu app
# and the music player.
# Dot-sourced by install.ps1, so its functions (install/lib.ps1) and $Repo are in scope.

Write-Step 'omawin and the helpers'
# Small exes into ~/.local/bin (on PATH): omawin (the setup's commands, bin/commands.txt), the
# bar's helpers, and the music player. bin/install-binaries.ps1 takes a release's prebuilt files
# when there is one for this PC and builds them here otherwise (docs/releases.md).
$bin = "$HOME\.local\bin"
New-Item -ItemType Directory -Force -Path $bin | Out-Null
if (Add-UserPath $bin -Prepend) { Write-Note "added $bin to PATH" }
# Started at the top of the install, alongside the tools bundle (install/software.ps1,
# Start-EarlyHelpers): its downloads are done or nearly, and only collected here.
$early = $script:EarlyHelpers
if ($early) {
    foreach ($piece in 'omawin.exe', 'system menu', 'music player', 'Windows fixes') { Add-InstallItem 'Tools' $piece $piece 'run' }
    $early.Process.WaitForExit()
    Get-Content $early.Log, "$($early.Log).err" -ErrorAction SilentlyContinue | ForEach-Object { if ("$_".Trim()) { Write-Note "$_".Trim() } }
    Remove-Item $early.Log, "$($early.Log).err" -Force -ErrorAction SilentlyContinue
    $state = if ($early.Process.ExitCode -eq 0) { 'done' } else { Write-Warn "the helpers' install stopped (exit $($early.Process.ExitCode)): run install.ps1 -Step helpers"; 'fail' }
    foreach ($piece in 'omawin.exe', 'system menu', 'music player', 'Windows fixes') { Add-InstallItem 'Tools' $piece $piece $state }
}
else { Invoke-InstallItem 'Tools' 'omawin.exe' { & "$Repo\bin\install-binaries.ps1" -What helpers } }

Write-Step 'noctty'
if (Get-Command Wait-BundleTools -ErrorAction SilentlyContinue) { Wait-BundleTools noctty }   # the tools bundle brings it
# The default terminal, per-user (bin/noctty.ps1; `omawin update terminal` takes a newer one).
Invoke-InstallItem 'Tools' 'noctty' { & "$Repo\bin\noctty.ps1" -IfMissing }

if (-not $early) {
    Write-Step 'Win+Alt+Space system menu'
    # A WinUI 3 app (shell/menu-app/), into %LOCALAPPDATA%\system-menu: the release's ZIP, or a build here
    # when there is none (it needs the .NET SDK, install/packages.ps1).
    Invoke-InstallItem 'Tools' 'system menu' { & "$Repo\bin\install-binaries.ps1" -What menu }

    Write-Step 'Music player (the bar)'
    # omawin-music (src/music/): one exe in ~/.local/bin, the release's, or built here.
    Invoke-InstallItem 'Tools' 'music player' { & "$Repo\bin\install-binaries.ps1" -What music }

    Write-Step 'Windows fixes (omawin who-has, health, startup, ...)'
    # omawin-windows (src/windows/): one exe in ~/.local/bin, the release's, or built here.
    Invoke-InstallItem 'Tools' 'Windows fixes' { & "$Repo\bin\install-binaries.ps1" -What windows }
}
