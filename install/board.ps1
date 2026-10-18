# install.ps1's screen: two panes redrawn in place, smoothly. On the left, everything the install
# has touched, by group (Programs, Tools, Settings, Desktop, Windows), and what's running now; on
# the right, the omawin logo, lit by a snake as the install gets further, the percentage, and every
# step on a line. When it ends, the list of everything goes into the scrollback.
#
# It's a small program, install/screen.cs, built here once with Windows' own csc.exe (and again when
# screen.cs changes) into %LOCALAPPDATA%\launcher. It draws at up to 60 frames a second on its own;
# this file starts it and tells it what happens, one line per event (screen.cs has the list). It keeps
# nothing of its own: the steps come from install/steps.txt, the rest from what the steps report
# through ui.ps1 (Write-Step, Write-Note, Write-Warn, Add-InstallItem). No compiler, a console too
# small, or the program gone: ui.ps1's line-by-line face, as before.
#
# Dot-sourced by install/ui.ps1. ASCII only, like ui.ps1.

$BoardMinWidth = 80
$BoardMinHeight = 24

# The console's size; OMAWIN_SCREEN=<width>x<height> fixes it (recording the demo into a file).
function Get-ScreenSize {
    if ($env:OMAWIN_SCREEN -match '^(\d+)x(\d+)$') { return @([int]$Matches[1], [int]$Matches[2]) }
    @([Console]::WindowWidth, [Console]::WindowHeight)
}
function Test-BoardFits {
    if ($env:OMAWIN_BOARD -eq '0') { return $false }
    try { $size = Get-ScreenSize; $size[0] -ge $BoardMinWidth -and $size[1] -ge $BoardMinHeight } catch { $false }
}

# The screen program, built for this version of screen.cs (its hash in the name), or $null.
function Get-ScreenExe {
    $source = Join-Path $PSScriptRoot 'screen.cs'
    $dir = Join-Path $env:LOCALAPPDATA 'launcher'
    $sha = [Security.Cryptography.SHA256]::Create()
    $hash = (-join ($sha.ComputeHash([IO.File]::ReadAllBytes($source)) | ForEach-Object { $_.ToString('x2') })).Substring(0, 12)
    $exe = Join-Path $dir "omawin-screen-$hash.exe"
    if (Test-Path $exe) { return $exe }
    $csc = Get-ChildItem "$env:WINDIR\Microsoft.NET\Framework64\v4*\csc.exe", "$env:WINDIR\Microsoft.NET\Framework\v4*\csc.exe" -ErrorAction SilentlyContinue | Select-Object -Last 1
    if (-not $csc) { Write-Log '    (the screen: no csc.exe on this PC)'; return $null }
    New-Item -ItemType Directory -Force $dir | Out-Null
    Get-ChildItem $dir -Filter 'omawin-screen-*.exe' -ErrorAction SilentlyContinue | Remove-Item -ErrorAction SilentlyContinue
    $ErrorActionPreference = 'Continue'
    $output = & $csc.FullName /nologo /target:exe /optimize "/out:$exe" $source 2>&1
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $exe)) { Write-Log "    (the screen didn't build: $($output -join ' '))"; return $null }
    $exe
}

function Start-InstallBoard([string]$Version, [string]$What) {
    if (-not $UI.Fancy -or $UI.Board) { return }
    # A terminal that has just opened (Windows Terminal, started for the install) may still be
    # at its first size for a moment: give it up to two seconds to settle.
    for ($i = 0; $i -lt 20 -and -not (Test-BoardFits); $i++) { if ($env:OMAWIN_BOARD -eq '0') { break }; Start-Sleep -Milliseconds 100 }
    $size = try { (Get-ScreenSize) -join 'x' } catch { 'unknown' }
    if (-not (Test-BoardFits)) { Write-Log "    (the screen needs a console of $($BoardMinWidth)x$($BoardMinHeight); this one is $($size): line by line instead)"; return }
    $exe = try { Get-ScreenExe } catch { Write-Log "    (the screen: $_)"; $null }
    if (-not $exe) { return }
    try {
        $psi = New-Object Diagnostics.ProcessStartInfo $exe
        $psi.UseShellExecute = $false
        $psi.RedirectStandardInput = $true
        $p = [Diagnostics.Process]::Start($psi)
        $writer = New-Object IO.StreamWriter($p.StandardInput.BaseStream, (New-Object Text.UTF8Encoding $false))
        $writer.AutoFlush = $true
    } catch { Write-Log "    (the screen didn't start: $_)"; return }
    $UI.Board = @{ P = $p; W = $writer }
    $UI.AdminItems = @{}
    Write-Log "    (the screen, in a console of $size)"
    # No progress bars from cmdlets (Invoke-WebRequest's): they'd draw over the screen.
    $script:ProgressPreference = 'SilentlyContinue'
    $rich = [bool]($env:WT_SESSION -or $env:TERM_PROGRAM -or $env:TERM)
    Send-Board 'init' $Version $What $UI.Log $(if ($rich) { '1' } else { '0' })
    Sync-BoardSteps
}

