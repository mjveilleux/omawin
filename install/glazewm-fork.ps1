<#
.SYNOPSIS
    Our GlazeWM fork in place of the official glazewm.exe, so the desktop is up the moment you sign in.

.DESCRIPTION
    The official GlazeWM aborts if it starts before Explorer's taskbar exists (its tray icon), so at
    sign-in it has to wait for Explorer (~3 s after sign-in, plus a margin). Our fork
    (github.com/mjveilleux/glazewm, branch omawin) adds the tray icon once the taskbar is up
    instead, and can start straight away. Its watcher and CLI also know this account's ports
    (bin/ports.ps1).

    GlazeWM asks Windows for uiAccess (to manage windows of programs running as admin), which
    Windows grants only to programs signed by a certificate the PC trusts. So the admin part:
      1. downloads the fork's build of the installed GlazeWM version (its release's SHA256SUMS
         checked), and stops if there isn't one;
      2. makes a code-signing certificate for this PC, once: in the PC's own store, its private
         key usable by admins only and not exportable, so nothing running in an account can sign
         with it;
      3. trusts it on this PC, signs the download with it, and puts the files in place in Program
         Files, keeping each official one as <name>.official.exe;
      4. stops trusting the certificates older versions made in each account's own store.
    It does it all itself, so it can run as another account (a standard account's admin prompt
    takes an admin's password): install.ps1's one admin step runs it (install/admin.ps1).
    Anything failing leaves the official GlazeWM, which works the same, only starting a few
    seconds later at sign-in. Safe to run again.

        .\glazewm-fork.ps1              the fork in (asks for admin), GlazeWM restarted on it
        .\glazewm-fork.ps1 -Restore     the official one back (asks for admin)
        .\glazewm-fork.ps1 -Check       whether the admin part has anything to do ("needed" or why not)
        .\glazewm-fork.ps1 -Machine     the admin part, run elevated; -Out <file> gets its last line
        .\glazewm-fork.ps1 -After       this account's part once the admin part is done: the winget
                                        pin; -Restart restarts GlazeWM on the new file

    Runs under Windows PowerShell 5.1 too.
#>
param([switch]$Restore, [switch]$Check, [switch]$Machine, [switch]$RestoreMachine, [switch]$After, [switch]$Restart, [string]$Out)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor 3072
$ProgressPreference = 'SilentlyContinue'

$dir = Join-Path $env:ProgramFiles 'glzr.io\GlazeWM'
$exe = Join-Path $dir 'glazewm.exe'
$subject = "CN=omawin code signing ($env:COMPUTERNAME)"
# What's installed, PC-wide, so every account and check sees the same.
$state = Join-Path $env:ProgramData 'omawin\glazewm-fork.txt'
# What the fork's release holds, laid out as in Program Files.
$Files = 'glazewm.exe', 'glazewm-watcher.exe', 'cli\glazewm.exe'
$elevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

# Puts $New at $Target even while another Windows account runs it: a running program can't be
# overwritten, but it can be renamed, so the old one is set aside (and deleted once nothing uses it).
function Set-InUseFile([string]$New, [string]$Target) {
    Get-ChildItem (Split-Path $Target) -Filter ((Split-Path $Target -Leaf) + '.old-*') -ErrorAction SilentlyContinue |
        ForEach-Object { Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue }
    if (Test-Path $Target) {
        try { Copy-Item $New $Target -Force; return } catch { }
        Rename-Item $Target ((Split-Path $Target -Leaf) + '.old-' + (Get-Date -Format yyyyMMddHHmmss))
    }
    Copy-Item $New $Target -Force
}
# The installed GlazeWM's version: from Windows' list of programs, else the official watcher's file.
function Get-Base {
    $entry = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue |
        Where-Object { $_.DisplayName -eq 'GlazeWM' } | Select-Object -First 1
    if ($entry.DisplayVersion) { return ($entry.DisplayVersion -replace '^(\d+\.\d+\.\d+).*', '$1') }
    (Get-Item (Join-Path $dir 'glazewm-watcher.exe')).VersionInfo.ProductVersion -replace '^(\d+\.\d+\.\d+).*', '$1'
}
function Get-Release([string]$Base) {
    # (Assigned first: Windows PowerShell passes the whole list down a pipe as one object.)
    $releases = Invoke-RestMethod 'https://api.github.com/repos/mjveilleux/glazewm/releases' -UseBasicParsing
    $releases | Where-Object { $_.tag_name -like "v$Base-omawin.*" } | Sort-Object { [int]($_.tag_name -replace '.*omawin\.', '') } | Select-Object -Last 1
}
# This PC's certificate (the machine store's public half is readable from any account).
function Get-MachineCert {
    Get-ChildItem Cert:\LocalMachine\My -ErrorAction SilentlyContinue |
        Where-Object { $_.Subject -eq $subject -and $_.NotAfter -gt (Get-Date).AddDays(30) } | Select-Object -First 1
}
# Whether $Tag is in, signed by this PC's certificate.
function Test-Current([string]$Tag) {
    if (-not (Test-Path $state) -or (Get-Content $state -Raw).Trim() -ne $Tag) { return $false }
    $cert = Get-MachineCert
    $sig = Get-AuthenticodeSignature $exe
    [bool]($cert -and $sig.Status -eq 'Valid' -and $sig.SignerCertificate.Thumbprint -eq $cert.Thumbprint)
}
function Get-MyGlazeWM { Get-Process glazewm -ErrorAction SilentlyContinue | Where-Object SessionId -eq (Get-Process -Id $PID).SessionId }
function Stop-GlazeWM {
    # This account's only: another account's GlazeWM is its own business.
    if (-not (Get-MyGlazeWM)) { return }
    try { & "$dir\cli\glazewm.exe" command wm-exit 2>$null | Out-Null } catch { }
    for ($i = 0; $i -lt 50 -and (Get-MyGlazeWM); $i++) { Start-Sleep -Milliseconds 100 }
    Get-MyGlazeWM | Stop-Process -Force
}
function Start-GlazeWM {
    # omawin.exe waits for Explorer and starts GlazeWM once; on a brand-new PC the desktop step
    # runs before the helpers step has built it, so start glazewm.exe itself then.
    $omawin = "$HOME\.local\bin\omawin.exe"
    if (Test-Path $omawin) { Start-Process $omawin -ArgumentList 'start', 'glazewm' -WindowStyle Hidden }
    elseif (Test-Path $exe) { Start-Process $exe }
}
# The admin part, elevated, with its last line back through a file (its window is hidden).
function Invoke-Elevated([string]$Switch) {
    $result = Join-Path $env:TEMP "glazewm-fork-$PID.txt"
    $p = Start-Process powershell.exe -Verb RunAs -Wait -PassThru -WindowStyle Hidden -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", $Switch, '-Out', "`"$result`""
    $line = if (Test-Path $result) { (Get-Content $result -Raw).Trim() }
    Remove-Item $result -ErrorAction SilentlyContinue
    if ($p.ExitCode -ne 0) { throw "the admin part failed$(if ($line) { ": $line" } else { " ($($p.ExitCode))" })" }
    $line
}
function Say([string]$Line) { if ($Out) { Set-Content $Out $Line -Encoding utf8 }; $Line }

# --- the admin part ------------------------------------------------------------------------------
if ($Machine) {
    if (-not $elevated) { throw '-Machine runs elevated (install/admin.ps1, or this script without it)' }
    try {
        if (-not (Test-Path $exe)) { Say 'skip  GlazeWM is not installed'; exit 0 }
        $base = Get-Base
        $release = Get-Release $base
        if (-not $release) { Say "ok  official GlazeWM $base (no fork build for it yet)"; exit 0 }
        if (Test-Current $release.tag_name) { Say "ok  GlazeWM fork $($release.tag_name)"; exit 0 }

        $zipAsset = $release.assets | Where-Object name -like 'glazewm-*-windows-x64.zip' | Select-Object -First 1
        $sumsAsset = $release.assets | Where-Object name -eq 'SHA256SUMS-windows-x64.txt' | Select-Object -First 1
        $work = Join-Path $env:TEMP "glazewm-fork-$(Get-Date -Format yyyyMMddHHmmss)"
        New-Item -ItemType Directory -Force $work | Out-Null
        try {
            Invoke-WebRequest $zipAsset.browser_download_url -OutFile "$work\fork.zip" -UseBasicParsing
            Invoke-WebRequest $sumsAsset.browser_download_url -OutFile "$work\sums.txt" -UseBasicParsing
            $want = ((Get-Content "$work\sums.txt" | Where-Object { $_ -match [regex]::Escape($zipAsset.name) } | Select-Object -First 1) -split '\s+')[0]
            if (-not $want -or (Get-FileHash "$work\fork.zip" -Algorithm SHA256).Hash -ne $want.ToUpper()) { throw "$($zipAsset.name) doesn't match its published SHA-256" }
            Expand-Archive "$work\fork.zip" "$work\x" -Force
            $download = Join-Path "$work\x" 'glazewm.exe'

            $cert = Get-MachineCert
            if (-not $cert) {
                $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject -CertStoreLocation Cert:\LocalMachine\My `
                    -KeyExportPolicy NonExportable -KeyUsage DigitalSignature -NotAfter (Get-Date).AddYears(10)
            }
            foreach ($store in 'Root', 'TrustedPublisher') {
                $s = New-Object Security.Cryptography.X509Certificates.X509Store($store, 'LocalMachine')
                $s.Open('ReadWrite')
                if (-not ($s.Certificates | Where-Object Thumbprint -eq $cert.Thumbprint)) { $s.Add([Security.Cryptography.X509Certificates.X509Certificate2]::new($cert.RawData)) }
                $s.Close()
            }
            $null = Set-AuthenticodeSignature -FilePath $download -Certificate $cert -HashAlgorithm SHA256
            $sig = Get-AuthenticodeSignature $download
            if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Thumbprint -ne $cert.Thumbprint) { throw "signing failed ($($sig.Status))" }

            # The window manager, its watcher and its CLI; each official one kept for -Restore.
            foreach ($name in $Files) {
                $new = Join-Path "$work\x" $name
                if (-not (Test-Path $new)) { continue }
                $target = Join-Path $dir $name
                $keep = $target -replace '\.exe$', '.official.exe'
                if ((Test-Path $target) -and -not (Test-Path $keep)) { Copy-Item $target $keep }
                Set-InUseFile $new $target
            }
            New-Item -ItemType Directory -Force (Split-Path $state) | Out-Null
            Set-Content $state $release.tag_name -NoNewline

            # Certificates older versions made in an account's own store, where anything running
            # in that account could sign with them: no longer trusted.
            foreach ($store in 'Root', 'TrustedPublisher') {
                $s = New-Object Security.Cryptography.X509Certificates.X509Store($store, 'LocalMachine')
                $s.Open('ReadWrite')
                foreach ($old in @($s.Certificates | Where-Object { $_.Subject -like 'CN=omawin code signing*' -and $_.Thumbprint -ne $cert.Thumbprint })) { $s.Remove($old) }
                $s.Close()
            }
        } finally { Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue }
        Say "installed  GlazeWM fork $($release.tag_name) (signed on this PC; the official one kept as glazewm.official.exe)"
        exit 0
    } catch { Say "failed  $_"; exit 1 }
}
if ($RestoreMachine) {
    if (-not $elevated) { throw '-RestoreMachine runs elevated' }
    foreach ($name in $Files) {
        $target = Join-Path $dir $name
        $keep = $target -replace '\.exe$', '.official.exe'
        if (Test-Path $keep) { Set-InUseFile $keep $target; Remove-Item $keep -ErrorAction SilentlyContinue }
    }
    Remove-Item $state -ErrorAction SilentlyContinue
    Say 'ok  official GlazeWM back'
    exit 0
}

# --- this account's part -------------------------------------------------------------------------
if ($Check) {
    # Not installed yet: the admin step installs it first (when it's on its list), then this.
    if (-not (Test-Path $exe)) { return 'needed' }
    try { $release = Get-Release (Get-Base) } catch { return "skip  couldn't reach GitHub: $_" }
    if (-not $release) { return 'skip  no fork build for this GlazeWM yet' }
    if (Test-Current $release.tag_name) { return "ok  GlazeWM fork $($release.tag_name)" }
    return 'needed'
}
if ($After) {
    # (Windows PowerShell turns a native program's stderr into an error under 'Stop'.)
    try { winget pin add --id glzr-io.glazewm --exact --accept-source-agreements 2>$null | Out-Null } catch { }
    if ($Restart -and (Get-MyGlazeWM)) { Stop-GlazeWM; Start-GlazeWM }
    return
}
if (-not (Test-Path $exe)) { 'GlazeWM not installed; re-run after installing it'; return }
if ($Restore) {
    Stop-GlazeWM
    try { Invoke-Elevated '-RestoreMachine' } finally { Start-GlazeWM }
    return
}
$line = if ($elevated) { & $PSCommandPath -Machine } else { Invoke-Elevated '-Machine' }
& $PSCommandPath -After -Restart:($line -like 'installed*')
$line
