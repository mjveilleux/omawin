# install.ps1's look: the banner, the step tracker with its progress bar, the spinner for long
# native calls, the finish screen, and the log every line also goes to. Dot-sourced by
# install/lib.ps1; install/demo-ui.ps1 walks it through fake steps without installing anything.
#
# Two faces. Fancy: colors, glyphs and a spinner, in a console. Plain: the old "==> step" lines,
# whenever the output is redirected (omawin update's log, an agent), or OMAWIN_PLAIN=1 or NO_COLOR
# is set. OMAWIN_PLAIN=0 forces fancy (recording the demo into a file).
#
# Runs under Windows PowerShell 5.1 too, so escapes come from [char]27 (5.1 has no `e), and this
# file stays ASCII: 5.1 reads a script without a BOM in the ANSI code page, so every glyph below
# is made from its code point. bootstrap.ps1 has its own small copy (it runs before the clone).

# Each step shows the time since the start, so slow steps are easy to spot.
$Started = Get-Date

$UI = @{
    Fancy = $false
    Width = 100
    Log = Join-Path $env:LOCALAPPDATA 'launcher\install.log'
    Steps = New-Object System.Collections.ArrayList     # @{ Name; Summary; State; Start; Seconds; Warnings }
    Current = $null
    Warnings = New-Object System.Collections.ArrayList  # "<step>: <text>", for the finish screen
    AlreadyThere = 0                                    # packages found installed, shown as one line
    Encoding = $null                                    # the console's own, put back at the end
    G = @{}                                             # glyphs
    C = @{}                                             # colors
}

function ConvertFrom-CodePoint([int[]]$Codes) { -join ($Codes | ForEach-Object { [string][char]$_ }) }

function Enable-VirtualTerminal {
    # conhost only reads escape sequences once a program asks; Windows Terminal always does, but
    # asking is harmless. A native program can switch it off again, so each step asks anew.
    try {
        if (-not ('Omawin.Vt' -as [type])) {
            Add-Type -Namespace Omawin -Name Vt -MemberDefinition @'
[DllImport("kernel32.dll")] public static extern IntPtr GetStdHandle(int n);
[DllImport("kernel32.dll")] public static extern bool GetConsoleMode(IntPtr h, out uint m);
[DllImport("kernel32.dll")] public static extern bool SetConsoleMode(IntPtr h, uint m);
public static bool Enable() {
    IntPtr h = GetStdHandle(-11);
    uint m;
    if (!GetConsoleMode(h, out m)) return false;
    return (m & 4) != 0 || SetConsoleMode(h, m | 4);
}
'@
        }
        [Omawin.Vt]::Enable()
    } catch { $false }
}

function Test-FancyOutput {
    if ($env:OMAWIN_PLAIN -eq '0') { [void](Enable-VirtualTerminal); return $true }
    if ($env:OMAWIN_PLAIN -eq '1' -or $env:NO_COLOR) { return $false }
    if ([Console]::IsOutputRedirected -or $Host.Name -ne 'ConsoleHost') { return $false }
    Enable-VirtualTerminal
}

