<#
.SYNOPSIS
    This Windows account's own port for GlazeWM.

.DESCRIPTION
    GlazeWM's IPC server (which the bar, the launcher and its CLI talk to) listens on a localhost
    port, and every account signed in on the PC shares localhost. With the official 6123, a
    second account's desktop takes the first one's port, and its commands reach the other
    account's window manager. So each account gets one of its own, from its account number (the
    last part of its SID: 1001, 1002, ...), always the same for that account and never another's:

        GLAZEWM_IPC_PORT = 22000 + 2 * (number mod 4000)

    Our fork reads it (github.com/mjveilleux/glazewm; the official build ignores it and keeps
    6123). Set as a user environment variable, so whatever starts after sign-in has it; this
    session gets it too. ZEBAR_ASSET_PORT, Zebar's from before it was removed, is cleared.

        .\ports.ps1        prints it
        .\ports.ps1 -Set   saves it for this account (install's desktop step); says when it changed
#>
param([switch]$Set)

$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$number = [int64]($sid.Split('-')[-1])
$glazewm = 22000 + 2 * ($number % 4000)
$ports = [ordered]@{ GLAZEWM_IPC_PORT = "$glazewm"; ZEBAR_ASSET_PORT = $null }

if (-not $Set) { [pscustomobject]@{ GLAZEWM_IPC_PORT = $ports.GLAZEWM_IPC_PORT }; return }

# Only with the fork in place: the official program keeps 6123 whatever this says, and the
# launcher would look for GlazeWM where it isn't. Told from Program Files itself, which every
# account shares (another account may have put the fork in, or put the official one back): the
# fork installer keeps the official CLI it replaced as glazewm.official.exe, and what runs is
# signed as ours. omawin.exe decides the same way at every sign-in (bin/omawin.cs, SetPorts); so
# does check.ps1. The tools bundle's per-user GlazeWM isn't the fork: the official port.
$glzr = Join-Path $env:ProgramFiles 'glzr.io'
$sig = Get-AuthenticodeSignature "$glzr\GlazeWM\glazewm.exe" -ErrorAction SilentlyContinue
$ready = $sig -and $sig.SignerCertificate -and $sig.SignerCertificate.Subject -like 'CN=omawin code signing*' -and
    (Test-Path "$glzr\GlazeWM\cli\glazewm.official.exe")
if (-not $ready) { $ports.GLAZEWM_IPC_PORT = $null }

$changed = $false
foreach ($name in @($ports.Keys)) {
    if ([Environment]::GetEnvironmentVariable($name, 'User') -ne $ports[$name]) {
        [Environment]::SetEnvironmentVariable($name, $ports[$name], 'User')
        $changed = $true
    }
    if ($ports[$name]) { Set-Item "env:$name" $ports[$name] } else { Remove-Item "env:$name" -ErrorAction SilentlyContinue }
}
if (-not $ready) { 'official port (6123): omawin''s GlazeWM isn''t in, so only one account''s desktop runs at a time'; return }
if ($changed) { "port for this account: GlazeWM $($ports.GLAZEWM_IPC_PORT) (sign out and back in to use it)" }
else { "ok  this account's port: GlazeWM $($ports.GLAZEWM_IPC_PORT)" }
