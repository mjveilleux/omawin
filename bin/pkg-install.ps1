<#
.SYNOPSIS
    omawin pkg install: any of winget's ~16,000 packages, fuzzy found and installed (Omarchy's
    omarchy-pkg-install, for winget).

.DESCRIPTION
    Every package winget knows (omawin pkg index reads its catalog off the PC, no network) in
    fzf: type to narrow by name, id or tag, the preview shows what it is (bin/pkg-preview.ps1),
    Tab picks several, Enter installs them with winget, one after another, right here. The ones
    already here are marked.

        Tab  pick (several)    Enter  install    Alt+P  preview on / off    Alt+J / Alt+K  scroll it

    The system menu's Install > Package runs it in a floating window. What's here comes from
    %LOCALAPPDATA%\launcher\installed.txt (default/launcher/menu.ps1 keeps it; refreshed in the background
    when it's older than a day), so the list opens at once.
#>
param([string]$Query = '')
$ErrorActionPreference = 'Stop'
$e = [char]27
if (-not (Get-Command fzf -ErrorAction SilentlyContinue)) { throw 'fzf is missing (it is in the core: omawin install)' }

$list = omawin-cli.exe pkg index | Select-Object -Last 1
if ($LASTEXITCODE) { throw "$list" }
$installedFile = Join-Path $env:LOCALAPPDATA 'launcher\installed.txt'
$here = [Collections.Generic.HashSet[string]]::new()
if (Test-Path $installedFile) { foreach ($l in Get-Content $installedFile) { [void]$here.Add($l.Trim()) } }
if (-not (Test-Path $installedFile) -or (Get-Item $installedFile).LastWriteTime -lt (Get-Date).AddDays(-1)) {
    Start-Process pwsh -WindowStyle Hidden -ArgumentList '-NoProfile', '-File', "`"$(Join-Path (Split-Path $PSScriptRoot) 'default\launcher\menu.ps1')`"", '-RefreshInstalled'
}

# One line a package: its id (hidden; fzf hands it to the preview and back), then what you see:
# a mark if it's here, its name, and dim, its id and first tags.
$check = [char]0x2713
$ascii = [regex]'^[\x20-\x7e]+$'
$lines = foreach ($line in [IO.File]::ReadLines($list)) {
    $f = $line.Split("`t")
    $mark = if ($here.Contains("winget:$($f[0].ToLower())")) { "$e[32m$check$e[0m" } else { ' ' }
    # The first few tags you could type (some are in other scripts), to find it by what it does.
    $tags = ''
    if ($f[4]) {
        $n = 0
        foreach ($t in $f[4].Split(',')) { $t = $t.Trim(); if ($n -lt 4 -and $ascii.IsMatch($t)) { $tags += " $t"; $n++ } }
        if ($tags) { $tags = "  $e[90m$($tags.Trim())$e[0m" }
    }
    "$($f[0])`t$mark $($f[1])  $e[2m$($f[0])$e[0m$tags"
}
$source = Join-Path $env:TEMP "omawin-pkg-$PID.txt"
[IO.File]::WriteAllLines($source, [string[]]$lines, (New-Object Text.UTF8Encoding $false))

$preview = "pwsh -NoProfile -NoLogo -File `"$PSScriptRoot\pkg-preview.ps1`" {1}"
$fzf = @(
    '--multi', '--ansi', '--delimiter', "`t", '--with-nth', '2..', '--query', $Query, '--tiebreak', 'begin,length',
    '--prompt', 'install > ', '--marker', $check, '--pointer', ([string][char]0x25B8),
    '--header', 'Tab pick  Enter install  Alt+P preview  Esc quit', '--header-first',
    '--preview', $preview, '--preview-window', 'down:45%:wrap', '--preview-label', ' winget ',
    '--bind', 'alt-p:toggle-preview,alt-j:preview-down,alt-k:preview-up',
    '--color', 'pointer:green,marker:green,prompt:yellow,header:gray'
)
try {
    $picked = @(Get-Content $source -Encoding utf8 | & fzf @fzf)
} finally { Remove-Item $source -ErrorAction SilentlyContinue }
if (-not $picked) { return }

$ids = @($picked | ForEach-Object { ($_ -split "`t")[0] })
$failed = @()
foreach ($id in $ids) {
    Write-Host "`n  $e[33m$([char]0x25CF)$e[0m installing $id"
    winget install --id $id --exact --accept-source-agreements --accept-package-agreements
    # 0: installed; 0x8A15002B / 0x8A150061: already there; 0x8A150109: installed, restart wanted.
    if ($LASTEXITCODE -in 0, -1978335189, -1978335135, -1978334967) {
        Add-Content $installedFile "winget:$($id.ToLower())"
        Write-Host "  $e[32m$check$e[0m $id"
    } else { $failed += $id; Write-Host "  $e[31m$([char]0x2717)$e[0m $id (winget exit $LASTEXITCODE)" }
}
Write-Host "`n  $($ids.Count - $failed.Count) of $($ids.Count) installed$(if ($failed) { "; not: $($failed -join ', ')" })"
if ($failed) { exit 1 }