# Called by lib.ps1 as it loads, so every script that uses it (install.ps1, preset.ps1) gets it.
# $Caller: the script, for the log.
function Initialize-InstallUI([string]$Caller) {
    $UI.Fancy = [bool](Test-FancyOutput)
    try {
        New-Item -ItemType Directory -Force -Path (Split-Path $UI.Log) | Out-Null
        # One run's worth is a few hundred lines; keep the file from growing for ever.
        if ((Test-Path $UI.Log) -and (Get-Item $UI.Log).Length -gt 1MB) { Move-Item $UI.Log "$($UI.Log).old" -Force }
    } catch { }
    Write-Log "==== $Caller"
    if (-not $UI.Fancy) { return }

    try { $UI.Width = [Math]::Max(40, [Console]::WindowWidth) } catch { }
    try {
        if (-not $UI.Encoding) { $UI.Encoding = [Console]::OutputEncoding }
        [Console]::OutputEncoding = New-Object Text.UTF8Encoding $false
    } catch { }

    # Terminals that set these (Windows Terminal, noctty, herdr) fall back to other fonts for a
    # glyph the font lacks; the old console doesn't, so it gets ones every console font has.
    $rich = [bool]($env:WT_SESSION -or $env:TERM_PROGRAM -or $env:TERM)
    $g = $UI.G
    $g.Pending = ConvertFrom-CodePoint 0x25CB
    $g.Running = ConvertFrom-CodePoint 0x25CF
    $g.Warn = '!'
    $g.Bar = ConvertFrom-CodePoint 0x2588
    $g.Track = ConvertFrom-CodePoint 0x2591
    $g.Dot = ConvertFrom-CodePoint 0x00B7
    $g.H = ConvertFrom-CodePoint 0x2500
    $g.V = ConvertFrom-CodePoint 0x2502
    if ($rich) {
        $g.Done = ConvertFrom-CodePoint 0x2713
        $g.Fail = ConvertFrom-CodePoint 0x2717
        $g.Skip = ConvertFrom-CodePoint 0x2013
        $g.Sub = ConvertFrom-CodePoint 0x25B8
        $g.Corners = ConvertFrom-CodePoint 0x256D, 0x256E, 0x2570, 0x256F
        $g.Spin = @(0x280B, 0x2819, 0x2839, 0x2838, 0x283C, 0x2834, 0x2826, 0x2827, 0x2807, 0x280F | ForEach-Object { [string][char]$_ })
    } else {
        $g.Done = ConvertFrom-CodePoint 0x221A
        $g.Fail = ConvertFrom-CodePoint 0x00D7
        $g.Skip = '-'
        $g.Sub = ConvertFrom-CodePoint 0x25BA
        $g.Corners = ConvertFrom-CodePoint 0x250C, 0x2510, 0x2514, 0x2518
        $g.Spin = @('|', '/', '-', '\')
    }

    # Bright enough for a dark background, deep enough for a light one; the accent is the orange
    # of the default theme (Starship).
    $e = [char]27
    $rgb = { param($r, $gr, $b) "$e[38;2;$r;$gr;${b}m" }
    $c = $UI.C
    $c.Accent = & $rgb 255 159 10
    $c.Green = & $rgb 48 209 88
    $c.Yellow = & $rgb 230 170 0
    $c.Red = & $rgb 255 69 58
    $c.Dim = & $rgb 140 140 150
    $c.Bold = "$e[1m"
    $c.Reset = "$e[0m"
    $c.Clear = "`r$e[2K"
    $c.HideCursor = "$e[?25l"
    $c.ShowCursor = "$e[?25h"
    $c.Logo = @((& $rgb 255 196 0), (& $rgb 255 168 10), (& $rgb 255 140 30), (& $rgb 255 112 50), (& $rgb 255 84 70), (& $rgb 255 58 92))
}

# Put the console back as it was (its encoding) once the install is over.
function Close-InstallUI {
    if (-not $UI.Fancy) { return }
    try { if ($UI.Encoding) { [Console]::OutputEncoding = $UI.Encoding } } catch { }
}

function Write-Log([string]$Text) {
    try { [IO.File]::AppendAllText($UI.Log, ('{0:yyyy-MM-dd HH:mm:ss}  {1}{2}' -f (Get-Date), $Text, [Environment]::NewLine)) } catch { }
}

function Write-UILine([string]$Text) { [Console]::Out.WriteLine($Text) }

function Format-Duration([double]$Seconds) {
    if ($Seconds -lt 1) { return '<1s' }
    if ($Seconds -lt 60) { return '{0}s' -f [int][Math]::Floor($Seconds) }
    '{0}m {1:00}s' -f [int][Math]::Floor($Seconds / 60), [int][Math]::Floor($Seconds % 60)
}

function Format-Clock {
    $t = (Get-Date) - $Started
    if ($t.TotalHours -ge 1) { return '{0}:{1:mm\:ss}' -f [int][Math]::Floor($t.TotalHours), $t }
    '{0:mm\:ss}' -f $t
}

# --- what the steps call: headings, notes, warnings ------------------------------------------

function Write-Step([string]$Text) {
    Write-Log "==> $Text"
    if (-not $UI.Fancy) {
        Write-Host ("`n==> {0}   [{1}]" -f $Text, (Format-Clock)) -ForegroundColor Cyan
        return
    }
    # The step's own name repeated as its first heading ('no ads' in no-ads) says nothing new.
    if ($UI.Current -and (($Text -replace '\W') -eq ($UI.Current.Name -replace '\W'))) { return }
    $c = $UI.C
    Write-UILine ("    {0}{1}{2} {3}  {4}{5}{2}" -f $c.Accent, $UI.G.Sub, $c.Reset, $Text, $c.Dim, (Format-Clock))
}

function Write-Note([string]$Text) {
    Write-Log "    $Text"
    if (-not $UI.Fancy) { Write-Host "    $Text" -ForegroundColor DarkGray; return }
    $c = $UI.C
    if ($Text.StartsWith('ok  ')) { Write-UILine ("      {0}{1}{2} {3}{4}{2}" -f $c.Green, $UI.G.Done, $c.Reset, $c.Dim, $Text.Substring(4)) }
    else { Write-UILine ("      {0}{1}{2}" -f $c.Dim, $Text, $c.Reset) }
}

function Write-Warn([string]$Text) {
    Write-Log "    ! $Text"
    $where = if ($UI.Current) { $UI.Current.Name } else { 'install' }
    [void]$UI.Warnings.Add("${where}: $Text")
    if ($UI.Current) { $UI.Current.Warnings++ }
    if (-not $UI.Fancy) { Write-Host "    ! $Text" -ForegroundColor Yellow; return }
    Write-UILine ("      {0}{1} {2}{3}" -f $UI.C.Yellow, $UI.G.Warn, $Text, $UI.C.Reset)
}

# Something already in place. Plain shows it as before; fancy counts it, for Write-OkCount.
function Write-Ok([string]$Text) {
    if (-not $UI.Fancy) { Write-Note "ok  $Text"; return }
    Write-Log "    ok  $Text"
    $UI.AlreadyThere++
}

function Write-OkCount([string]$What = 'already installed') {
    if ($UI.Fancy -and $UI.AlreadyThere) {
        $c = $UI.C
        Write-UILine ("      {0}{1}{2} {3}{4} {5}{2}" -f $c.Green, $UI.G.Done, $c.Reset, $c.Dim, $UI.AlreadyThere, $What)
    }
    $UI.AlreadyThere = 0
}

# Something just done, with how long it took (fancy only; plain said "installing x" before it).
function Write-Done([string]$Text, [double]$Seconds) {
    Write-Log "    done  $Text ($(Format-Duration $Seconds))"
    if (-not $UI.Fancy) { return }
    $c = $UI.C
    Write-UILine ("      {0}{1}{2} {3}  {4}{5}{2}" -f $c.Green, $UI.G.Done, $c.Reset, $Text, $c.Dim, (Format-Duration $Seconds))
}

# --- the step tracker --------------------------------------------------------------------------

function Add-InstallStep([string]$Name, [string]$Summary, [switch]$Skip) {
    $state = if ($Skip) { 'skipped' } else { 'pending' }
    [void]$UI.Steps.Add(@{ Name = $Name; Summary = $Summary; State = $state; Seconds = 0; Warnings = 0 })
}

function Get-InstallStep([string]$Name) {
    foreach ($s in $UI.Steps) { if ($s.Name -eq $Name) { return $s } }
    Add-InstallStep $Name
    $UI.Steps[$UI.Steps.Count - 1]
}

function Format-InstallBar {
    $todo = @($UI.Steps | Where-Object { $_.State -ne 'skipped' })
    $done = @($todo | Where-Object { $_.State -in 'done', 'warned', 'failed' }).Count
    $width = 24
    $fill = if ($todo.Count) { [int][Math]::Round($width * $done / $todo.Count) } else { $width }
    $c = $UI.C
    '{0}{1}{2}{3}{4}  {5}/{6} {7} {8}{4}' -f $c.Accent, ($UI.G.Bar * $fill), $c.Dim, ($UI.G.Track * ($width - $fill)), $c.Reset,
        $done, $todo.Count, $UI.G.Dot, (Format-Clock)
}

function Enter-InstallStep([string]$Name) {
    $s = Get-InstallStep $Name
    $s.State = 'running'
    $s.Start = Get-Date
    $UI.Current = $s
    Write-Log "[$Name]"
    if (-not $UI.Fancy) { return }
    [void](Enable-VirtualTerminal)
    $todo = @($UI.Steps | Where-Object { $_.State -ne 'skipped' })
    $n = [array]::IndexOf(@($todo | ForEach-Object { $_.Name }), $Name) + 1
    $c = $UI.C
    Write-UILine ''
    Write-UILine ("  {0}{1}{2} {3}{4}{2}  {5}step {6}/{7}{2}" -f $c.Accent, $UI.G.Running, $c.Reset, $c.Bold, $Name, $c.Dim, $n, $todo.Count)
}

# The step's result line: its glyph, its time, and the overall bar. -Failure: the error that
# stopped it.
function Exit-InstallStep($Failure) {
    $s = $UI.Current
    if (-not $s) { return }
    $UI.Current = $null
    $s.Seconds = ((Get-Date) - $s.Start).TotalSeconds
    $s.State = if ($Failure) { 'failed' } elseif ($s.Warnings) { 'warned' } else { 'done' }
    Write-Log ("[{0}] {1} in {2}{3}" -f $s.Name, $s.State, (Format-Duration $s.Seconds), $(if ($Failure) { ": $Failure" }))
    if (-not $UI.Fancy) { return }
    $c = $UI.C
    $mark = switch ($s.State) {
        'failed' { $c.Red + $UI.G.Fail }
        'warned' { $c.Yellow + $UI.G.Warn }
        default { $c.Green + $UI.G.Done }
    }
    $time = Format-Duration $s.Seconds
    if ($s.Warnings) { $time += " $($UI.G.Dot) $($s.Warnings) warning$(if ($s.Warnings -gt 1) { 's' })" }
    Write-UILine ("  {0}{1} {2}{3}{1}  {4}{5}{1}  {6}" -f $mark, $c.Reset, $c.Bold, $s.Name.PadRight(12), $c.Dim, $time.PadRight(18), (Format-InstallBar))
    if ($Failure) { Write-UILine ("    {0}{1}{2}" -f $c.Red, $Failure, $c.Reset) }
}

# --- a native program, with a spinner while it runs --------------------------------------------

function ConvertTo-NativeArgument([string]$Arg) {
    # The quoting PowerShell gives an argument with spaces or quotes when it calls a program.
    if ($Arg -and $Arg -notmatch '[\s"]') { return $Arg }
    '"' + (($Arg -replace '(\\*)"', '$1$1\"') -replace '(\\+)$', '$1$1') + '"'
}

# Run $Exe with its output captured (to the log, and the last lines on a failure), animating
# $Label until it exits. Only the fancy face calls this; the spinner costs nothing, since it only
# redraws while the program runs. Returns its exit code, output and time.
function Invoke-NativeStatus([string]$Exe, [string[]]$Arguments, [string]$Label) {
    $psi = New-Object Diagnostics.ProcessStartInfo
    $psi.FileName = $Exe
    $psi.Arguments = (@($Arguments | ForEach-Object { ConvertTo-NativeArgument $_ }) -join ' ')
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = [Text.Encoding]::UTF8
    $psi.StandardErrorEncoding = [Text.Encoding]::UTF8
    $t0 = Get-Date
    $p = [Diagnostics.Process]::Start($psi)
    $out = $p.StandardOutput.ReadToEndAsync()
    $err = $p.StandardError.ReadToEndAsync()
    $c = $UI.C
    $room = [Math]::Max(10, $UI.Width - 22)
    if ($Label.Length -gt $room) { $Label = $Label.Substring(0, $room - 3) + '...' }
    $i = 0
    [Console]::Out.Write($c.HideCursor)
    try {
        while (-not $p.WaitForExit(90)) {
            $frame = $UI.G.Spin[$i % $UI.G.Spin.Count]
            [Console]::Out.Write(("{0}      {1}{2}{3} {4}  {5}{6}{3}" -f $c.Clear, $c.Accent, $frame, $c.Reset, $Label, $c.Dim, (Format-Duration ((Get-Date) - $t0).TotalSeconds)))
            $i++
        }
        $p.WaitForExit()
    } finally { [Console]::Out.Write($c.Clear + $c.ShowCursor) }
    $output = $out.Result + $err.Result
    Write-Log ("    $ $([IO.Path]::GetFileName($Exe)) $($psi.Arguments)  (exit $($p.ExitCode))")
    if ($p.ExitCode -ne 0 -and $output.Trim()) { Write-Log ($output.Trim() -replace "`r", '') }
    [pscustomobject]@{ ExitCode = $p.ExitCode; Output = $output; Seconds = ((Get-Date) - $t0).TotalSeconds }
}

# The last few meaningful lines of a program's output (winget's progress bars aside), dim.
function Write-OutputTail([string]$Output, [int]$Lines = 3) {
    if (-not $UI.Fancy) { return }
    $keep = @(($Output -split "[`r`n]+") | ForEach-Object { $_.Trim() } |
        Where-Object { $_ -and $_ -notmatch '^[-\\|/ ]+$' -and $_ -notmatch '[\u2580-\u259F]' } | Select-Object -Last $Lines)
    foreach ($line in $keep) { Write-UILine ("        {0}{1}{2}" -f $UI.C.Dim, $line, $UI.C.Reset) }
}

# --- the banner and the finish screen ------------------------------------------------------------

# "omawin" in figlet's ANSI Shadow, one letter at a time: # is a full block, r 7 L J = | are the
# double box-drawing corners and lines (ASCII here, glyphs at print time; see the top).
$Logo = @(
    @(' ######7 ', '##r===##7', '##|   ##|', '##|   ##|', 'L######rJ', ' L=====J '),
    @('###7   ###7', '####7 ####|', '##r####r##|', '##|L##rJ##|', '##| L=J ##|', 'L=J     L=J'),
    @(' #####7 ', '##r==##7', '#######|', '##r==##|', '##|  ##|', 'L=J  L=J'),
    @('##7    ##7', '##|    ##|', '##| #7 ##|', '##|###7##|', 'L###r###rJ', ' L==JL==J '),
    @('##7', '##|', '##|', '##|', '##|', 'L=J'),
    @('###7   ##7', '####7  ##|', '##r##7 ##|', '##|L##7##|', '##| L####|', 'L=J  L===J')
)

function Write-InstallBanner([string]$Version, [string]$What) {
    if (-not $UI.Fancy) {
        Write-Host "omawin ${Version}: $What" -ForegroundColor Cyan
        return
    }
    $c = $UI.C
    Write-UILine ''
    # bootstrap.ps1 showed the logo a moment ago; once is enough.
    if ($env:OMAWIN_BANNER -ne 'shown') {
        $map = @{ '#' = 0x2588; 'r' = 0x2554; '7' = 0x2557; 'L' = 0x255A; 'J' = 0x255D; '=' = 0x2550; '|' = 0x2551 }
        for ($row = 0; $row -lt 6; $row++) {
            $line = -join ($Logo | ForEach-Object { $_[$row] })
            $line = -join ($line.ToCharArray() | ForEach-Object { if ($map.ContainsKey([string]$_)) { [string][char]$map[[string]$_] } else { [string]$_ } })
            Write-UILine ("  {0}{1}{2}" -f $c.Logo[$row], $line, $c.Reset)
        }
        Write-UILine ''
    }
    $dot = " $($UI.G.Dot) "
    Write-UILine ("  {0}Omakase Windows{1}{2}{3}keyboard-driven developer desktop{3}{4}{1}" -f $c.Bold, $c.Reset, $c.Dim, $dot, $Version)
    Write-UILine ("  {0}{1}{2}" -f $c.Dim, $What, $c.Reset)
}

# The steps about to run, each with what it does, all pending.
function Write-InstallPlan {
    if (-not $UI.Fancy) { return }
    $c = $UI.C
    Write-UILine ''
    foreach ($s in $UI.Steps) {
        if ($s.State -eq 'skipped') {
            Write-UILine ("    {0}{1} {2} {3}{4}" -f $c.Dim, $UI.G.Skip, $s.Name.PadRight(12), 'skipped (-LinkOnly)', $c.Reset)
        } else {
            Write-UILine ("    {0}{1}{2} {3}{4}{5}{2}" -f $c.Dim, $UI.G.Pending, $c.Reset, $s.Name.PadRight(12), $c.Dim, $s.Summary)
        }
    }
}

function Get-InstallVersion([string]$Dir) {
    # The tag and commit (v0.1.0-5-gbd7af8a), or 'dev'. git's complaints must not stop the install.
    $ErrorActionPreference = 'Continue'
    if (Get-Command git -ErrorAction SilentlyContinue) {
        try { $v = & git -C $Dir describe --tags --always 2>$null; if ($LASTEXITCODE -eq 0 -and $v) { return [string]$v } } catch { }
    }
    'dev'
}

function Write-InstallLogPath {
    if ($UI.Fancy) { Write-UILine ("`n  {0}log  {1}{2}`n" -f $UI.C.Dim, $UI.Log, $UI.C.Reset) }
}

# What the install did and what's next, or which step stopped it.
function Write-InstallSummary {
    $Failed = @($UI.Steps | Where-Object { $_.State -eq 'failed' } | ForEach-Object { $_.Name }) | Select-Object -First 1
    $elapsed = Format-Duration ((Get-Date) - $Started).TotalSeconds
    $done = @($UI.Steps | Where-Object { $_.State -in 'done', 'warned' }).Count
    $skipped = @($UI.Steps | Where-Object { $_.State -eq 'skipped' }).Count
    Write-Log ("finished: {0} steps done, {1} warnings, {2}{3}" -f $done, $UI.Warnings.Count, $elapsed, $(if ($Failed) { ", stopped at $Failed" }))

    if (-not $UI.Fancy) {
        if ($Failed) { return }  # the error says it
        Write-Step 'done'
        Write-Note 'Sign out and back in: the desktop (GlazeWM, the bar, the launcher) starts at sign-in.'
        Write-Note 'Then Win+Enter for a terminal; the first nvim start installs its plugins. `omawin help` lists every command.'
        if ($UI.Warnings.Count) { Write-Note "$($UI.Warnings.Count) warning(s) above" }
        Write-Note "log: $($UI.Log)"
        return
    }

    $c = $UI.C
    $g = $UI.G
    $inner = [Math]::Min(56, $UI.Width - 6)
    $rows = New-Object System.Collections.ArrayList
    # Each row: its text (padded by its plain length) and the color it's drawn in.
    if ($Failed) { [void]$rows.Add(@("$($g.Fail) install stopped at $Failed", $c.Red)) }
    elseif ($UI.Warnings.Count) { [void]$rows.Add(@("$($g.Done) omawin is installed, with warnings", $c.Yellow)) }
    else { [void]$rows.Add(@("$($g.Done) omawin is installed", $c.Green)) }
    [void]$rows.Add(@('', ''))
    $steps = "$done done" + $(if ($skipped) { ", $skipped skipped" }) + $(if ($Failed) { ', 1 failed' })
    [void]$rows.Add(@(('steps     {0}' -f $steps), ''))
    [void]$rows.Add(@(('warnings  {0}' -f $UI.Warnings.Count), $(if ($UI.Warnings.Count) { $c.Yellow } else { '' })))
    [void]$rows.Add(@(('time      {0}' -f $elapsed), ''))

    Write-UILine ''
    Write-UILine ("  {0}{1}{2}{3}{4}" -f $c.Accent, $g.Corners[0], ($g.H * ($inner + 2)), $g.Corners[1], $c.Reset)
    foreach ($r in $rows) {
        $text = [string]$r[0]
        if ($text.Length -gt $inner) { $text = $text.Substring(0, $inner) }
        $color = if ($r[1]) { $r[1] } else { $c.Reset }
        Write-UILine ("  {0}{1}{2} {3}{4}{2}{5} {0}{1}{2}" -f $c.Accent, $g.V, $c.Reset, $color, $text, (' ' * ($inner - $text.Length)))
    }
    Write-UILine ("  {0}{1}{2}{3}{4}" -f $c.Accent, $g.Corners[2], ($g.H * ($inner + 2)), $g.Corners[3], $c.Reset)

    if ($UI.Warnings.Count) {
        Write-UILine ''
        Write-UILine ("  {0}Warnings, and what to do{1}" -f $c.Bold, $c.Reset)
        foreach ($w in $UI.Warnings) { Write-UILine ("    {0}{1} {2}{3}" -f $c.Yellow, $g.Warn, $w, $c.Reset) }
    }

    Write-UILine ''
    if ($Failed) {
        Write-UILine ("  {0}Next{1}" -f $c.Bold, $c.Reset)
        Write-UILine ("    {0}1{1}  fix what the error above says" -f $c.Accent, $c.Reset)
        Write-UILine ("    {0}2{1}  run install.ps1 again {2}(it skips what's done; -Step {3} runs just that one){1}" -f $c.Accent, $c.Reset, $c.Dim, $Failed)
    } else {
        $next = @(
            @('Sign out and back in', 'the desktop (GlazeWM, the bar, the launcher) starts at sign-in'),
            @('Win+Space', 'the launcher'),
            @('Win+Enter', 'a terminal; the first nvim start installs its plugins'),
            @('omawin check', '0 failed, 0 warnings is the bar; omawin help lists every command')
        )
        Write-UILine ("  {0}Next{1}" -f $c.Bold, $c.Reset)
        for ($i = 0; $i -lt $next.Count; $i++) {
            Write-UILine ("    {0}{1}{2}  {3}{4}{2}  {5}{6}{2}" -f $c.Accent, ($i + 1), $c.Reset, $c.Bold, $next[$i][0].PadRight(20), $c.Dim, $next[$i][1])
        }
    }
    Write-InstallLogPath
}
