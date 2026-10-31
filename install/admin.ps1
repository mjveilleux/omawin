# install.ps1's one admin prompt: what the install needs admin for is worked out first, asked for
# once, and done in the background by install/admin-batch.ps1 while the other steps carry on.
# Dot-sourced by install.ps1 (after lib.ps1 and software.ps1's functions). Runs under Windows
# PowerShell 5.1 too.
#
# It's the only prompt: after it, install.ps1 sets OMAWIN_NO_ADMIN and nothing asks again.
# Declined, or no admin password at hand (the non-admin path): the install goes on without it,
# installing what it can for this account alone (the whole-PC packages with winget --scope user
# where they have such an installer) and leaving out what only admin can do (the fork: the
# per-user or official GlazeWM stays; WSL), each said in the summary.

$AdminBatch = $null

# What needs admin: the whole-PC packages not installed yet ($WholePC in install/packages.ps1, among
# $Ids), the forks unless they're in and current, and WSL itself when the wsl preset is picked
# and the PC hasn't got it.
function Get-AdminPlan([string[]]$Ids, [switch]$NoPackages) {
    $packages = @()
    if (-not $NoPackages) {
        $installed = Get-WingetInstalled
        $packages = @($Ids | Where-Object { $WholePC -contains $_ -and -not (Test-PackageInstalled $_ $installed) } | Select-Object -Unique)
        # winget's export leaves out a package whose installed version its source no longer has
        # (a package pinned to an older version). On a PC omawin has been on, each
        # one it missed is asked about alone, so a reinstall doesn't ask for admin for nothing;
        # the software step then counts it as there.
        if ($installed -and (Test-Path "$env:LOCALAPPDATA\launcher\migrations.txt")) {
            $packages = @($packages | Where-Object {
                    # (Windows PowerShell throws on a native program's stderr under 'Stop'.)
                    & { $ErrorActionPreference = 'Continue'; winget list --id $_ --exact --accept-source-agreements *> $null }
                    if ($LASTEXITCODE -eq 0) { $installed[$_] = $true; $false } else { $true }
                })
        }
    }
    $forks = @()
    foreach ($fork in 'glazewm-fork') {
        # -Check says "needed" only once you chose the whole-PC GlazeWM (omawin glazewm admin) and
        # it has something to do: a new build, an old certificate to take away. An update never
        # asks for admin (roadmap 2.9): it says what's waiting, and you run it when you choose.
        $answer = try { & "$Repo\install\$fork.ps1" -Check } catch { "skip  $_" }
        if ($answer -ne 'needed') { Write-Log "    $($fork): $answer" }
        elseif ($NoPackages) { Write-Warn "omawin's GlazeWM for the whole PC has an update, which takes admin: omawin glazewm admin (one prompt, when you choose)" }
        else { $forks += $fork }
    }
    # An update installs no programs, WSL neither (omawin preset add wsl does, with its own prompt).
    $wsl = (-not $NoPackages) -and (@(Get-ChosenPresets) -contains 'wsl') -and -not (Test-WslInstalled)
    [pscustomobject]@{ Packages = $packages; Forks = $forks; Wsl = $wsl }
}

# Whether WSL went in just now and works only once Windows restarts: waits for the admin step's
# word on it (the wsl preset's installer asks, install/packages.ps1). $false when it wasn't on the list.
function Get-WslRestart {
    if (-not ($script:AdminBatch -and $script:AdminBatch.Plan.Wsl)) { return $false }
    Wait-AdminItems @('wsl')
    (Get-AdminState 'wsl') -eq 'done' -and (Get-AdminText 'wsl') -eq 'restart'
}

# Shows what it's for, asks once (Windows' prompt), and starts the batch. $false if declined.
function Start-AdminBatch($Plan) {
    $what = @()
    if ($Plan.Packages.Count) {
        $names = @($Plan.Packages | ForEach-Object { Get-PackageName $_ })
        $what += "$($Plan.Packages.Count) program$(if ($Plan.Packages.Count -ne 1) { 's' }) for the whole PC|$(($names | Select-Object -First 5) -join ', ')$(if ($names.Count -gt 5) { ' ...' })"
    }
    if ($Plan.Forks.Count) { $what += "omawin's GlazeWM|signed on this PC" }
    if ($Plan.Wsl) { $what += "WSL, for $((Get-LinuxPick).Label)|it's ready after Windows restarts" }
    Show-AdminRequest $what

    $status = Join-Path $env:TEMP "omawin-admin-$Stamp.txt"
    Set-Content $status '' -NoNewline
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$Repo\install\admin-batch.ps1`"", '-Status', "`"$status`"")
    if ($Plan.Packages.Count) { $arguments += '-Packages', ($Plan.Packages -join ',') }
    if ($Plan.Forks.Count) { $arguments += '-Forks' }
    if ($Plan.Wsl) { $arguments += '-Wsl' }
    $started = Get-Date
    try {
        $p = Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -PassThru -ArgumentList $arguments
    } catch {
        Close-AdminRequest $false
        Write-Note "no admin: this account gets what it can install alone, and nothing asks again ($($_.Exception.Message))"
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

# Waits until each of $Items is finished, running $Tick between looks. Not for ever: past 45
# minutes from the prompt (each package has its own 15 in the batch), what's still waiting is
# taken as failed, and this account installs it, or the official fork stays.
function Wait-AdminItems([string[]]$Items, [scriptblock]$Tick) {
    if (-not $script:AdminBatch) { return }
    $deadline = $script:AdminBatch.Started.AddMinutes(45)
    while ((Get-Date) -lt $deadline) {
        Update-AdminStatus
        if ($Tick) { & $Tick }
        if (-not @($Items | Where-Object { (Get-AdminState $_) -in 'wait', 'run' })) { return }
        Start-Sleep -Milliseconds 200
    }
    foreach ($item in $Items) {
        if ((Get-AdminState $item) -in 'wait', 'run') { $script:AdminBatch.Items[$item] = @{ State = 'fail'; Text = 'the admin step took too long' } }
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
