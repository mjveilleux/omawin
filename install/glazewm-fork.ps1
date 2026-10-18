<#
.SYNOPSIS
    Our GlazeWM fork in place of the official glazewm.exe, so the desktop is up the moment you sign in.

.DESCRIPTION
    The official GlazeWM aborts if it starts before Explorer's taskbar exists (its tray icon), so at
    sign-in it has to wait for Explorer (~3 s after sign-in, plus a margin). Our fork
    (github.com/mjveilleux/glazewm, branch omawin) adds the tray icon once the taskbar is up
    instead, and can start straight away.

    GlazeWM asks Windows for uiAccess (to manage windows of programs running as admin), which
    Windows grants only to programs signed by a certificate the PC trusts. So this:
      1. downloads the fork's glazewm.exe built on the same GlazeWM version as the installed one
         (its release's SHA256SUMS checked), and stops if there isn't one;
      2. makes a code-signing certificate for this PC alone, if there isn't one yet: its private
         key stays in your Windows account and can't be exported, and it can only sign code;
      3. trusts it on this PC (admin), signs the download with it, and puts it in place of
         glazewm.exe in Program Files (admin), keeping the official one as glazewm.official.exe.
    The watcher, the CLI and Zebar stay the official ones. GlazeWM is pinned in winget, so an
    upgrade doesn't swap in a version the fork isn't built on. Anything failing leaves the
    official GlazeWM, which works the same, only starting a few seconds later at sign-in.
    Safe to run again; -Restore puts the official glazewm.exe back.

        .\glazewm-fork.ps1            (asks for admin once)
        .\glazewm-fork.ps1 -Restore
#>
param([switch]$Restore, [string]$Signed, [string]$Cert)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor 3072

$dir = Join-Path $env:ProgramFiles 'glzr.io\GlazeWM'
$exe = Join-Path $dir 'glazewm.exe'
$official = Join-Path $dir 'glazewm.official.exe'
$subject = "CN=omawin code signing ($env:COMPUTERNAME)"
$elevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
# What the fork's release holds, laid out as in Program Files.
$Files = 'glazewm.exe', 'glazewm-watcher.exe', 'cli\glazewm.exe'

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

# --- the admin part: trust the certificate, put the file in place ---------------------------------
# Run elevated by the part below, with the signed download; GlazeWM is already stopped.
if ($Signed) {
    # The certificate that signed it, as a file from the part below: this part may run as
    # another account (a standard account's admin prompt takes an admin's password), whose own
    # certificate store doesn't have it.
    $cert = if ($Cert) { New-Object Security.Cryptography.X509Certificates.X509Certificate2 (, [IO.File]::ReadAllBytes($Cert)) }
            else { Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $subject } | Select-Object -First 1 }
    foreach ($store in 'Root', 'TrustedPublisher') {
        if (-not (Get-ChildItem "Cert:\LocalMachine\$store" | Where-Object Thumbprint -eq $cert.Thumbprint)) {
            $s = New-Object Security.Cryptography.X509Certificates.X509Store($store, 'LocalMachine')
            $s.Open('ReadWrite'); $s.Add([Security.Cryptography.X509Certificates.X509Certificate2]::new($cert.RawData)); $s.Close()
        }
    }
    $from = Split-Path $Signed
    if ((Get-AuthenticodeSignature $Signed).Status -ne 'Valid') { throw 'the signed glazewm.exe does not verify' }
    # The window manager, its watcher and its CLI: all three know the per-account port
    # (bin/ports.ps1). Each official one is kept as <name>.official.exe for -Restore.
    foreach ($name in $Files) {
        $new = Join-Path $from $name
        if (-not (Test-Path $new)) { continue }
        $target = Join-Path $dir $name
        $keep = $target -replace '\.exe$', '.official.exe'
        if ((Test-Path $target) -and -not (Test-Path $keep)) { Copy-Item $target $keep }
        Set-InUseFile $new $target
    }
    exit 0
}
if ($Restore -and $elevated) {
    foreach ($name in $Files) {
        $target = Join-Path $dir $name
        $keep = $target -replace '\.exe$', '.official.exe'
        if (Test-Path $keep) { Set-InUseFile $keep $target; Remove-Item $keep -ErrorAction SilentlyContinue }
    }
    exit 0
}

