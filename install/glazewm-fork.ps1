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

    Opt-in (roadmap 2.9, "manage admin windows too"): by default omawin runs the tools bundle's
    per-user GlazeWM, with no admin at all; its one loss is windows of programs running as admin,
    which it can't tile or move. `omawin glazewm admin` runs this, which says what it will change
    and asks once. On a PC with no GlazeWM in Program Files it puts the fork there on its own
    (the newest fork release; nothing official to keep), and -Restore takes those files away
    again, so the per-user one takes over. -Check only ever answers "needed" once the fork is in
    (its state file), so neither the install's admin prompt nor a migration offers it unasked,
    and an update never asks: it says so instead (install/admin.ps1).

        .\glazewm-fork.ps1              the fork in (asks for admin), GlazeWM restarted on it
        .\glazewm-fork.ps1 -Restore     the official one back, or the per-user one (asks for admin)
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
# Says the fork went in on its own, with no official GlazeWM under it: -Restore removes its files.
$alone = Join-Path $dir '.omawin-only'
# And beside them, the list of what the fork can do (emit, ...), gone with the fork.
$FeaturesFile = 'omawin-features.txt'
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
# The newest fork build of GlazeWM $Base; with $Base '*', the newest of any (a PC with no
# GlazeWM in Program Files: the fork goes in on its own).
function Get-Release([string]$Base) {
    # (Assigned first: Windows PowerShell passes the whole list down a pipe as one object.)
    $releases = Invoke-RestMethod 'https://api.github.com/repos/mjveilleux/glazewm/releases' -UseBasicParsing
    $releases | Where-Object { $_.tag_name -like "v$Base-omawin.*" -and $_.tag_name -match '^v(\d+\.\d+\.\d+)-omawin\.(\d+)$' } |
        Sort-Object { $null = $_.tag_name -match '^v(\d+\.\d+\.\d+)-omawin\.(\d+)$'; [version]$Matches[1] }, { $null = $_.tag_name -match 'omawin\.(\d+)$'; [int]$Matches[1] } |
        Select-Object -Last 1
}
# This PC's certificate (the machine store's public half is readable from any account).
function Get-MachineCert {
    Get-ChildItem Cert:\LocalMachine\My -ErrorAction SilentlyContinue |
        Where-Object { $_.Subject -eq $subject -and $_.NotAfter -gt (Get-Date).AddDays(30) } | Select-Object -First 1
}
# Certificates older versions made in each account's own store, where anything running in that
# account could sign with them, still trusted by the PC: every omawin one but $Keep's.
function Get-OldTrust([string]$Keep) {
    foreach ($store in 'Root', 'TrustedPublisher') {
        Get-ChildItem "Cert:\LocalMachine\$store" -ErrorAction SilentlyContinue |
            Where-Object { $_.Subject -like 'CN=omawin code signing*' -and $_.Thumbprint -ne $Keep } |
            ForEach-Object { [pscustomobject]@{ Store = $store; Cert = $_ } }
    }
}
function Remove-OldTrust([string]$Keep) {
    foreach ($store in 'Root', 'TrustedPublisher') {
        $s = New-Object Security.Cryptography.X509Certificates.X509Store($store, 'LocalMachine')
        $s.Open('ReadWrite')
        foreach ($old in @($s.Certificates | Where-Object { $_.Subject -like 'CN=omawin code signing*' -and $_.Thumbprint -ne $Keep })) { $s.Remove($old) }
        $s.Close()
    }
}
# A folder of its own under Program Files, which only admins can write, for the admin part's
# download: nothing running in an account can swap a file in it between the hash check, the
# signing and the copy (the account's own TEMP is that account's to write).
function New-Staging {
    $root = Join-Path $env:ProgramFiles 'glzr.io\.omawin-staging'
    New-Item -ItemType Directory -Force $root | Out-Null
    $work = Join-Path $root ([Guid]::NewGuid().ToString('n'))
    New-Item -ItemType Directory $work | Out-Null
    $work
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
# GlazeWM's config again (themes/render.ps1), for the GlazeWM now in place; a failure is only said.
function Update-GlazeConfig {
    try { & (Join-Path (Split-Path $PSScriptRoot) 'themes\render.ps1') | Out-Null }
    catch { Write-Warning "GlazeWM's config wasn't written again: $_ (omawin restart glazewm)" }
}
function Say([string]$Line) { if ($Out) { Set-Content $Out $Line -Encoding utf8 }; $Line }

# --- the admin part ------------------------------------------------------------------------------
if ($Machine) {
    if (-not $elevated) { throw '-Machine runs elevated (install/admin.ps1, or this script without it)' }
    $failure = $null
    try {
        # First, whatever happens next: no certificate an account could sign with stays trusted.
        $machineCert = Get-MachineCert
        Remove-OldTrust $(if ($machineCert) { $machineCert.Thumbprint } else { '-' })
        # No GlazeWM in Program Files (the per-user one runs): the newest fork, on its own.
        $fresh = -not (Test-Path $exe)
        $base = if ($fresh) { '*' } else { Get-Base }
        $release = Get-Release $base
        if (-not $release) { Say $(if ($fresh) { 'skip  no fork release found' } else { "ok  official GlazeWM $base (no fork build for it yet)" }); exit 0 }
        if (Test-Current $release.tag_name) { Say "ok  GlazeWM fork $($release.tag_name)"; exit 0 }

        $zipAsset = $release.assets | Where-Object name -like 'glazewm-*-windows-x64.zip' | Select-Object -First 1
        $sumsAsset = $release.assets | Where-Object name -eq 'SHA256SUMS-windows-x64.txt' | Select-Object -First 1
        $work = New-Staging
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
            if ($fresh) { New-Item -ItemType Directory -Force (Join-Path $dir 'cli') | Out-Null; Set-Content $alone 'the fork, on its own (install/glazewm-fork.ps1)' }
            foreach ($name in $Files) {
                $new = Join-Path "$work\x" $name
                if (-not (Test-Path $new)) { continue }
                $target = Join-Path $dir $name
                $keep = $target -replace '\.exe$', '.official.exe'
                if ((Test-Path $target) -and -not (Test-Path $keep)) { Copy-Item $target $keep }
                Set-InUseFile $new $target
            }
            # What this build can do beyond the official one (bin/glazewm-features.ps1 reads it
            # beside glazewm.exe): data, not signed. A release from before it has none, so an old
            # copy goes.
            $features = Join-Path "$work\x" $FeaturesFile
            if (Test-Path $features) { Copy-Item $features (Join-Path $dir $FeaturesFile) -Force }
            else { Remove-Item (Join-Path $dir $FeaturesFile) -ErrorAction SilentlyContinue }
            New-Item -ItemType Directory -Force (Split-Path $state) | Out-Null
            Set-Content $state $release.tag_name -NoNewline
            Remove-OldTrust $cert.Thumbprint
        } finally { Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue }
        Say "installed  GlazeWM fork $($release.tag_name) (signed on this PC; the official one kept as glazewm.official.exe)"
        exit 0
    } catch { $failure = "$_"; Say "failed  $_"; exit 1 }
    finally {
        # A glazewm.exe still signed by a certificate taken away above (the new one didn't go in:
        # GitHub down, no build for this version) won't start: Windows refuses a uiAccess program
        # whose signature doesn't check out. The official one goes back instead.
        $sig = if (Test-Path $exe) { Get-AuthenticodeSignature $exe }
        if ($sig -and $sig.SignerCertificate -and $sig.SignerCertificate.Subject -like 'CN=omawin code signing*' -and $sig.Status -ne 'Valid') {
            foreach ($name in $Files) {
                $target = Join-Path $dir $name
                $keep = $target -replace '\.exe$', '.official.exe'
                if (Test-Path $keep) { Set-InUseFile $keep $target; Remove-Item $keep -ErrorAction SilentlyContinue }
            }
            Remove-Item (Join-Path $dir $FeaturesFile) -ErrorAction SilentlyContinue
            Remove-Item $state -ErrorAction SilentlyContinue
            # Say writes -Out's one line: a failure above stays a failure, not a skip.
            if ($failure) { Say "failed  $failure (the official GlazeWM is back)" }
            else { Say "skip  official GlazeWM back: the fork in place was signed by a certificate no longer trusted" }
        }
    }
}
if ($RestoreMachine) {
    if (-not $elevated) { throw '-RestoreMachine runs elevated' }
    if (Test-Path $alone) {
        # Put in on its own: there's no official one to bring back, so omawin's files go, and the
        # per-user GlazeWM takes over at its next start. (A running file is renamed aside and
        # goes with the next restore or reinstall.)
        foreach ($name in $Files) {
            $target = Join-Path $dir $name
            if (Test-Path $target) { try { Remove-Item $target -Force } catch { Rename-Item $target ((Split-Path $target -Leaf) + '.old-' + (Get-Date -Format yyyyMMddHHmmss)) } }
        }
        Remove-Item $alone -Force
    }
    foreach ($name in $Files) {
        $target = Join-Path $dir $name
        $keep = $target -replace '\.exe$', '.official.exe'
        if (Test-Path $keep) { Set-InUseFile $keep $target; Remove-Item $keep -ErrorAction SilentlyContinue }
    }
    Remove-Item (Join-Path $dir $FeaturesFile) -ErrorAction SilentlyContinue
    Remove-Item $state -ErrorAction SilentlyContinue
    # The official GlazeWM needs none of omawin's certificates: none stays trusted, and this PC's
    # own goes too.
    Remove-OldTrust '-'
    Get-ChildItem Cert:\LocalMachine\My -ErrorAction SilentlyContinue | Where-Object Subject -eq $subject | Remove-Item -ErrorAction SilentlyContinue
    Say 'ok  official GlazeWM back'
    exit 0
}

