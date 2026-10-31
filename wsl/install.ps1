# The wsl preset's installer (install/packages.ps1): this account's Linux in WSL, Arch (the default) or
# Ubuntu (`omawin default linux`, bin/defaults.txt), set up by wsl/setup.sh (the tools, omawin's
# Neovim config, herdr, the agents this account has on Windows). A distro is per account, so each
# account that picks the preset gets its own.
#
# WSL itself takes admin and a restart: install.ps1's one admin prompt installs it
# (install/admin-batch.ps1 -Wsl), and this never asks for admin. Until Windows has restarted it
# only arranges to finish at the next sign-in. Runs under Windows PowerShell 5.1. Safe to run
# again: setup.sh keeps what's there.
#
# Never your files: a distro this account had before omawin (one omawin didn't set up) is used as
# it is, never set up or changed, unless you ask (-Adopt). Without a pick, the Linux you already
# have is the one used, so terminals and projects keep opening where your files are. Nothing here
# removes a distro.
param(
    [string]$Linux,           # arch or ubuntu; else this PC's pick, else the one you have, else Arch
    [switch]$RestartPending,  # WSL was installed just now: finish after Windows restarts
    [switch]$Adopt            # set up a distro of yours too (setup.sh moves your configs aside first)
)
$ErrorActionPreference = 'Stop'
$Repo = Split-Path $PSScriptRoot
$env:WSL_UTF8 = '1'  # wsl.exe's own messages in UTF-8, not UTF-16
$State = "$env:LOCALAPPDATA\launcher"
$Waiting = "$State\wsl-restart"  # when WSL went in, while Windows hasn't restarted since

# The choices (bin/defaults.txt's linux rows) and the one wanted.
$choices = @(foreach ($line in Get-Content "$Repo\bin\defaults.txt") {
        $p = @($line -split '\|' | ForEach-Object { $_.Trim() })
        if ($p.Count -ge 5 -and $p[0] -eq 'linux') { [pscustomobject]@{ Name = $p[1]; Label = ($p[2] -split ',')[0]; Distro = $p[4] } }
    })
