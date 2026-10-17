# Stops the running system menu and waits until its files in $Dir can be written: Windows won't
# replace a running program's files, and a copy that was stopped can hang for minutes before it
# lets go. Both callers need this before they put new files there: menu-app\build.ps1 (publish)
# and bin\install-binaries.ps1 (unpacking a release's ZIP).
#
#   .\stop-menu.ps1                       %LOCALAPPDATA%\system-menu
#   .\stop-menu.ps1 -Dir ..\scratch\menu
param([string]$Dir = (Join-Path $env:LOCALAPPDATA 'system-menu'))
$ErrorActionPreference = 'Stop'

Get-Process SystemMenu -ErrorAction SilentlyContinue | Where-Object SessionId -eq (Get-Process -Id $PID).SessionId | Stop-Process -Force
if (-not (Test-Path $Dir)) { return }

function Test-Locked($file) {
    try { [IO.File]::Open($file, 'Open', 'ReadWrite', 'None').Dispose(); $false } catch { $true }
}
# Files a build left aside (listed while exiting, but stuck in Windows) are cleared here, once
# nothing holds them.
Get-ChildItem $Dir -Filter '*.locked-*' -ErrorAction SilentlyContinue | Remove-Item -ErrorAction SilentlyContinue
$dll = Join-Path $Dir 'SystemMenu.dll'
for ($i = 0; $i -lt 100 -and (Test-Path $dll) -and (Test-Locked $dll); $i++) { Start-Sleep -Milliseconds 100 }
# Exited but never let go: a file in use can still be renamed, so those are moved aside and the
# running copy goes on until it is restarted.
if ((Test-Path $dll) -and (Test-Locked $dll)) {
    $stamp = Get-Date -Format 'yyyyMMddHHmmss'
    $moved = @(Get-ChildItem $Dir -File | Where-Object { $_.Extension -in '.dll', '.exe' -and (Test-Locked $_.FullName) })
    $moved | ForEach-Object { Rename-Item $_.FullName "$($_.Name).locked-$stamp" }
    if ($moved) { Write-Host "    ! $($moved.Count) file(s) still held, moved aside (.locked-$stamp)" -ForegroundColor Yellow }
}
