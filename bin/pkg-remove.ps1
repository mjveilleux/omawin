<#
.SYNOPSIS
    omawin pkg remove: what winget installed here, fuzzy found and uninstalled (Omarchy's
    omarchy-pkg-remove, for winget).

.DESCRIPTION
    The packages on this PC that winget knows, in fzf with the same preview as omawin pkg install
    (bin/pkg-preview.ps1). Tab picks several, Enter uninstalls them, one after another, right here.

        Tab  pick (several)    Enter  remove    Alt+P  preview on / off    Alt+J / Alt+K  scroll it

    What's here is asked of winget each time (winget export, a few seconds: removing something
    by its list from yesterday would be worse than waiting).
#>
param([string]$Query = '')
$ErrorActionPreference = 'Stop'
$e = [char]27
if (-not (Get-Command fzf -ErrorAction SilentlyContinue)) { throw 'fzf is missing (it is in the core: omawin install)' }

Write-Host "  $e[90mwhat's installed ...$e[0m"
$list = omawin-cli.exe pkg index | Select-Object -Last 1
if ($LASTEXITCODE) { throw "$list" }
$installedFile = Join-Path $env:LOCALAPPDATA 'launcher\installed.txt'
& (Join-Path (Split-Path $PSScriptRoot) 'default\launcher\menu.ps1') -RefreshInstalled
$here = [Collections.Generic.HashSet[string]]::new()
if (Test-Path $installedFile) { foreach ($l in Get-Content $installedFile) { if ($l.StartsWith('winget:')) { [void]$here.Add($l.Substring(7).Trim()) } } }

$lines = foreach ($line in [IO.File]::ReadLines($list)) {
    $f = $line -split "`t"
    if ($here.Contains($f[0].ToLower())) { "$($f[0])`t$($f[1])  $e[2m$($f[0])  $($f[3])$e[0m" }
}
if (-not $lines) { Write-Host '  nothing winget installed here'; return }
$source = Join-Path $env:TEMP "omawin-pkg-$PID.txt"
[IO.File]::WriteAllLines($source, [string[]]$lines, (New-Object Text.UTF8Encoding $false))
[Console]::Write("$e[1A$e[2K")

$preview = "pwsh -NoProfile -NoLogo -File `"$PSScriptRoot\pkg-preview.ps1`" {1}"
$fzf = @(
    '--multi', '--ansi', '--delimiter', "`t", '--with-nth', '2..', '--query', $Query, '--tiebreak', 'begin,length',
    '--prompt', 'remove > ', '--marker', ([string][char]0x2717), '--pointer', ([string][char]0x25B8),
    '--header', 'Tab pick  Enter remove  Alt+P preview  Esc quit', '--header-first',
    '--preview', $preview, '--preview-window', 'down:45%:wrap', '--preview-label', ' winget ',
    '--bind', 'alt-p:toggle-preview,alt-j:preview-down,alt-k:preview-up',
    '--color', 'pointer:red,marker:red,prompt:red,header:gray'
)
try {
    $picked = @(Get-Content $source -Encoding utf8 | & fzf @fzf)
} finally { Remove-Item $source -ErrorAction SilentlyContinue }
if (-not $picked) { return }

$ids = @($picked | ForEach-Object { ($_ -split "`t")[0] })
$failed = @()
foreach ($id in $ids) {
    Write-Host "`n  $e[31m$([char]0x25CF)$e[0m removing $id"
    winget uninstall --id $id --exact --accept-source-agreements
    if ($LASTEXITCODE -eq 0) {
        (Get-Content $installedFile) | Where-Object { $_ -ne "winget:$($id.ToLower())" } | Set-Content $installedFile -Encoding utf8
        Write-Host "  $e[32m$([char]0x2713)$e[0m $id removed"
    } else { $failed += $id; Write-Host "  $e[31m$([char]0x2717)$e[0m $id (winget exit $LASTEXITCODE)" }
}
Write-Host "`n  $($ids.Count - $failed.Count) of $($ids.Count) removed$(if ($failed) { "; not: $($failed -join ', ')" })"
if ($failed) { exit 1 }
