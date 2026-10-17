<#
.SYNOPSIS
    This Windows account's own ports for GlazeWM and Zebar.

.DESCRIPTION
    GlazeWM (its IPC server, which the bar, the launcher and its CLI talk to) and Zebar (the
    server its widgets load from) listen on localhost ports, and every account signed in on
    the PC shares localhost. With the official 6123 and 6124, a second account's desktop takes
    the first one's ports, and its commands reach the other account's window manager. So each
    account gets a pair of its own, from its account number (the last part of its SID: 1001,
    1002, ...), always the same for that account and never another's:

        GLAZEWM_IPC_PORT = 22000 + 2 * (number mod 4000)     ZEBAR_ASSET_PORT = that + 1

    Our forks read them (github.com/mjveilleux/glazewm and /zebar; the official builds ignore
    them and keep 6123 / 6124). Set as user environment variables, so whatever starts after
    sign-in has them; this session gets them too.

        .\ports.ps1        prints them
        .\ports.ps1 -Set   saves them for this account (install's desktop step); says when they changed

    Runs under Windows PowerShell 5.1 too.
#>
param([switch]$Set)

$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$number = [int64]($sid.Split('-')[-1])
$glazewm = 22000 + 2 * ($number % 4000)
$ports = [ordered]@{ GLAZEWM_IPC_PORT = "$glazewm"; ZEBAR_ASSET_PORT = "$($glazewm + 1)" }

if (-not $Set) { [pscustomobject]$ports; return }

# Only with both forks in place (install/glazewm-fork.ps1 from omawin.7, which ships the CLI and
# watcher too; install/zebar-fork.ps1): the official programs keep 6123 / 6124 whatever these
# say, and the launcher would look for GlazeWM where it isn't. Without them, everything stays
# on the official ports (one account's desktop at a time).
$state = Join-Path $env:LOCALAPPDATA 'launcher'
$glazeTag = if (Test-Path "$state\glazewm-fork.txt") { (Get-Content "$state\glazewm-fork.txt" -Raw).Trim() } else { '' }
$ready = ($glazeTag -match 'omawin\.(\d+)$' -and [int]$Matches[1] -ge 7) -and (Test-Path "$state\zebar-fork.txt")
if (-not $ready) { $ports = [ordered]@{ GLAZEWM_IPC_PORT = $null; ZEBAR_ASSET_PORT = $null } }

$changed = $false
foreach ($name in @($ports.Keys)) {
    if ([Environment]::GetEnvironmentVariable($name, 'User') -ne $ports[$name]) {
        [Environment]::SetEnvironmentVariable($name, $ports[$name], 'User')
        $changed = $true
    }
    if ($ports[$name]) { Set-Item "env:$name" $ports[$name] } else { Remove-Item "env:$name" -ErrorAction SilentlyContinue }
}
if (-not $ready) { 'official ports (6123, 6124): the GlazeWM and Zebar forks aren''t both in, so only one account''s desktop runs at a time'; return }
if ($changed) { "ports for this account: GlazeWM $($ports.GLAZEWM_IPC_PORT), Zebar $($ports.ZEBAR_ASSET_PORT) (sign out and back in to use them)" }
else { "ok  this account's ports: GlazeWM $($ports.GLAZEWM_IPC_PORT), Zebar $($ports.ZEBAR_ASSET_PORT)" }