# --- this account's part -------------------------------------------------------------------------
if ($Check) {
    # Opt-in: until the fork is in (its state file), nothing offers it unasked.
    if (-not (Test-Path $state)) { return 'skip  not chosen: the per-user GlazeWM runs (omawin glazewm admin: admin windows too, one prompt)' }
    if (-not (Test-Path $exe)) { return 'needed' }
    # An old per-account certificate still trusted: the admin part takes that away, whatever else.
    $machineCert = Get-MachineCert
    if (Get-OldTrust $(if ($machineCert) { $machineCert.Thumbprint } else { '-' })) { return 'needed' }
    try { $release = Get-Release (Get-Base) } catch { return "skip  couldn't reach GitHub: $_" }
    if (-not $release) { return 'skip  no fork build for this GlazeWM yet' }
    if (Test-Current $release.tag_name) { return "ok  GlazeWM fork $($release.tag_name)" }
    return 'needed'
}
if ($After) {
    # (Windows PowerShell turns a native program's stderr into an error under 'Stop'.)
    try { winget pin add --id glzr-io.glazewm --exact --accept-source-agreements 2>$null | Out-Null } catch { }
    # GlazeWM's config is written for what the GlazeWM in place can do (the menu keys' emit).
    Update-GlazeConfig
    if ($Restart -and (Get-MyGlazeWM)) { Stop-GlazeWM; Start-GlazeWM }
    return
}
if ($Restore) {
    if (-not (Test-Path $exe)) { 'ok  no GlazeWM in Program Files: the per-user one runs'; return }
    Stop-GlazeWM
    # Written again for the official GlazeWM, which refuses a config with emit in it.
    try { Invoke-Elevated '-RestoreMachine' } finally { Update-GlazeConfig; Start-GlazeWM }
    return
}
# What the one prompt will change, said before Windows asks (roadmap 2.9).
if (-not $elevated) {
    Write-Host 'Manage admin windows too: one admin prompt, which'
    if (Test-Path $exe) { Write-Host "  - swaps omawin's GlazeWM in for the one in $dir (each official file kept as <name>.official.exe)" }
    else { Write-Host "  - puts omawin's GlazeWM (the fork's newest release, checked against its SHA-256) in $dir" }
    Write-Host "  - signs it with a certificate made for this PC ($subject), trusted on this PC only, its key usable by admins only"
    Write-Host '  - restarts GlazeWM on it: windows of programs running as admin are tiled too'
    Write-Host '  Undo: omawin glazewm admin-off (one prompt).'
}
$line = if ($elevated) { & $PSCommandPath -Machine } else { Invoke-Elevated '-Machine' }
& $PSCommandPath -After -Restart:($line -like 'installed*')
$line
