<#
.SYNOPSIS
    install.ps1's admin step: everything the install needs admin for, after one Windows prompt.

.DESCRIPTION
    Started elevated, hidden, by install/admin.ps1 (Start-AdminBatch); not meant to be run by hand.
    May run as another account than the one installing (a standard account's prompt takes an
    admin's password), so it only does what's the same for every account:

      1. the whole-PC winget packages it's given (-Packages), --scope machine, three at once
         (Windows runs one MSI at a time; one that finds another busy gets one more go, alone)
      2. WSL itself (-Wsl), for the wsl preset: `wsl --install --no-distribution`, which wants a
         restart before it works ("done restart"); the distro is the account's, installed
         without admin (wsl/install.ps1)
      3. omawin's GlazeWM (-Forks): install/glazewm-fork.ps1 -Machine

    It takes nothing from the account that started it but these arguments: package ids (checked
    against the shape of one) and a file to report to. Each line there is
    "<item>`t<state>`t<text>": a package id, "packages", "wsl", "glazewm-fork" or
    "end"; run, done, fail or skip. install/admin.ps1 reads it as it grows.

    Runs under Windows PowerShell 5.1.
#>
param([Parameter(Mandatory)][string]$Status, [string]$Packages, [switch]$Wsl, [switch]$Forks)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function Say([string]$Item, [string]$State, [string]$Text = '') {
    $line = "$Item`t$State`t$($Text -replace '[\r\n\t]+', ' ')"
    # The account reading it may have it open; retry briefly rather than lose a line.
    for ($i = 0; $i -lt 20; $i++) {
        try { [IO.File]::AppendAllText($Status, $line + [Environment]::NewLine); return } catch { Start-Sleep -Milliseconds 50 }
    }
}

try {
    # --- 1. the whole-PC packages ----------------------------------------------------------------
    $ids = @(($Packages -split ',') | ForEach-Object { $_.Trim() } | Where-Object { $_ -match '^[A-Za-z0-9][A-Za-z0-9._+-]*$' })
    if ($ids) {
        $winget = Get-Command winget -ErrorAction SilentlyContinue
        if (-not $winget) {
            # The admin account may never have signed in, so it has no winget: this account
            # installs what it can of them for itself instead (--scope user).
            foreach ($id in $ids) { Say $id 'fail' 'winget is missing for the admin account' }
        } else {
            $queue = New-Object Collections.Generic.Queue[string]
            foreach ($id in $ids) { $queue.Enqueue($id) }
            $retry = @()
            foreach ($width in 3, 1) {
                $running = @{}
                while ($queue.Count -or $running.Count) {
                    while ($queue.Count -and $running.Count -lt $width) {
                        $id = $queue.Dequeue()
                        $psi = New-Object Diagnostics.ProcessStartInfo
                        $psi.FileName = $winget.Source
                        $psi.Arguments = "install --id $id --exact --silent --scope machine --accept-source-agreements --accept-package-agreements --disable-interactivity"
                        $psi.UseShellExecute = $false
                        $psi.CreateNoWindow = $true
                        $psi.RedirectStandardOutput = $true
                        $psi.RedirectStandardError = $true
                        $p = [Diagnostics.Process]::Start($psi)
                        $running[$id] = @{ P = $p; Out = $p.StandardOutput.ReadToEndAsync(); Err = $p.StandardError.ReadToEndAsync(); Start = Get-Date }
                        Say $id 'run'
                    }
                    foreach ($id in @($running.Keys)) {
                        $r = $running[$id]
                        # An installer waiting on something it can't show (this window is hidden):
                        # given up on after 15 minutes, and this account installs it instead.
                        if (-not $r.P.HasExited -and ((Get-Date) - $r.Start).TotalMinutes -gt 15) {
                            try { $r.P.Kill() } catch { }
                            $running.Remove($id)
                            Say $id 'fail' 'gave up after 15 minutes'
                            continue
                        }
                        if (-not $r.P.HasExited) { continue }
                        $r.P.WaitForExit()
                        $running.Remove($id)
                        $code = $r.P.ExitCode
                        # 0: installed; 0x8A15002B / 0x8A150061: already there; 0x8A150109: installed,
                        # Windows wants a restart.
                        if ($code -in 0, -1978335189, -1978335135, -1978334967) { Say $id 'done' }
                        elseif ($width -gt 1) { $retry += $id }
                        else {
                            $tail = (($r.Out.Result + $r.Err.Result) -split "[`r`n]+" | Where-Object { $_.Trim() -and $_ -notmatch ('[{0}-{1}]' -f [char]0x2580, [char]0x259F) } | Select-Object -Last 1)
                            Say $id 'fail' "exit $code$(if ($tail) { ": $($tail.Trim())" })"
                        }
                    }
                    Start-Sleep -Milliseconds 150
                }
                foreach ($id in $retry) { $queue.Enqueue($id) }
                $retry = @()
            }
        }
        Say 'packages' 'done'
    }

    # --- 2. WSL ----------------------------------------------------------------------------------
    if ($Wsl) {
        Say 'wsl' 'run'
        if (Test-Path "$env:ProgramFiles\WSL\wsl.exe") { Say 'wsl' 'done' 'already there' }
        else {
            # Windows' own wsl.exe fetches the WSL package (from GitHub: no Store needed) and turns
            # on the Virtual Machine Platform, which takes effect at the next restart.
            $psi = New-Object Diagnostics.ProcessStartInfo
            $psi.FileName = "$env:SystemRoot\System32\wsl.exe"
            $psi.Arguments = '--install --no-distribution --web-download'
            $psi.UseShellExecute = $false
            $psi.CreateNoWindow = $true
            $psi.RedirectStandardOutput = $true
            $psi.RedirectStandardError = $true
            $p = [Diagnostics.Process]::Start($psi)
            $out = $p.StandardOutput.ReadToEndAsync(); $err = $p.StandardError.ReadToEndAsync()
            $present = { Test-Path "$env:ProgramFiles\WSL\wsl.exe" }
            if (-not $p.WaitForExit(15 * 60 * 1000)) { try { $p.Kill() } catch { }; Say 'wsl' 'fail' 'gave up after 15 minutes' }
            # Done: it's in, or it said so (0; 3010: a restart finishes it, as some Windows
            # versions put the package in only then).
            elseif ((& $present) -or $p.ExitCode -in 0, 3010) {
                $platform = try { (Get-WindowsOptionalFeature -Online -FeatureName VirtualMachinePlatform).State } catch { 'Unknown' }
                Say 'wsl' 'done' $(if ((& $present) -and "$platform" -eq 'Enabled') { '' } else { 'restart' })
            } else {
                # (wsl.exe writes UTF-16: the NULs out.)
                $tail = (($out.Result + $err.Result) -replace "`0", '' -split "[`r`n]+" | Where-Object { $_.Trim() } | Select-Object -Last 1)
                Say 'wsl' 'fail' "exit $($p.ExitCode)$(if ($tail) { ": $($tail.Trim())" })"
            }
        }
    }

    # --- 3. omawin's GlazeWM -----------------------------------------------------------------
    if ($Forks) {
        foreach ($fork in 'glazewm-fork') {
            Say $fork 'run'
            $out = Join-Path $env:TEMP "$fork-$PID.txt"
            try {
                $null = & (Join-Path $PSScriptRoot "$fork.ps1") -Machine -Out $out
                $line = if (Test-Path $out) { (Get-Content $out -Raw).Trim() } else { '' }
                if ($line -like 'failed*') { Say $fork 'fail' ($line -replace '^failed\s+') }
                elseif ($line -like 'skip*') { Say $fork 'skip' ($line -replace '^skip\s+') }
                else { Say $fork 'done' $line }
            } catch { Say $fork 'fail' "$_" }
            finally { Remove-Item $out -ErrorAction SilentlyContinue }
        }
    }
} catch {
    Say 'end' 'fail' "$_"
    exit 1
}
Say 'end' 'done'
exit 0