# --- your part ---------------------------------------------------------------------------------------
function Get-MyGlazeWM { Get-Process glazewm -ErrorAction SilentlyContinue | Where-Object SessionId -eq (Get-Process -Id $PID).SessionId }
function Stop-GlazeWM {
    # This account's only: another account's GlazeWM is its own business.
    if (-not (Get-MyGlazeWM)) { return }
    try { & "$dir\cli\glazewm.exe" command wm-exit 2>$null | Out-Null } catch { }
    for ($i = 0; $i -lt 50 -and (Get-MyGlazeWM); $i++) { Start-Sleep -Milliseconds 100 }
    Get-Process glazewm -ErrorAction SilentlyContinue | Where-Object SessionId -eq (Get-Process -Id $PID).SessionId | Stop-Process -Force
}
function Start-GlazeWM {
    # omawin.exe waits for Explorer and starts GlazeWM once; on a brand-new PC the desktop step
    # runs before the helpers step has built it, so start glazewm.exe itself then.
    $omawin = "$HOME\.local\bin\omawin.exe"
    if (Test-Path $omawin) { Start-Process $omawin -ArgumentList 'start', 'glazewm' -WindowStyle Hidden }
    elseif (Test-Path $exe) { Start-Process $exe }
}
function Invoke-Elevated([string[]]$Arguments) {
    $shell = if (Get-Command pwsh -ErrorAction SilentlyContinue) { 'pwsh' } else { 'powershell' }
    $p = Start-Process $shell -Verb RunAs -Wait -PassThru -WindowStyle Hidden -ArgumentList (@('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"") + $Arguments)
    if ($p.ExitCode -ne 0) { throw "the admin part failed ($($p.ExitCode))" }
}

if (-not (Test-Path $exe)) { 'GlazeWM not installed; re-run after installing it'; return }

if ($Restore) {
    Stop-GlazeWM; Invoke-Elevated '-Restore'; Start-GlazeWM
    'ok  official GlazeWM back'
    return
}

# The GlazeWM version installed: from the official files (the watcher), since glazewm.exe may be ours.
$base = (Get-Item (Join-Path $dir 'glazewm-watcher.exe')).VersionInfo.ProductVersion -replace '^(\d+\.\d+\.\d+).*', '$1'
$current = Get-AuthenticodeSignature $exe
$ours = $current.Status -eq 'Valid' -and $current.SignerCertificate.Subject -eq $subject

$releases = Invoke-RestMethod 'https://api.github.com/repos/mjveilleux/glazewm/releases' -UseBasicParsing
$release = $releases | Where-Object { $_.tag_name -like "v$base-omawin.*" } | Sort-Object { [int]($_.tag_name -replace '.*omawin\.', '') } | Select-Object -Last 1
if (-not $release) { "ok  official GlazeWM $base (no fork build for $base yet)"; return }
$state = Join-Path $env:LOCALAPPDATA 'launcher\glazewm-fork.txt'
if ($ours -and (Test-Path $state) -and (Get-Content $state -Raw).Trim() -eq $release.tag_name) { "ok  GlazeWM fork $($release.tag_name)"; return }

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

    # This PC's own code-signing certificate (private key not exportable), made once.
    $cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Where-Object { $_.Subject -eq $subject -and $_.NotAfter -gt (Get-Date).AddDays(30) } | Select-Object -First 1
    if (-not $cert) {
        $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject -CertStoreLocation Cert:\CurrentUser\My `
            -KeyExportPolicy NonExportable -KeyUsage DigitalSignature -NotAfter (Get-Date).AddYears(10)
    }
    $null = Set-AuthenticodeSignature -FilePath $download -Certificate $cert -HashAlgorithm SHA256
    # Signed by us but not trusted yet: that's the admin part's first step.
    if ((Get-AuthenticodeSignature $download).SignerCertificate.Thumbprint -ne $cert.Thumbprint) { throw 'signing failed' }

    Stop-GlazeWM
    # The state file is written as soon as the admin part succeeds, so a failure restarting
    # GlazeWM can't make the next run redo the install.
    try {
        $certFile = Join-Path $work 'omawin-signing.cer'   # the public half only
        [IO.File]::WriteAllBytes($certFile, $cert.RawData)
        Invoke-Elevated @('-Signed', "`"$download`"", '-Cert', "`"$certFile`"")
        New-Item -ItemType Directory -Force (Split-Path $state) | Out-Null
        Set-Content $state $release.tag_name -NoNewline
    }
    finally {
        try { Start-GlazeWM } catch { Write-Warning "couldn't start GlazeWM again: $_" }
    }
    # (Windows PowerShell turns a native program's stderr into an error under 'Stop'.)
    try { winget pin add --id glzr-io.glazewm --exact --accept-source-agreements 2>$null | Out-Null } catch { }
    "ok  GlazeWM fork $($release.tag_name) (signed on this PC; the official one kept as glazewm.official.exe)"
} finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
