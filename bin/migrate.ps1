<#
.SYNOPSIS
    Run the migrations this PC hasn't run yet (omawin migrate; install.ps1 and omawin update run it too).

.DESCRIPTION
    install.ps1 adds and links things; it never takes away what an older version of this setup
    left behind. That's what migrations/ is for, as in Omarchy: one small script per change,
    named <date>-<what>.ps1, run once per PC, oldest first. The ones already run are listed in
    %LOCALAPPDATA%\launcher\migrations.txt.

    Every migration must be safe on any PC, including a brand-new one where there's nothing to
    clean up, and safe to run twice. A failing one stops the run (the rest wait for next time).

        omawin migrate             the new ones
        omawin migrate -List       all of them, and which have run
#>
param([switch]$List)
# Any error in a migration stops it, so it isn't recorded as done (a migration that tolerates a
# failure catches it itself).
$ErrorActionPreference = 'Stop'

$Repo = Split-Path $PSScriptRoot
$Done = Join-Path $env:LOCALAPPDATA 'launcher\migrations.txt'
$ran = @(if (Test-Path $Done) { Get-Content $Done })
$all = Get-ChildItem "$Repo\migrations" -Filter *.ps1 -ErrorAction SilentlyContinue | Sort-Object Name

if ($List) {
    foreach ($m in $all) { '{0}  {1}' -f $(if ($ran -contains $m.BaseName) { 'ran ' } else { 'new ' }), $m.BaseName }
    return
}

$pending = @($all | Where-Object { $ran -notcontains $_.BaseName })
if (-not $pending) { Write-Host 'migrations: nothing new'; return }
New-Item -ItemType Directory -Force -Path (Split-Path $Done) | Out-Null
foreach ($m in $pending) {
    Write-Host "migration $($m.BaseName)" -ForegroundColor Cyan
    try { & $m.FullName }
    catch {
        Write-Host "  failed: $_" -ForegroundColor Red
        Write-Host '  stopped here; the rest run next time (omawin migrate)' -ForegroundColor Red
        exit 1
    }
    Add-Content $Done $m.BaseName
}
exit 0  # not a migration's native program's exit code
