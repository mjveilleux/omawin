# Files apps write no longer live in the omawin checkout, so `omawin update` never trips over them:
#
# - Windows Terminal: LocalState was a junction into windows-terminal/. It becomes a folder of its
#   own holding the settings.json you have now (Terminal's settings UI writes it) and Terminal's
#   state files. windows-terminal/settings.json stays in the repo, only to seed a new PC.
# - PowerShell: Documents\PowerShell was a junction into powershell/. It becomes a folder of its own
#   with two stubs that load omawin's profiles from the repo, plus what pwsh writes there:
#   powershell.config.json (Set-ExecutionPolicy) and Modules\ (Install-Module), copied over.
# - Neovim: lazy-lock.json and lazyvim.json are copied to nvim-data, where lazy.nvim and LazyVim
#   now keep them (nvim/lua/config/lazy.lua); one already there is kept.
#
# What each junction showed is copied to %LOCALAPPDATA%\omawin-backup\<date>\ first. The new folder
# is built beside the junction and swapped in only once it's complete; if the swap fails, the
# junction is put back. The repo's files are left where they are.
# Safe on a new PC (no junctions, no nvim files: nothing to do) and safe twice (a folder that's no
# longer a junction is left alone; files already in nvim-data are kept).
$ErrorActionPreference = 'Stop'
$Repo = Split-Path $PSScriptRoot
$backup = Join-Path $env:LOCALAPPDATA ('omawin-backup\' + (Get-Date -Format 'yyyy-MM-dd'))

# The folder $Name of an omawin checkout (this one, or one from before the repo moved) that the
# junction at $Link points to; nothing when $Link isn't such a junction.
function Get-RepoTarget([string]$Link, [string]$Name) {
    $item = Get-Item -LiteralPath $Link -Force -ErrorAction SilentlyContinue
    if (-not $item -or -not ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { return }
    $target = (@($item.Target) | Select-Object -First 1)
    if (-not $target) { return }
    $target = $target.TrimEnd('\')
    if ($target -eq "$Repo\$Name" -or $target -match "\\(omawin|windows-dotfiles)\\$([regex]::Escape($Name))$") { $target }
}

# Replace the junction $Link (pointing at $Target) with a real folder: $Fill gets the new folder's
# path and puts the files in it. The new folder is built in $BuildIn (beside the junction unless
# said otherwise) and moved into place, a rename on the same drive.
function Convert-Junction([string]$Link, [string]$Target, [scriptblock]$Fill, [string]$BuildIn = (Split-Path $Link)) {
    $saved = Join-Path $backup (Split-Path $Target -Leaf)
    New-Item -ItemType Directory -Force -Path $saved | Out-Null
    if (Test-Path -LiteralPath $Target) { Copy-Item -Path (Join-Path $Target '*') -Destination $saved -Recurse -Force }
    Write-Host "  backed up $Target -> $saved"

    # Built beside the junction by default, so it gets that folder's permissions.
    $new = Join-Path $BuildIn ((Split-Path $Link -Leaf) + '.omawin-new')
    if (Test-Path -LiteralPath $new) { Remove-Item -LiteralPath $new -Recurse -Force }  # a half-built one from a failed run
    New-Item -ItemType Directory -Path $new | Out-Null
    & $Fill $new

    # Directory.Delete without recursion removes the junction itself, never what it points to.
    [IO.Directory]::Delete($Link)
    $moved = $false
    for ($i = 0; $i -lt 10 -and -not $moved; $i++) {
        try { [IO.Directory]::Move($new, $Link); $moved = $true } catch { Start-Sleep -Milliseconds 300 }
    }
    if (-not $moved) {
        New-Item -ItemType Junction -Path $Link -Target $Target | Out-Null
        throw "couldn't put $new in place of $Link; the link is back as it was"
    }
    Write-Host "  $Link is now a folder of its own (was a link to $Target)"
}

# --- Windows Terminal ---------------------------------------------------------------------------
$wtPackage = Get-ChildItem "$env:LOCALAPPDATA\Packages" -Filter 'Microsoft.WindowsTerminal_*' -Directory -ErrorAction SilentlyContinue | Select-Object -First 1
if ($wtPackage) {
    $localState = Join-Path $wtPackage.FullName 'LocalState'
    $wtTarget = Get-RepoTarget $localState 'windows-terminal'
    if ($wtTarget) {
        Convert-Junction $localState $wtTarget {
            param($dir)
            foreach ($name in 'settings.json', 'state.json', 'elevated-state.json') {
                if (Test-Path (Join-Path $wtTarget $name)) { Copy-Item (Join-Path $wtTarget $name) $dir }
            }
        }
        # It's yours now: install seeds it only when it's gone (install/shell.ps1).
        $marker = Join-Path $env:LOCALAPPDATA 'launcher\seeded-windows-terminal'
        New-Item -ItemType Directory -Force -Path (Split-Path $marker) | Out-Null
        [IO.File]::WriteAllText($marker, "$localState\settings.json`n")
        if (Get-Process WindowsTerminal -ErrorAction SilentlyContinue) { Write-Host '  restart Windows Terminal so it reads its settings from there' }
    }
}

# --- PowerShell ---------------------------------------------------------------------------------
$psDir = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'PowerShell'
$psTarget = Get-RepoTarget $psDir 'powershell'
if ($psTarget) {
    # Documents is often synced by OneDrive, which can hold a new file open while it uploads it:
    # the folder is built outside it, in the backup folder when that's on the same drive (so the
    # move stays a rename).
    $psBuildIn = Split-Path $psDir
    if ([IO.Path]::GetPathRoot($backup) -eq [IO.Path]::GetPathRoot($psDir)) { New-Item -ItemType Directory -Force -Path $backup | Out-Null; $psBuildIn = $backup }
    Convert-Junction $psDir $psTarget {
        param($dir)
        # The same stubs install/shell.ps1 writes (it rewrites them when they change).
        foreach ($name in 'profile.ps1', 'Microsoft.PowerShell_profile.ps1') {
            $profilePath = ("$Repo\powershell\$name") -replace "'", "''"
            [IO.File]::WriteAllText((Join-Path $dir $name), "# Managed by omawin: loads $Repo\powershell\$name. Your own lines go in your user layer's powershell\ (omawin docs/work.md).`nif (Test-Path '$profilePath') { . '$profilePath' }`n")
        }
        if (Test-Path (Join-Path $psTarget 'powershell.config.json')) { Copy-Item (Join-Path $psTarget 'powershell.config.json') $dir }
        if (Test-Path (Join-Path $psTarget 'Modules')) { Copy-Item (Join-Path $psTarget 'Modules') $dir -Recurse }
    } $psBuildIn
}

# --- Neovim -------------------------------------------------------------------------------------
$nvimData = Join-Path $env:LOCALAPPDATA 'nvim-data'
foreach ($name in 'lazy-lock.json', 'lazyvim.json') {
    $from = Join-Path $Repo "nvim\$name"
    $to = Join-Path $nvimData $name
    if ((Test-Path $from) -and -not (Test-Path $to)) {
        New-Item -ItemType Directory -Force -Path $nvimData | Out-Null
        Copy-Item $from $to
        Write-Host "  copied nvim\$name -> $to"
    }
}
