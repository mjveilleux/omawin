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

    1. downloads the fork's zebar.exe built on the installed Zebar's version (skipped, with
       the official one kept, when there's no fork build for it yet)
    2. checks it against the release's SHA256SUMS
    3. puts it in Program Files (admin: one prompt), keeping the official one as
       zebar.official.exe, and pins Zebar in winget so an upgrade doesn't undo it

    Safe to run again; -Restore puts the official zebar.exe back. Runs under Windows
    PowerShell 5.1 too.

        .\zebar-fork.ps1
        .\zebar-fork.ps1 -Restore
#>
param([switch]$Restore, [string]$From)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor 3072

$dir = Join-Path $env:ProgramFiles 'glzr.io\Zebar'
$exe = Join-Path $dir 'zebar.exe'
$official = Join-Path $dir 'zebar.official.exe'
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

# --- the admin part ------------------------------------------------------------------------
if ($From -and $elevated) {
    if (-not (Test-Path $official)) { Copy-Item $exe $official }
    Set-InUseFile $From $exe
    exit 0
}
if ($Restore -and $elevated) {
    if (Test-Path $official) { Set-InUseFile $official $exe; Remove-Item $official -ErrorAction SilentlyContinue }
    exit 0
}

# --- your part -----------------------------------------------------------------------------
function Invoke-Elevated([string[]]$Arguments) {
    $shell = if (Get-Command pwsh -ErrorAction SilentlyContinue) { 'pwsh' } else { 'powershell' }
    $p = Start-Process $shell -Verb RunAs -Wait -PassThru -WindowStyle Hidden -ArgumentList (@('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"") + $Arguments)
    if ($p.ExitCode -ne 0) { throw "the admin part failed ($($p.ExitCode))" }
}

if (-not (Test-Path $exe)) { 'Zebar not installed; re-run after installing it'; return }
if ($Restore) { Invoke-Elevated '-Restore'; 'ok  official Zebar back (restart the bar: omawin restart zebar)'; return }

# The installed Zebar's version: from winget (zebar.exe's own file version is 0.0.0).
$base = $null
try { $line = winget list --id glzr-io.zebar --exact --accept-source-agreements 2>$null | Select-String 'glzr-io\.zebar'; if ($line -match '(\d+\.\d+\.\d+)') { $base = $Matches[1] } } catch { }
if (-not $base) { 'Zebar version unknown; official Zebar kept'; return }

$releases = Invoke-RestMethod 'https://api.github.com/repos/mjveilleux/zebar/releases' -UseBasicParsing
$release = $releases | Where-Object { $_.tag_name -like "v$base-omawin.*" } | Sort-Object { [int]($_.tag_name -replace '.*omawin\.', '') } | Select-Object -Last 1
if (-not $release) { "ok  official Zebar $base (no fork build for $base yet)"; return }
$state = Join-Path $env:LOCALAPPDATA 'launcher\zebar-fork.txt'
if ((Test-Path $official) -and (Test-Path $state) -and (Get-Content $state -Raw).Trim() -eq $release.tag_name) { "ok  Zebar fork $($release.tag_name)"; return }

$zipAsset = $release.assets | Where-Object name -like 'zebar-*-windows-x64.zip' | Select-Object -First 1
$sumsAsset = $release.assets | Where-Object name -eq 'SHA256SUMS-windows-x64.txt' | Select-Object -First 1
$work = Join-Path $env:TEMP "zebar-fork-$(Get-Date -Format yyyyMMddHHmmss)"
New-Item -ItemType Directory -Force $work | Out-Null
try {
    Invoke-WebRequest $zipAsset.browser_download_url -OutFile "$work\fork.zip" -UseBasicParsing
    Invoke-WebRequest $sumsAsset.browser_download_url -OutFile "$work\sums.txt" -UseBasicParsing
    $want = ((Get-Content "$work\sums.txt" | Where-Object { $_ -match [regex]::Escape($zipAsset.name) } | Select-Object -First 1) -split '\s+')[0]
    if (-not $want -or (Get-FileHash "$work\fork.zip" -Algorithm SHA256).Hash -ne $want.ToUpper()) { throw "$($zipAsset.name) doesn't match its published SHA-256" }
    Expand-Archive "$work\fork.zip" "$work\x" -Force
    Invoke-Elevated @('-From', "`"$work\x\zebar.exe`"")
    New-Item -ItemType Directory -Force (Split-Path $state) | Out-Null
    Set-Content $state $release.tag_name -NoNewline
    try { winget pin add --id glzr-io.zebar --exact --accept-source-agreements 2>$null | Out-Null } catch { }
    "ok  Zebar fork $($release.tag_name) (the official one kept as zebar.official.exe)"
} finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
