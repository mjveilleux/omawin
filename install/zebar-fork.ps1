<#
.SYNOPSIS
    Our Zebar fork in place of the official zebar.exe, so each Windows account has its own bar.

.DESCRIPTION
    Zebar serves its widgets from a localhost port (6124), and its widgets reach GlazeWM on
    another (6123). Localhost is shared by every account signed in on the PC, so with the
    official Zebar a second account's bar can't start, or shows the other account's
    workspaces. The fork (github.com/mjveilleux/zebar, branch omawin) takes both ports from
    this account's settings, ZEBAR_ASSET_PORT and GLAZEWM_IPC_PORT (bin/ports.ps1), and is
    the official one otherwise.

    The admin part downloads the fork's zebar.exe built on the installed Zebar's version
    (skipped, with the official one kept, when there's no fork build for it yet), checks it
    against the release's SHA256SUMS, and puts it in Program Files, keeping the official one as
    zebar.official.exe. It does it all itself, so install.ps1's one admin step can run it
    (install/admin.ps1), as another account if need be. Safe to run again.

        .\zebar-fork.ps1              the fork in (asks for admin)
        .\zebar-fork.ps1 -Restore     the official one back (asks for admin)
        .\zebar-fork.ps1 -Check       whether the admin part has anything to do ("needed" or why not)
        .\zebar-fork.ps1 -Machine     the admin part, run elevated; -Out <file> gets its last line
        .\zebar-fork.ps1 -After       this account's part: pins Zebar in winget, so an upgrade doesn't undo it

    Runs under Windows PowerShell 5.1 too.
#>
param([switch]$Restore, [switch]$Check, [switch]$Machine, [switch]$RestoreMachine, [switch]$After, [string]$Out)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor 3072
$ProgressPreference = 'SilentlyContinue'

$dir = Join-Path $env:ProgramFiles 'glzr.io\Zebar'
$exe = Join-Path $dir 'zebar.exe'
$official = Join-Path $dir 'zebar.official.exe'
$state = Join-Path $env:ProgramData 'omawin\zebar-fork.txt'
$elevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

# Puts $New at $Target even while another account runs it (see install/glazewm-fork.ps1).
function Set-InUseFile([string]$New, [string]$Target) {
    Get-ChildItem (Split-Path $Target) -Filter ((Split-Path $Target -Leaf) + '.old-*') -ErrorAction SilentlyContinue |
        ForEach-Object { Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue }
    if (Test-Path $Target) {
        try { Copy-Item $New $Target -Force; return } catch { }
        Rename-Item $Target ((Split-Path $Target -Leaf) + '.old-' + (Get-Date -Format yyyyMMddHHmmss))
    }
    Copy-Item $New $Target -Force
}
# The installed Zebar's version, from Windows' list of programs (zebar.exe's own is 0.0.0).
function Get-Base {
    $entry = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue |
        Where-Object { $_.DisplayName -eq 'Zebar' } | Select-Object -First 1
    if ($entry.DisplayVersion) { $entry.DisplayVersion -replace '^(\d+\.\d+\.\d+).*', '$1' }
}
function Get-Release([string]$Base) {
    # (Assigned first: Windows PowerShell passes the whole list down a pipe as one object.)
    $releases = Invoke-RestMethod 'https://api.github.com/repos/mjveilleux/zebar/releases' -UseBasicParsing
    $releases | Where-Object { $_.tag_name -like "v$Base-omawin.*" } | Sort-Object { if ($_.tag_name -match 'omawin\.(\d+)') { [int]$Matches[1] } else { 0 } } | Select-Object -Last 1
}
# A folder of its own under Program Files, which only admins can write, for the admin part's
# download: nothing running in an account can swap a file in it between the hash check, the
# signing and the copy (the account's own TEMP is that account's to write).
function New-Staging {
    $root = Join-Path $env:ProgramFiles 'glzr.io\.omawin-staging'
    New-Item -ItemType Directory -Force $root | Out-Null
    $work = Join-Path $root ([Guid]::NewGuid().ToString('n'))
    New-Item -ItemType Directory $work | Out-Null
    $work
}
# In, and still ours: an upgrade that got past the pin puts the official file back.
function Test-Current([string]$Tag) {
    (Test-Path $state) -and (Get-Content $state -Raw).Trim() -eq $Tag -and (Test-Path $official) -and
        (Get-Item $exe).Length -ne (Get-Item $official).Length
}
function Invoke-Elevated([string]$Switch) {
    $result = Join-Path $env:TEMP "zebar-fork-$PID.txt"
    $p = Start-Process powershell.exe -Verb RunAs -Wait -PassThru -WindowStyle Hidden -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", $Switch, '-Out', "`"$result`""
    $line = if (Test-Path $result) { (Get-Content $result -Raw).Trim() }
    Remove-Item $result -ErrorAction SilentlyContinue
    if ($p.ExitCode -ne 0) { throw "the admin part failed$(if ($line) { ": $line" } else { " ($($p.ExitCode))" })" }
    $line
}
function Say([string]$Line) { if ($Out) { Set-Content $Out $Line -Encoding utf8 }; $Line }

# --- the admin part ------------------------------------------------------------------------------
if ($Machine) {
    if (-not $elevated) { throw '-Machine runs elevated (install/admin.ps1, or this script without it)' }
    try {
        if (-not (Test-Path $exe)) { Say 'skip  Zebar is not installed'; exit 0 }
        $base = Get-Base
        if (-not $base) { Say 'skip  Zebar version unknown; official Zebar kept'; exit 0 }
        $release = Get-Release $base
        if (-not $release) { Say "ok  official Zebar $base (no fork build for it yet)"; exit 0 }
        if (Test-Current $release.tag_name) { Say "ok  Zebar fork $($release.tag_name)"; exit 0 }

        $zipAsset = $release.assets | Where-Object name -like 'zebar-*-windows-x64.zip' | Select-Object -First 1
        $sumsAsset = $release.assets | Where-Object name -eq 'SHA256SUMS-windows-x64.txt' | Select-Object -First 1
        $work = New-Staging
        try {
            Invoke-WebRequest $zipAsset.browser_download_url -OutFile "$work\fork.zip" -UseBasicParsing
            Invoke-WebRequest $sumsAsset.browser_download_url -OutFile "$work\sums.txt" -UseBasicParsing
            $want = ((Get-Content "$work\sums.txt" | Where-Object { $_ -match [regex]::Escape($zipAsset.name) } | Select-Object -First 1) -split '\s+')[0]
            if (-not $want -or (Get-FileHash "$work\fork.zip" -Algorithm SHA256).Hash -ne $want.ToUpper()) { throw "$($zipAsset.name) doesn't match its published SHA-256" }
            Expand-Archive "$work\fork.zip" "$work\x" -Force
            if (-not (Test-Path $official)) { Copy-Item $exe $official }
            Set-InUseFile "$work\x\zebar.exe" $exe
            New-Item -ItemType Directory -Force (Split-Path $state) | Out-Null
            Set-Content $state $release.tag_name -NoNewline
        } finally { Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue }
        Say "installed  Zebar fork $($release.tag_name) (the official one kept as zebar.official.exe)"
        exit 0
    } catch { Say "failed  $_"; exit 1 }
}
if ($RestoreMachine) {
    if (-not $elevated) { throw '-RestoreMachine runs elevated' }
    if (Test-Path $official) { Set-InUseFile $official $exe; Remove-Item $official -ErrorAction SilentlyContinue }
    Remove-Item $state -ErrorAction SilentlyContinue
    Say 'ok  official Zebar back (restart the bar: omawin restart zebar)'
    exit 0
}

# --- this account's part -------------------------------------------------------------------------
if ($Check) {
    if (-not (Test-Path $exe)) { return 'needed' }
    $base = Get-Base
    if (-not $base) { return 'skip  Zebar version unknown' }
    try { $release = Get-Release $base } catch { return "skip  couldn't reach GitHub: $_" }
    if (-not $release) { return 'skip  no fork build for this Zebar yet' }
    if (Test-Current $release.tag_name) { return "ok  Zebar fork $($release.tag_name)" }
    return 'needed'
}
if ($After) {
    try { winget pin add --id glzr-io.zebar --exact --accept-source-agreements 2>$null | Out-Null } catch { }
    return
}
if (-not (Test-Path $exe)) { 'Zebar not installed; re-run after installing it'; return }
if ($Restore) { Invoke-Elevated '-RestoreMachine'; return }
$line = if ($elevated) { & $PSCommandPath -Machine } else { Invoke-Elevated '-Machine' }
& $PSCommandPath -After
$line
