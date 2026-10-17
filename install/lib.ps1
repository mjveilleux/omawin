# install.ps1's shared helpers: output, junctions, stub files. Dot-sourced by install.ps1
# before any step, so each step (and install.ps1 -Step <name>) can use them.
# Runs under Windows PowerShell 5.1 too (a fresh PC has no pwsh yet).

# The output (Write-Step, Write-Note, Write-Warn, the step tracker) and its log: install/ui.ps1.
. "$PSScriptRoot\ui.ps1"
Initialize-InstallUI (Split-Path -Leaf $MyInvocation.PSCommandPath)

# Your user layer: what's yours and not omawin's (your git identity, Claude settings, launcher
# items, profile additions), in a folder of your own next to the repo (docs/work.md). Each step
# picks up what it finds there and works without it. Where it is: bin/user-layer.ps1
# (~/omawin-work, or OMAWIN_USER_LAYER).
$UserLayer = & "$Repo\bin\user-layer.ps1"

# Whether a person is there to answer a question (not a scheduled or piped run).
function Test-Interactive {
    [Environment]::UserInteractive -and -not [Console]::IsInputRedirected -and
        -not ([Environment]::GetCommandLineArgs() -match '^-NonI')
}

# The presets this PC picked (packages.ps1 has them), one name per line.
$PresetsFile = "$env:LOCALAPPDATA\launcher\presets.txt"
function Get-Presets {
    @(Get-Content $PresetsFile -ErrorAction SilentlyContinue | ForEach-Object { $_.Trim() } |
        Where-Object { $_ -and -not $_.StartsWith('#') })
}

function Test-Admin {
    ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Update-SessionPath {
    # Pick up PATH changes from installers without opening a new shell.
    $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' +
        [Environment]::GetEnvironmentVariable('Path', 'User')
}

function Add-UserPath([string]$Dir, [switch]$Prepend) {
    # Put $Dir on the user PATH unless it's there already. The PATH is read and written raw
    # (REG_EXPAND_SZ), so entries like %LOCALAPPDATA%\Handy stay unexpanded, and entries are
    # compared expanded, so C:\...\Handy and %LOCALAPPDATA%\Handy count as the same one.
    # [Environment]::SetEnvironmentVariable would write the expanded PATH back as REG_SZ.
    # Returns $true if it was added.
    $key = Get-Item 'HKCU:\Environment'
    $raw = [string]$key.GetValue('Path', '', 'DoNotExpandEnvironmentNames')
    $norm = { param($p) [Environment]::ExpandEnvironmentVariables($p).TrimEnd('\').ToLowerInvariant() }
    $want = & $norm $Dir
    foreach ($entry in $raw -split ';') { if ($entry -and (& $norm $entry) -eq $want) { return $false } }
    $raw = $raw.Trim(';')
    $new = if (-not $raw) { $Dir } elseif ($Prepend) { "$Dir;$raw" } else { "$raw;$Dir" }
    Set-ItemProperty 'HKCU:\Environment' -Name Path -Value $new -Type ExpandString
    # Tell running programs (Explorer) the environment changed, as SetEnvironmentVariable does.
    [Environment]::SetEnvironmentVariable('OMAWIN_PATH_NUDGE', '1', 'User')
    [Environment]::SetEnvironmentVariable('OMAWIN_PATH_NUDGE', $null, 'User')
    return $true
}

function Move-Aside([string]$Path) {
    if (Test-Path -LiteralPath $Path) {
        $backup = "$Path.backup-$Stamp"
        Move-Item -LiteralPath $Path -Destination $backup
        Write-Note "backed up existing $Path -> $backup"
    }
}

function Set-Junction([string]$Link, [string]$Target) {
    # Point the directory $Link at $Target (a folder in this repo).
    $item = Get-Item -LiteralPath $Link -Force -ErrorAction SilentlyContinue
    if ($item -and $item.LinkType -eq 'Junction' -and (@($item.Target)[0].TrimEnd('\') -eq $Target.TrimEnd('\'))) {
        Write-Note "ok  $Link"
        return
    }
    if ($item -and $item.LinkType) { Remove-Item -LiteralPath $Link -Force }  # stale link: drop it
    else { Move-Aside $Link }
    New-Item -ItemType Directory -Force -Path (Split-Path $Link) | Out-Null
    New-Item -ItemType Junction -Path $Link -Target $Target | Out-Null
    Write-Note "linked $Link -> $Target"
}

function Set-FileContent([string]$Path, [string]$Content) {
    # Write a small stub file. Whatever was there is backed up, unless it's this stub already
    # or an older one of ours (same first line, or "# Managed by ...": from before the repo moved).
    $old = if (Test-Path -LiteralPath $Path) { Get-Content -LiteralPath $Path -Raw }
    if ($old -eq $Content) { Write-Note "ok  $Path"; return }
    $ours = $old -and ($old.StartsWith(($Content -split "`n")[0]) -or $old.StartsWith('# Managed by '))
    if (-not $ours) { Move-Aside $Path }
    New-Item -ItemType Directory -Force -Path (Split-Path $Path) | Out-Null
    [IO.File]::WriteAllText($Path, $Content)
    Write-Note "wrote $Path"
}
