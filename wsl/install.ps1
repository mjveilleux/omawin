# The wsl preset's installer (packages.ps1): Ubuntu in WSL for this account, set up by
# wsl/setup.sh (the tools, omawin's Neovim config, herdr, the agents this account has on Windows).
# A distro is per account, so each account that picks the preset gets its own. Needs WSL itself,
# which takes admin and a restart (not part of the admin step yet). Runs under Windows PowerShell
# 5.1. Safe to run again: setup.sh keeps what's there.
$ErrorActionPreference = 'Stop'
$Repo = Split-Path $PSScriptRoot
$Distro = 'Ubuntu'   # the name omawin.exe and the menu app look for
$env:WSL_UTF8 = '1'  # wsl.exe's own messages in UTF-8, not UTF-16

# wsl.exe's exit code, with what it printed passed on.
function Invoke-Wsl {
    $wslArgs = $args   # (a script block below has $args of its own)
    & { $ErrorActionPreference = 'Continue'; & wsl.exe @wslArgs 2>&1 } | ForEach-Object { if ("$_".Trim()) { "$_" } }
    if ($LASTEXITCODE -ne 0) { throw "wsl $($args -join ' ') failed ($LASTEXITCODE)" }
}

if (-not (Get-Command wsl.exe -ErrorAction SilentlyContinue) -or -not (Test-Path "$env:ProgramFiles\WSL\wsl.exe")) {
    throw "WSL isn't on this PC: in an admin PowerShell run 'wsl --install --no-distribution', restart, then 'omawin preset add wsl'"
}

$distros = @(& wsl.exe --list --quiet 2>$null | ForEach-Object { "$_".Trim() })
if ($distros -notcontains $Distro) {
    'installing Ubuntu (a download of about 400 MB)'
    Invoke-Wsl --install -d $Distro --no-launch
}

# The Linux user: this account's name, as Linux allows it.
$user = ([Environment]::UserName.ToLower() -replace '[^a-z0-9_-]', '')
if ($user -notmatch '^[a-z_]') { $user = "u$user" }

# The default user applies from WSL's next start, so the distro is stopped below when setup.sh
# makes it this user. (Only then: stopping it ends whatever runs in it.)
$wasDefault = ((& wsl.exe -d $Distro -u root '--' cat /etc/wsl.conf 2>$null) -join "`n") -match "(?m)^default=$user\s*$"
Invoke-Wsl -d $Distro -u root --cd "$Repo\wsl" '--' bash ./setup.sh root $user

# Its password, for sudo: asked for here when someone is at the console, else said how.
$status = (& wsl.exe -d $Distro -u root '--' passwd -S $user 2>$null) -join ''
if ($status -match "^$user (L|NP) ") {
    if ([Environment]::UserInteractive -and -not [Console]::IsInputRedirected) {
        "a password for $user in Ubuntu (sudo asks for it):"
        & wsl.exe -d $Distro -u root '--' passwd $user
    } else {
        "set $user's password in Ubuntu (for sudo): wsl -d $Distro -u root passwd $user"
    }
}

if (-not $wasDefault) { Invoke-Wsl --terminate $Distro }

# The agents this account has on Windows, in Ubuntu too (Ubuntu projects open a tab for each).
$agents = @('claude', 'opencode' | Where-Object { Get-Command $_ -ErrorAction SilentlyContinue })
Invoke-Wsl -d $Distro -u $user --cd "$Repo\wsl" '--' bash ./setup.sh user @agents
"Ubuntu is set up: Win+Space > Projects lists its repos; nvim there is omawin's"
