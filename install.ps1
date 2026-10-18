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
         packages and omawin's GlazeWM and Zebar, done in the background from then on
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
# Never as administrator (see bootstrap.ps1): it would set up whichever account the admin
# password belongs to, not the one signed in. The admin step asks for admin itself.
if (([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "This PowerShell runs as administrator ($env:USERNAME): run install.ps1 from a normal one; it asks for admin where it needs it." -ForegroundColor Yellow
    exit 1
}
$Repo = $PSScriptRoot
$Stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
. "$Repo\packages.ps1"
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
    Request-GitIdentity
    $GitAsked = $true

    # --- 2. the one admin prompt -----------------------------------------------------------------
    Write-Note 'what needs admin ...'
    $plan = Get-AdminPlan @(if (-not $LinkOnly) { Get-WantedPackages }) -NoPackages:$LinkOnly
    if ($plan.Packages.Count -or $plan.Forks.Count) {
        Add-InstallStep 'admin' 'what every account shares, after one prompt' -First
        [void](Start-AdminBatch $plan)
    } else { Write-Note 'ok  nothing needs admin' }

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
    & "$Repo\bin\migrate.ps1"
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
}
