# install.ps1's shared helpers: output, junctions, stub files. Dot-sourced by install.ps1
# before any step, so each step (and install.ps1 -Step <name>) can use them.
# Runs under Windows PowerShell 5.1 too (a fresh PC has no pwsh yet).

# The output (Write-Step, Write-Note, Write-Warn, the step tracker) and its log: install/ui.ps1.
. "$PSScriptRoot\ui.ps1"
Initialize-InstallUI (Split-Path -Leaf $MyInvocation.PSCommandPath)

# Your account folder: what's yours and not omawin's (your git identity, Claude settings, launcher
# items, profile additions), in a folder of your own next to the repo (docs/work.md). Each step
# picks up what it finds there and works without it. Where it is: bin/user-layer.ps1
# (~/omawin-account, or OMAWIN_USER_LAYER).
$UserLayer = & "$Repo\bin\user-layer.ps1"

# install.ps1's steps, from install/steps.txt: Name, Summary, Seconds (a first guess), in order.
function Get-InstallSteps {
    foreach ($line in Get-Content "$Repo\install\steps.txt") {
        if ($line -match '^\s*(#|$)') { continue }
        $f = @($line -split '\|' | ForEach-Object { $_.Trim() })
        [pscustomobject]@{ Name = $f[0]; Summary = $(if ($f.Count -gt 1) { $f[1] }); Seconds = $(if ($f.Count -gt 2 -and $f[2]) { [double]$f[2] } else { 0 }) }
    }
}

# Your git name and email: never in the repo. Asked once if git has none, into your own
# ~/.gitconfig (--includes: an identity from your account folder's gitconfig counts, even before
# the shell step includes it). install.ps1 asks it before anything starts, with its other
# questions; $true once it's settled (there, answered or skipped), $false when git isn't here yet
# and the shell step should ask after all.
function Request-GitIdentity {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) { return $false }
    $ErrorActionPreference = 'Continue'
    $name = git config --global --includes --get user.name 2>$null
    $mine = "$UserLayer\git\gitconfig"
    if (-not $name -and (Test-Path $mine)) { $name = git config --file $mine --get user.name 2>$null }
    if ($name) { Write-Note "ok  git user.name $name"; return $true }
    if (-not (Test-Interactive)) { Write-Warn 'no git identity: git config --global user.name "Your Name"; git config --global user.email you@example.com'; return $true }
    $name = (Read-InstallAnswer 'git: your name, for commits (Enter to skip)').Trim()
    if (-not $name) { return $true }
    $email = (Read-InstallAnswer 'git: your email').Trim()
    git config --global user.name $name; Write-Note "git user.name = $name"
    if ($email) { git config --global user.email $email; Write-Note "git user.email = $email" }
    $true
}

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

# The Linux choices for WSL (bin/defaults.txt's linux rows: Name, Label, Distro, Arch first), and
# the one this PC picked (omawin default linux), else the one this account has, else the first.
function Get-LinuxChoices {
    foreach ($line in Get-Content "$Repo\bin\defaults.txt") {
        $p = @($line -split '\|' | ForEach-Object { $_.Trim() })
        if ($p.Count -ge 5 -and $p[0] -eq 'linux') { [pscustomobject]@{ Name = $p[1]; Label = ($p[2] -split ',')[0]; Distro = $p[4] } }
    }
}
$LinuxPickFile = "$env:LOCALAPPDATA\launcher\defaults\linux"
function Get-LinuxPick {
    $choices = @(Get-LinuxChoices)
    $picked = if (Test-Path $LinuxPickFile) { "$(Get-Content $LinuxPickFile -TotalCount 1)".Trim() }
    $have = @(Get-WslDistros)
    @($choices | Where-Object Name -eq $picked) + @($choices | Where-Object { $have -contains $_.Distro }) + $choices | Select-Object -First 1
}

# WSL itself: the WSL package (what `wsl --install` puts in). And this account's distros, from
# WSL's registry, so WSL isn't started to ask.
function Test-WslInstalled { Test-Path "$env:ProgramFiles\WSL\wsl.exe" }
function Get-WslDistros {
    Get-ChildItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Lxss' -ErrorAction SilentlyContinue |
        ForEach-Object { $_.GetValue('DistributionName') } | Where-Object { $_ }
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
        Add-InstallItem 'Settings' $Link (Split-Path $Link -Leaf) 'have'
        return
    }
    if ($item -and $item.LinkType) { Remove-Item -LiteralPath $Link -Force }  # stale link: drop it
    else { Move-Aside $Link }
    New-Item -ItemType Directory -Force -Path (Split-Path $Link) | Out-Null
    New-Item -ItemType Junction -Path $Link -Target $Target | Out-Null
    Write-Note "linked $Link -> $Target"
    Add-InstallItem 'Settings' $Link (Split-Path $Link -Leaf) 'done'
}

function Set-FileContent([string]$Path, [string]$Content) {
    # Write a small stub file. Whatever was there is backed up, unless it's this stub already
    # or an older one of ours (same first line, or "# Managed by ...": from before the repo moved).
    $old = if (Test-Path -LiteralPath $Path) { Get-Content -LiteralPath $Path -Raw }
    if ($old -eq $Content) { Write-Note "ok  $Path"; Add-InstallItem 'Settings' $Path (Split-Path $Path -Leaf) 'have'; return }
    $ours = $old -and ($old.StartsWith(($Content -split "`n")[0]) -or $old.StartsWith('# Managed by '))
    if (-not $ours) { Move-Aside $Path }
    New-Item -ItemType Directory -Force -Path (Split-Path $Path) | Out-Null
    [IO.File]::WriteAllText($Path, $Content)
    Write-Note "wrote $Path"
    Add-InstallItem 'Settings' $Path (Split-Path $Path -Leaf) 'done'
}