# One event for the screen: its fields, tab-separated, on one line.
function Send-Board {
    $B = $UI.Board
    if (-not $B) { return }
    $line = (@($args | ForEach-Object { ("$_" -replace '[\t\r\n]+', ' ') }) -join "`t")
    try { $B.W.WriteLine($line) }
    catch {
        # The screen is gone (closed, crashed): carry on line by line.
        $UI.Board = $null
        Write-Log "    (the screen stopped: $($_.Exception.Message))"
        [Console]::Out.Write("$([char]27)[0m$([char]27)[?25h`n")
    }
}

# Every step as it stands, in order.
function Sync-BoardSteps {
    if (-not $UI.Board) { return }
    $inv = [Globalization.CultureInfo]::InvariantCulture
    Send-Board 'order' (@($UI.Steps | ForEach-Object { $_.Name }) -join ',')
    foreach ($s in @($UI.Steps)) {
        Send-Board 'step' $s.Name $s.State ([double]$s.Seconds).ToString($inv) ([double]$s.Expected).ToString($inv) $s.Note
    }
}

# The last frame stays on the screen, and the list of everything goes below it.
function Stop-InstallBoard {
    $B = $UI.Board
    if (-not $B) { return }
    Send-Board 'quit'
    try { $B.W.Close() } catch { }
    # A finished install's finale plays out first (screen.cs, DrawFinale).
    try { if (-not $B.P.WaitForExit(8000)) { $B.P.Kill() } } catch { }
    $UI.Board = $null
    [Console]::Out.Write("$([char]27)[0m$([char]27)[?25h")
}

function Add-BoardLine([string]$Kind, [string]$Text, $Seconds) {
    $s = if ($Seconds) { ([double]$Seconds).ToString([Globalization.CultureInfo]::InvariantCulture) } else { '' }
    Send-Board 'line' $Kind $Text $s
}

# Something the install put in place or found there, for the screen's list: $Group (Programs,
# Tools, Settings, Desktop, Windows), an id, its name, and its state: wait, run, done (new), have
# (already there) or fail. -Admin: the admin step does it.
function Add-InstallItem([string]$Group, [string]$Id, [string]$Name, [string]$State, [switch]$Admin) {
    Write-Log "    item $Group $Name $State"
    if (-not $UI.Board) { return }
    if ($Admin) { $UI.AdminItems[$Id] = $true }
    Send-Board 'item' $Group $Id $Name $State $(if ($UI.AdminItems[$Id]) { 'admin' })
}
# Runs $Do as one item: its output becomes notes, and the item is "have" if all it said was
# "ok  ...", "done" if it did anything, "fail" if it threw (a warning, not the end of the step).
function Invoke-InstallItem([string]$Group, [string]$Name, [scriptblock]$Do) {
    Add-InstallItem $Group $Name $Name 'run'
    $before = [int]$UI.Done
    try { & $Do | ForEach-Object { if ("$_".Trim()) { Write-Note "$_" } } }
    catch { Write-Warn "$($Name): $_"; Add-InstallItem $Group $Name $Name 'fail'; return }
    Add-InstallItem $Group $Name $Name $(if ([int]$UI.Done -gt $before) { 'done' } else { 'have' })
}
function Set-BoardPackage([string]$Id, [string]$State, [switch]$Admin) {
    if (-not $UI.Board) { return }
    $name = if (Get-Command Get-PackageName -ErrorAction SilentlyContinue) { Get-PackageName $Id } else { $Id }
    Add-InstallItem 'Programs' $Id $name $State -Admin:$Admin
}
function Set-BoardPackageNote([string]$Text) { Send-Board 'note' $Text }

# The admin prompt's screen: what it's for ("<what>|<detail>" each), while Windows asks.
function Show-AdminRequest([string[]]$What) {
    $s = Get-InstallStep 'admin'
    $s.State = 'running'
    $s.Start = Get-Date
    $s.Note = 'waiting'
    if ($UI.Board) { Sync-BoardSteps; Send-Board 'admin' @($What); return }
    Write-Step 'one admin prompt'
    Write-Note "Windows asks for admin once, now, for what every account on this PC shares:"
    foreach ($w in $What) { $p = $w -split '\|', 2; Write-Note "  $($p[0])$(if ($p.Count -gt 1) { "  ($($p[1]))" })" }
    Write-Note 'Nothing asks again after this. Say no, and each one asks on its own as it installs.'
}
function Close-AdminRequest([bool]$Approved) {
    $s = Get-InstallStep 'admin'
    if ($Approved) { $s.State = 'background'; $s.Note = 'busy' }
    else { $s.State = 'skipped'; $s.Note = 'declined' }
    if ($UI.Board) { Sync-BoardSteps; Send-Board 'adminclose' }
}

# Asks while the screen is up: the screen pauses, the question goes on its bottom line.
function Read-BoardAnswer([string]$Question) {
    Send-Board 'pause'
    Start-Sleep -Milliseconds 80
    $e = [char]27
    $row = (Get-ScreenSize)[1] - 1
    [Console]::Out.Write("$e[$row;1H$e[0m$e[2K  $($UI.C.Accent)?$($UI.C.Reset) $Question $e[?25h")
    try { $answer = [Console]::In.ReadLine() }
    finally {
        [Console]::Out.Write("$e[?25l")
        Send-Board 'resume'
    }
    "$answer"
}
