# install.ps1's one admin prompt: what the install needs admin for is worked out first, asked for
# once, and done in the background by install/admin-batch.ps1 while the other steps carry on.
# Dot-sourced by install.ps1 (after lib.ps1 and software.ps1's functions). Runs under Windows
# PowerShell 5.1 too.
#
# Declined, or no admin password at hand: the install goes on as before it had this, each whole-PC
# package and fork asking on its own as it comes.

$AdminBatch = $null

# What needs admin: the whole-PC packages not installed yet ($WholePC in packages.ps1, among
# $Ids), and the forks unless they're in and current.
function Get-AdminPlan([string[]]$Ids, [switch]$NoPackages) {
    $packages = @()
    if (-not $NoPackages) {
        $installed = Get-WingetInstalled
        $packages = @($Ids | Where-Object { $WholePC -contains $_ -and -not (Test-PackageInstalled $_ $installed) } | Select-Object -Unique)
        # winget's export leaves out a package whose installed version its source no longer has
        # (Zebar, pinned to the version the fork is built on). On a PC omawin has been on, each
        # one it missed is asked about alone, so a reinstall doesn't ask for admin for nothing;
        # the software step then counts it as there.
        if ($installed -and (Test-Path "$env:LOCALAPPDATA\launcher\migrations.txt")) {
            $packages = @($packages | Where-Object {
                    winget list --id $_ --exact --accept-source-agreements *> $null
                    if ($LASTEXITCODE -eq 0) { $installed[$_] = $true; $false } else { $true }
                })
        }
    }
    $forks = @()
    foreach ($fork in 'glazewm-fork', 'zebar-fork') {
        $answer = try { & "$Repo\install\$fork.ps1" -Check } catch { "skip  $_" }
        if ($answer -eq 'needed') { $forks += $fork } else { Write-Log "    $($fork): $answer" }
    }
    [pscustomobject]@{ Packages = $packages; Forks = $forks }
}

# Shows what it's for, asks once (Windows' prompt), and starts the batch. $false if declined.
function Start-AdminBatch($Plan) {
    $what = @()
    if ($Plan.Packages.Count) {
        $names = @($Plan.Packages | ForEach-Object { Get-PackageName $_ })
        $what += "$($Plan.Packages.Count) program$(if ($Plan.Packages.Count -ne 1) { 's' }) for the whole PC|$(($names | Select-Object -First 5) -join ', ')$(if ($names.Count -gt 5) { ' ...' })"
    }
    if ($Plan.Forks.Count) { $what += "omawin's $((@($Plan.Forks | ForEach-Object { if ($_ -eq 'glazewm-fork') { 'GlazeWM' } else { 'Zebar' } })) -join ' and ')|signed on this PC" }
    Show-AdminRequest $what

    $status = Join-Path $env:TEMP "omawin-admin-$Stamp.txt"
    Set-Content $status '' -NoNewline
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$Repo\install\admin-batch.ps1`"", '-Status', "`"$status`"")
    if ($Plan.Packages.Count) { $arguments += '-Packages', ($Plan.Packages -join ',') }
    if ($Plan.Forks.Count) { $arguments += '-Forks' }
    $started = Get-Date
    try {
        $p = Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -PassThru -ArgumentList $arguments
    } catch {
        Close-AdminRequest $false
        Write-Note "no admin: each whole-PC program and fork asks on its own as it installs ($($_.Exception.Message))"
        return $false
    }
    Close-AdminRequest $true
    Write-Log "    admin step started (pid $($p.Id)): $($arguments -join ' ')"
    $script:AdminBatch = @{ P = $p; Status = $status; Pos = 0; Items = @{}; Plan = $Plan; Started = $started; Waited = ((Get-Date) - $started).TotalSeconds }
    $true
}

# Reads what the batch reported since last time.
function Update-AdminStatus {
    $b = $script:AdminBatch
    if (-not $b) { return }
    try {
        $fs = New-Object IO.FileStream($b.Status, 'Open', 'Read', 'ReadWrite')
        try {
            if ($fs.Length -gt $b.Pos) {
                $null = $fs.Seek($b.Pos, 'Begin')
                $buffer = New-Object byte[] ($fs.Length - $b.Pos)
                $n = $fs.Read($buffer, 0, $buffer.Length)
                $text = [Text.Encoding]::UTF8.GetString($buffer, 0, $n)
                # Only whole lines; a half-written one waits for the next read.
                $end = $text.LastIndexOf("`n")
                if ($end -ge 0) {
                    $b.Pos += [Text.Encoding]::UTF8.GetByteCount($text.Substring(0, $end + 1))
                    foreach ($line in $text.Substring(0, $end) -split "`r?`n") {
                        $f = $line -split "`t", 3
                        if ($f.Count -lt 2) { continue }
                        $b.Items[$f[0]] = @{ State = $f[1]; Text = $(if ($f.Count -gt 2) { $f[2] } else { '' }) }
                        if ($f[0] -eq 'end') { $b.EndedAt = Get-Date }
                        Write-Log "    admin: $line"
                    }
                }
            }
        } finally { $fs.Dispose() }
    } catch { }
}

# Where $Item is: run, done, fail or skip; 'wait' before the batch reaches it; 'fail' if the
# batch ended without a word about it.
function Get-AdminState([string]$Item) {
    $b = $script:AdminBatch
    if (-not $b) { return 'none' }
    $i = $b.Items[$Item]
    if ($i -and $i.State -ne 'run') { return $i.State }
    if ($b.P.HasExited) { Update-AdminStatus; $i = $b.Items[$Item]; if ($i -and $i.State -ne 'run') { return $i.State }; return 'fail' }
    if ($i) { 'run' } else { 'wait' }
}
function Get-AdminText([string]$Item) { $i = $script:AdminBatch.Items[$Item]; if ($i) { $i.Text } else { '' } }

# Waits until each of $Items is finished, running $Tick between looks.
function Wait-AdminItems([string[]]$Items, [scriptblock]$Tick) {
    if (-not $script:AdminBatch) { return }
    while ($true) {
        Update-AdminStatus
        if ($Tick) { & $Tick }
        if (-not @($Items | Where-Object { (Get-AdminState $_) -in 'wait', 'run' })) { return }
        Start-Sleep -Milliseconds 200
    }
}

# Once nothing more is coming from it: the admin step's line in the list gets its time.
function Complete-AdminBatch {
    $b = $script:AdminBatch
    if (-not $b -or $b.Completed) { return }
    Wait-AdminItems @('end')
    $b.Completed = $true
    $s = Get-InstallStep 'admin'
    $ended = if ($b.EndedAt) { $b.EndedAt } else { Get-Date }
    $s.Seconds = [Math]::Max(0, ($ended - $b.Started).TotalSeconds - $b.Waited)
    $s.State = if ((Get-AdminState 'end') -eq 'done') { 'done' } else { 'warned' }
    $s.Note = $null
    Write-Log ("[admin] {0} in {1}" -f $s.State, (Format-Duration $s.Seconds))
    Sync-BoardSteps
}
