<#
.SYNOPSIS
    What the GlazeWM that runs here can do beyond the official build: one name a line, from the
    omawin-features.txt our fork's release ships beside glazewm.exe (emit, animations.enabled,
    animations.window_close, ...). Nothing for the official GlazeWM, or a fork from before it.

.DESCRIPTION
    The GlazeWM that runs is the one omawin.exe starts (GlazeWMExe in bin/omawin.cs): Program
    Files' when it's there (the official build, or our fork signed on this PC), else this
    account's copy from the tools bundle (always our fork). In Program Files the file counts only
    beside a glazewm.exe signed as ours, so one left behind by an official update never makes the
    config name a command the official GlazeWM refuses.

    themes/render.ps1 writes GlazeWM's config for what's listed (the menu keys' emit, roadmap 1.2);
    bin/check.ps1 checks the menu app listens when it does.

    Runs under Windows PowerShell 5.1 too (render.ps1 runs there during install).
#>
$machine = Join-Path $env:ProgramFiles 'glzr.io\GlazeWM'
$mine = Join-Path $env:LOCALAPPDATA 'omawin\tools\glazewm'
$dir = $null
if (Test-Path (Join-Path $machine 'glazewm.exe')) {
    $sig = Get-AuthenticodeSignature (Join-Path $machine 'glazewm.exe') -ErrorAction SilentlyContinue
    if ($sig -and $sig.SignerCertificate -and $sig.SignerCertificate.Subject -like 'CN=omawin code signing*') { $dir = $machine }
} elseif (Test-Path (Join-Path $mine 'glazewm.exe')) { $dir = $mine }
if (-not $dir) { return }
$file = Join-Path $dir 'omawin-features.txt'
if (-not (Test-Path $file)) { return }
Get-Content $file | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') }
