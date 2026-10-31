<#
.SYNOPSIS
    Set up a Windows machine from this repo: install tools, the Nerd Font, and link configs.

.DESCRIPTION
    Safe to re-run; every step skips what's already done. Configs are linked, not copied,
    so editing them in place (nvim, $PROFILE, Windows Terminal's settings UI) edits this repo.
    Directory junctions are used because they need neither admin nor Developer Mode.
    Anything already at a target path is moved aside to <path>.backup-<timestamp>.

    Runs under Windows PowerShell 5.1 too, so it works on a fresh machine before pwsh exists.

    In order:
      1. the questions, all at once (which presets, on a first install; your git name, if git has
         none), so nothing waits on a person after that
      2. the one admin prompt (install/admin.ps1), when anything needs admin: the whole-PC
         packages, omawin's GlazeWM, and WSL for the wsl preset, done in the
         background from then on. Nothing asks for admin after it, approved or not.
      3. the steps listed in install/steps.txt, each its file in install/ (-Step runs just one)
      4. the migrations not run on this PC yet (bin/migrate.ps1: what older versions left behind)

.EXAMPLE
    .\install.ps1                # everything
    .\install.ps1 -LinkOnly      # no installs: relink, rebuild
    .\install.ps1 -Step shell    # one step (install/shell.ps1)

    omawin update (bin/update.ps1) runs it with -LinkOnly -UpdateInfo <file>: what the update
    pulled, shown as its first step ("update"), the changes as items, and the finale saying
    what's new.
#>
param(
    [switch]$LinkOnly,     # no packages, presets, other installers or the font
    [string]$Step,         # run only install/<Step>.ps1
    [string]$UpdateInfo    # omawin update's: "<kind>`t<text>" lines (from, to, commit, note, warn, seconds)
)

$ErrorActionPreference = 'Stop'
# PowerShell 7 only (bootstrap.ps1 puts it in first, from the release's tools bundle). Started
# from Windows PowerShell 5.1, this goes again in 7, with the same arguments.
if ($PSVersionTable.PSVersion.Major -lt 7) {
    $pwsh = @((Get-Command pwsh -CommandType Application -ErrorAction SilentlyContinue | ForEach-Object Source),
        "$env:LOCALAPPDATA\omawin\tools\pwsh\pwsh.exe") | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $pwsh) { throw 'install.ps1 runs in PowerShell 7. Run the one-line install (bootstrap.ps1), which puts it in first.' }
    $again = @('-NoLogo', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath)
    foreach ($p in $PSBoundParameters.GetEnumerator()) {
        if ($p.Value -is [switch]) { if ($p.Value) { $again += "-$($p.Key)" } } else { $again += "-$($p.Key)", "$($p.Value)" }
    }
    & $pwsh @again
    exit $LASTEXITCODE
}
# Never as administrator (see bootstrap.ps1): it would set up whichever account the admin
# password belongs to, not the one signed in. The admin step asks for admin itself.
if (([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "This PowerShell runs as administrator ($env:USERNAME): run install.ps1 from a normal one; it asks for admin where it needs it." -ForegroundColor Yellow
    exit 1
}
$Repo = $PSScriptRoot
$Stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
. "$Repo\install\packages.ps1"
. "$Repo\install\lib.ps1"
$UI.Mode = if ($LinkOnly) { 'link' } else { 'full' }
$Steps = @(Get-InstallSteps)

# A step's output that isn't a Write-Note of its own (a script's lines) becomes its notes. Each
# step is dot-sourced, in this scope, as it always was (piping its output doesn't change that).

# One step. A step that throws still stops the install, as it always has; it's marked failed first.
if ($Step) {
    $one = $Steps | Where-Object Name -eq $Step
    if (-not $one) { throw "no step '$Step'; the steps (install/steps.txt): $(($Steps | ForEach-Object Name) -join ', ')" }
    Add-InstallStep $one.Name $one.Summary $one.Seconds
    Enter-InstallStep $Step
    try { . "$Repo\install\$Step.ps1" | ForEach-Object { if ("$_".Trim()) { Write-Note "$_" } } } catch { Exit-InstallStep $_; Close-InstallUI; throw }
    Exit-InstallStep
    Write-InstallLogPath
    Close-InstallUI
    return
}

# What omawin update did before this ran (bin/update.ps1).
$Update = $null
if ($UpdateInfo -and (Test-Path $UpdateInfo)) {
    $Update = @{ From = ''; To = ''; Commits = @(); Notes = @(); Warnings = @(); Seconds = 0 }
    foreach ($line in Get-Content $UpdateInfo -Encoding UTF8) {
        $kind, $text = $line -split "`t", 2
        switch ($kind) {
            'from' { $Update.From = $text }
            'to' { $Update.To = $text }
            'commit' { $Update.Commits += $text }
            'note' { $Update.Notes += $text }
            'warn' { $Update.Warnings += $text }
            'seconds' { $Update.Seconds = [double]$text }
        }
    }
    Remove-Item $UpdateInfo -ErrorAction SilentlyContinue
}

$version = Get-InstallVersion $Repo
$what = if ($Update) {
    if ($Update.From -and $Update.From -ne $Update.To) { "update $($Update.From) -> $($Update.To)" } else { 'update: already up to date' }
} elseif ($LinkOnly) { 'relink and rebuild (-LinkOnly)' } else { 'set up this PC' }
$UI.SaveTimes = $true
foreach ($s in $Steps) { Add-InstallStep $s.Name $s.Summary $s.Seconds }
Add-InstallStep 'migrations' 'clear away what older versions left' 2
if ($Update) { Add-InstallStep 'update' 'this setup, from GitHub' 3 -First }
Start-InstallBoard $version $what
Write-InstallBanner $version $what
Write-InstallPlan
$markKind = if ($Update) { 'update' } else { 'install' }
Start-InstallMark $markKind

try {
    # --- 0. omawin update: what it pulled ---------------------------------------------------------
    if ($Update) {
        Enter-InstallStep 'update'
        $UI.Current.Start = $UI.Current.Start.AddSeconds(-$Update.Seconds)   # the pull, before this ran
        Write-Step 'from GitHub'
        foreach ($n in $Update.Notes) { Write-Note $n }
        foreach ($w in $Update.Warnings) { Write-Warn $w }
        $i = 0
        foreach ($c in $Update.Commits) { $i++; Add-InstallItem 'Changes' "change:$i" $c 'done' }
        # yt-dlp breaks whenever YouTube changes; the music player needs a current one.
        if (Get-Command yt-dlp -ErrorAction SilentlyContinue) {
            Add-InstallItem 'Programs' 'yt-dlp.yt-dlp' 'yt-dlp' 'run'
            & { $ErrorActionPreference = 'Continue'; winget upgrade --id yt-dlp.yt-dlp -e --silent --accept-package-agreements --accept-source-agreements *> $null }
            Add-InstallItem 'Programs' 'yt-dlp.yt-dlp' 'yt-dlp' $(if ($LASTEXITCODE -eq 0) { 'done' } else { 'have' })
        }
        Exit-InstallStep
        $n = $Update.Commits.Count
        $UI.FinaleTitle = if ($n) { "omawin is updated: $n change$(if ($n -ne 1) { 's' })" } else { 'omawin is already up to date' }
        $UI.FinaleSection = if ($n) { "What's new" } else { '' }
        $UI.FinaleNext = @(foreach ($c in @($Update.Commits | Select-Object -First $(if ($n -gt 6) { 5 } else { 6 }))) { , @('', $c) })
        if ($n -gt 6) { $UI.FinaleNext += , @('', "and $($n - 5) more (git -C ~/omawin log)") }
    }

    # --- 1. the questions ------------------------------------------------------------------------
    $SoftwareFunctionsOnly = $true
    . "$Repo\install\software.ps1"
    $SoftwareFunctionsOnly = $false
    . "$Repo\install\admin.ps1"
    Send-Board 'enter' 'before it starts'
    Write-Step 'before it starts'
    if (-not $LinkOnly) { Initialize-Presets }
    $GitAsked = Request-GitIdentity

    # --- 2. the one admin prompt -----------------------------------------------------------------
    Write-Note 'what needs admin ...'
    # What the release's tools bundle carries goes in per-user without admin (install/software.ps1).
    $bundled = @(if (-not $LinkOnly) { Get-BundleProvides })
    # Its unpacking starts now, in the background, and runs alongside everything up to the end of
    # the software step (install/unpack-tools.ps1). "Yours" is judged by the PATH here; winget's
    # list isn't read yet.
    if (-not $LinkOnly) { Start-ToolBundle $null; Start-EarlyHelpers; Start-EarlyExtras @($CoreExtras) }
    # Windows' ads off, from the first second, in a PowerShell of its own: its step (later in the
    # order) only collects what it said. It only changes this account's settings and stops the PC
    # maker's background extras, so it runs alongside the other steps safely.
    $NoAdsEarly = $null
    if ($Steps.Name -contains 'no-ads') {
        $noAdsLog = Join-Path $env:TEMP "omawin-no-ads-$Stamp"
        $NoAdsEarly = @{ Out = "$noAdsLog.log"; Err = "$noAdsLog.err.log"
            Process = Start-Process (Join-Path $PSHOME 'pwsh.exe') -ArgumentList '-NoLogo', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$Repo\bin\no-ads.ps1`"", '-UserOnly' -RedirectStandardOutput "$noAdsLog.log" -RedirectStandardError "$noAdsLog.err.log" -WindowStyle Hidden -PassThru }
        $null = $NoAdsEarly.Process.Handle  # read now, so its ExitCode is there later
    }
    Write-Log ("    bundle and helpers started at {0:0.0} s" -f ((Get-Date) - $Started).TotalSeconds)
    $plan = Get-AdminPlan @(if (-not $LinkOnly) { Get-WantedPackages | Where-Object { $bundled -notcontains $_ } }) -NoPackages:$LinkOnly
    Write-Log ("    admin plan done at {0:0.0} s" -f ((Get-Date) - $Started).TotalSeconds)
    if ($plan.Packages.Count -or $plan.Forks.Count -or $plan.Wsl) {
        Add-InstallStep 'admin' 'what every account shares, after one prompt' -First
        # omawin update's pull came before it.
        if ($Update) { $u = Get-InstallStep 'update'; $UI.Steps.Remove($u); $UI.Steps.Insert(0, $u); Sync-BoardSteps }
        [void](Start-AdminBatch $plan)
    } else { Write-Note 'ok  nothing needs admin' }
    # That was the one prompt, approved or not: from here on nothing asks for admin (the steps,
    # the scripts they run, the migrations). What only admin can do is left out and said.
    $env:OMAWIN_NO_ADMIN = '1'

    # --- 3. the steps ----------------------------------------------------------------------------
    foreach ($s in $Steps) {
        Enter-InstallStep $s.Name
        try { . "$Repo\install\$($s.Name).ps1" | ForEach-Object { if ("$_".Trim()) { Write-Note "$_" } } } catch { Exit-InstallStep $_; Write-InstallSummary; throw }
        Exit-InstallStep
    }

    # --- 4. the migrations -----------------------------------------------------------------------
    Enter-InstallStep 'migrations'
    Write-Step 'migrations'
    $global:LASTEXITCODE = 0  # not a stale one from a step's native program
    & "$Repo\bin\migrate.ps1" | ForEach-Object { if ("$_".Trim()) { Write-Note "$_" } }
    if ($LASTEXITCODE -ne 0) {
        # migrate.ps1 already said which one failed; the rest run on the next install or omawin migrate.
        Write-Warn 'a migration failed (above); fix it, then run: omawin migrate'
        Exit-InstallStep 'a migration failed'
        Complete-AdminBatch
        Write-InstallSummary
        Close-InstallUI
        exit 1
    }
    Exit-InstallStep
    Complete-AdminBatch
    Write-InstallSummary
} finally {
    Close-InstallUI
    # The bar's install glyph goes, and its toast says how it went (after the screen's finale).
    Stop-InstallMark $markKind
    # (The shell that ran this keeps its environment: a later -Step may ask for admin again.)
    Remove-Item Env:OMAWIN_NO_ADMIN -ErrorAction SilentlyContinue
}