if (-not $Linux -and (Test-Path "$State\defaults\linux")) { $Linux = "$(Get-Content "$State\defaults\linux" -TotalCount 1)".Trim() }
$pick = $choices | Where-Object Name -eq $Linux | Select-Object -First 1
if (-not $pick) {
    if ($Linux) { throw "no Linux '$Linux'; the choices: $(($choices | ForEach-Object Name) -join ', ')" }
    $have = @(Get-ChildItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Lxss' -ErrorAction SilentlyContinue | ForEach-Object { $_.GetValue('DistributionName') })
    $pick = @($choices | Where-Object { $have -contains $_.Distro }) + $choices | Select-Object -First 1
}
$Distro = $pick.Distro   # the name WSL knows it by, which omawin.exe and the menu app use

# wsl.exe's exit code, with what it printed passed on.
function Invoke-Wsl {
    $wslArgs = $args   # (a script block below has $args of its own)
    & { $ErrorActionPreference = 'Continue'; & wsl.exe @wslArgs 2>&1 } | ForEach-Object { if ("$_".Trim()) { "$_" } }
    if ($LASTEXITCODE -ne 0) { throw "wsl $($args -join ' ') failed ($LASTEXITCODE)" }
}

# WSL went in moments ago (the admin step): it works once Windows restarts. Then this runs again
# by itself, at the next sign-in (RunOnce: this account's, no admin), in a window of its own.
if ($RestartPending) {
    New-Item -ItemType Directory -Force -Path $State | Out-Null
    Set-Content $Waiting ((Get-Date).ToUniversalTime().ToString('o'))
}
if (Test-Path $Waiting) {
    $since = [datetime]::Parse("$(Get-Content $Waiting -TotalCount 1)".Trim()).ToUniversalTime()
    $boot = (Get-CimInstance Win32_OperatingSystem).LastBootUpTime.ToUniversalTime()
    if ($boot -lt $since) {
        $command = "powershell.exe -NoProfile -ExecutionPolicy Bypass -NoExit -File `"$PSCommandPath`" -Linux $($pick.Name)"
        Set-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\RunOnce' 'omawin linux' $command
        "$($pick.Label) is set up after Windows restarts: it carries on by itself when you sign in"
        return
    }
    Remove-Item $Waiting
}

if (-not (Test-Path "$env:ProgramFiles\WSL\wsl.exe")) {
    throw "WSL isn't on this PC yet, and putting it in takes admin once: omawin preset add wsl (one admin prompt, then a restart)"
}

# The Linux user: this account's name, as Linux allows it.
$user = ([Environment]::UserName.ToLower() -replace '[^a-z0-9_-]', '')
if ($user -notmatch '^[a-z_]') { $user = "u$user" }

# What --wsl opens from now on (omawin default linux).
function Save-Pick {
    New-Item -ItemType Directory -Force -Path "$State\defaults" | Out-Null
    Set-Content "$State\defaults\linux" $pick.Name -NoNewline
}

$distros = @(& wsl.exe --list --quiet 2>$null | ForEach-Object { "$_".Trim() })
if ($distros -notcontains $Distro) {
    "installing $($pick.Label) (a download of a few hundred MB)"
    Invoke-Wsl --install -d $Distro --no-launch
} elseif (-not $Adopt) {
    # There already: omawin's if its setup ran there (/etc/omawin, or the bashrc block of an older
    # setup), else yours, and left exactly as it is.
    & wsl.exe -d $Distro -u root '--' sh -c "test -f /etc/omawin || grep -qs '^# >>> omawin >>>' /home/$user/.bashrc" 2>$null
    if ($LASTEXITCODE -ne 0) {
        Save-Pick
        "$($pick.Label) is yours (omawin didn't set it up), so it's left as it is: terminals and projects open in it. To give it omawin's tools too: wsl\install.ps1 -Adopt (your configs are moved aside, never deleted)"
        return
    }
}

# Windows' folders for setup.sh (the theme file, the account folder's overrides, the home).
. "$PSScriptRoot\env.ps1"

# /etc/wsl.conf (the default user, systemd, interop) applies from WSL's next start, so the distro
# is stopped below when setup.sh changed it. (Only then: stopping it ends whatever runs in it.)
$confBefore = (& wsl.exe -d $Distro -u root --exec cat /etc/wsl.conf 2>$null) -join "`n"
Invoke-Wsl -d $Distro -u root --cd "$Repo\wsl" '--' bash ./setup.sh root $user
if (((& wsl.exe -d $Distro -u root --exec cat /etc/wsl.conf 2>$null) -join "`n") -ne $confBefore) { Invoke-Wsl --terminate $Distro }

# The agents this account has on Windows, in Linux too (Linux projects open a tab for each).
$agents = @('claude', 'opencode' | Where-Object { Get-Command $_ -ErrorAction SilentlyContinue })
Invoke-Wsl -d $Distro -u $user --cd "$Repo\wsl" '--' bash ./setup.sh user @agents

# Windows Terminal's own profile for this distro (its WSL generator makes one per distro) opens in
# Linux's home rather than the Windows folder WSL would translate, through a fragment of omawin's:
# settings.json stays yours (anything you set there wins), and a Linux omawin didn't set up gets
# none. Terminal names that profile by a name-based (v5) UUID of the distro's name in UTF-16
# under its profile namespace (Ubuntu's is the well-known {2c4de342-38b7-51cf-b940-2309a097f518}).
# omawin's own windows pass wsl.exe themselves, so Win+Enter doesn't depend on it.
function Get-TerminalWslGuid([string]$Name) {
    $ns = [byte[]](0x2B, 0xDE, 0x4A, 0x90, 0xD0, 0x5F, 0x40, 0x1C, 0x94, 0x92, 0xE4, 0x08, 0x84, 0xEA, 0xD1, 0xD8)
    $sha = [Security.Cryptography.SHA1]::Create()
    $b = [byte[]]$sha.ComputeHash([byte[]]($ns + [Text.Encoding]::Unicode.GetBytes($Name)))[0..15]
    $b[6] = ($b[6] -band 0x0F) -bor 0x50   # version 5
    $b[8] = ($b[8] -band 0x3F) -bor 0x80   # RFC 4122 variant
    $hex = -join ($b | ForEach-Object { $_.ToString('x2') })
    '{' + $hex.Substring(0, 8) + '-' + $hex.Substring(8, 4) + '-' + $hex.Substring(12, 4) + '-' + $hex.Substring(16, 4) + '-' + $hex.Substring(20) + '}'
}
$fragments = "$env:LOCALAPPDATA\Microsoft\Windows Terminal\Fragments\omawin"
New-Item -ItemType Directory -Force -Path $fragments | Out-Null
$wtProfile = [ordered]@{ updates = (Get-TerminalWslGuid $Distro); startingDirectory = '~'; hidden = $false }
$fragment = [ordered]@{
    '$help'  = "Written by omawin's wsl/install.ps1: Windows Terminal's own $($pick.Label) profile, opening in Linux's home. Rewritten each setup; settings.json wins over it."
    profiles = @($wtProfile)
}
[IO.File]::WriteAllText("$fragments\linux-$Distro.json", ($fragment | ConvertTo-Json -Depth 4), (New-Object Text.UTF8Encoding $false))

Save-Pick
"$($pick.Label) is set up: Win+Space > Projects lists its repos; nvim there is omawin's"
