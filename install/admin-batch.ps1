<#
.SYNOPSIS
    install.ps1's admin step: everything the install needs admin for, after one Windows prompt.

.DESCRIPTION
    Started elevated, hidden, by install/admin.ps1 (Start-AdminBatch); not meant to be run by hand.
    May run as another account than the one installing (a standard account's prompt takes an
    admin's password), so it only does what's the same for every account:

      1. the whole-PC winget packages it's given (-Packages), --scope machine, three at once
         (Windows runs one MSI at a time; one that finds another busy gets one more go, alone)
      2. omawin's GlazeWM and Zebar (-Forks): install/glazewm-fork.ps1 and zebar-fork.ps1, -Machine

    It takes nothing from the account that started it but these arguments: package ids (checked
    against the shape of one) and a file to report to. Each line there is
    "<item>`t<state>`t<text>": a package id, "packages", "glazewm-fork", "zebar-fork" or "end";
    run, done, fail or skip. install/admin.ps1 reads it as it grows.

    Runs under Windows PowerShell 5.1.
#>
param([Parameter(Mandatory)][string]$Status, [string]$Packages, [switch]$Forks)
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
            # installs them instead, each with its own prompt.
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
                        $running[$id] = @{ P = $p; Out = $p.StandardOutput.ReadToEndAsync(); Err = $p.StandardError.ReadToEndAsync() }
                        Say $id 'run'
                    }
                    foreach ($id in @($running.Keys)) {
                        $r = $running[$id]
                        if (-not $r.P.HasExited) { continue }
                        $r.P.WaitForExit()
                        $running.Remove($id)
                        $code = $r.P.ExitCode
                        # 0: installed; 0x8A15002B / 0x8A150061: already there.
                        if ($code -in 0, -1978335189, -1978335135) { Say $id 'done' }
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

    # --- 2. omawin's GlazeWM and Zebar ---------------------------------------------------------
    if ($Forks) {
        foreach ($fork in 'glazewm-fork', 'zebar-fork') {
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
