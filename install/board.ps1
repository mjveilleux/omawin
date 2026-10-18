# install.ps1's screen: two panes that redraw in place. On the left, what the step running now is
# doing (its packages as a grid, or its lines); on the right, the omawin logo, lit as far as the
# install has got by a snake that slides along its strokes, the percentage, and every step on a line.
# Dot-sourced by install/ui.ps1; used in a console big enough for it (Test-BoardFits), else
# ui.ps1's line-by-line face.
#
# The screen keeps nothing of its own: it draws $UI.Steps (from install/steps.txt) and what the
# steps report through ui.ps1's Write-Step / Write-Note / Write-Warn and Set-BoardPackage. A
# second thread (a runspace) draws it ~12 times a second, so the snake and the spinners move
# even while a step is busy in one long call; the steps' thread never writes to the console
# while it's up (Read-InstallAnswer pauses it to ask something).
#
# ASCII only, like ui.ps1: glyphs come from code points ($UI.G).

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

function Start-InstallBoard([string]$Version, [string]$What) {
    if (-not $UI.Fancy -or $UI.Board) { return }
    # A terminal that has just opened (Windows Terminal, started for the install) may still be
    # at its first size for a moment: give it up to two seconds to settle.
    for ($i = 0; $i -lt 20 -and -not (Test-BoardFits); $i++) { if ($env:OMAWIN_BOARD -eq '0') { break }; Start-Sleep -Milliseconds 100 }
    $size = try { (Get-ScreenSize) -join 'x' } catch { 'unknown' }
    if (-not (Test-BoardFits)) { Write-Log "    (the screen needs a console of $($BoardMinWidth)x$($BoardMinHeight); this one is $($size): line by line instead)"; return }
    Write-Log "    (the screen, in a console of $size)"
    $B = [hashtable]::Synchronized(@{})
    $B.Steps = $UI.Steps
    $B.Log = [Collections.ArrayList]::Synchronized((New-Object Collections.ArrayList))
    $B.Pkgs = [Collections.ArrayList]::Synchronized((New-Object Collections.ArrayList))
    $B.G = $UI.G
    $B.Version = $Version
    $B.What = $What
    $B.Started = $Started
    $B.LogPath = $UI.Log
    $B.Mode = 'log'
    $B.Legend = ''
    $B.Main = [runspace]::DefaultRunspace
    $rs = [runspacefactory]::CreateRunspace()
    $rs.Open()
    $ps = [powershell]::Create()
    $ps.Runspace = $rs
    [void]$ps.AddScript($BoardScript).AddArgument($B)
    $UI.BoardPS = $ps
    $UI.BoardRS = $rs
    $UI.BoardIndex = @{}
    $UI.Board = $B
    # No progress bars from cmdlets (Invoke-WebRequest's): they'd draw over the screen.
    $script:ProgressPreference = 'SilentlyContinue'
    [Console]::Out.Write("$([char]27)[?25l$([char]27)[2J$([char]27)[H")
    $UI.BoardHandle = $ps.BeginInvoke()
}

# The last frame stays on the screen; the cursor goes below it.
function Stop-InstallBoard {
    $B = $UI.Board
    if (-not $B) { return }
    $B.Stop = $true
    try { [void]$UI.BoardHandle.AsyncWaitHandle.WaitOne(3000); [void]$UI.BoardPS.EndInvoke($UI.BoardHandle) }
    catch { Write-Log "    (the screen stopped: $($_.Exception.InnerException.Message))" }
    try { $UI.BoardPS.Dispose(); $UI.BoardRS.Dispose() } catch { }
    if ($B.Error) { Write-Log "    (the screen: $($B.Error))" }
    $UI.Board = $null
    [Console]::Out.Write("$([char]27)[?25h")
}

function Add-BoardLine([string]$Kind, [string]$Text, $Seconds) {
    $log = $UI.Board.Log
    [void]$log.Add(@{ K = $Kind; T = $Text; S = $Seconds })
    if ($log.Count -gt 300) { $log.RemoveRange(0, 100) }
}

# A package in the software step's grid: wait, run, done, have (there already) or fail. -Admin:
# the admin step installs it.
function Set-BoardPackage([string]$Id, [string]$State, [switch]$Admin) {
    $B = $UI.Board
    if (-not $B) { return }
    $p = $UI.BoardIndex[$Id]
    if (-not $p) {
        $name = if (Get-Command Get-PackageName -ErrorAction SilentlyContinue) { Get-PackageName $Id } else { $Id }
        $p = [hashtable]::Synchronized(@{ N = $name; State = $State; Admin = $false; T0 = Get-Date })
        $UI.BoardIndex[$Id] = $p
        [void]$B.Pkgs.Add($p)
        $B.Mode = 'packages'
    }
    if ($Admin) { $p.Admin = $true }
    if ($State -eq 'run' -and $p.State -ne 'run') { $p.T0 = Get-Date }
    $p.State = $State
}
function Set-BoardPackageNote([string]$Text) { if ($UI.Board) { $UI.Board.PkgNote = $Text } }

# The admin prompt's screen: what it's for ("<what>|<detail>" each), while Windows asks.
function Show-AdminRequest([string[]]$What) {
    $s = Get-InstallStep 'admin'
    $s.State = 'running'
    $s.Start = Get-Date
    $s.Note = 'waiting'
    if ($UI.Board) {
        $UI.Board.AdminWhat = $What
        $UI.Board.Legend = 'admin'
        $UI.Board.Mode = 'admin'
        return
    }
    Write-Step 'one admin prompt'
    Write-Note "Windows asks for admin once, now, for what every account on this PC shares:"
    foreach ($w in $What) { $p = $w -split '\|', 2; Write-Note "  $($p[0])$(if ($p.Count -gt 1) { "  ($($p[1]))" })" }
    Write-Note 'Nothing asks again after this. Say no, and each one asks on its own as it installs.'
}
function Close-AdminRequest([bool]$Approved) {
    $s = Get-InstallStep 'admin'
    if ($Approved) { $s.State = 'background'; $s.Note = 'busy' }
    else { $s.State = 'skipped'; $s.Note = 'declined' }
    if ($UI.Board) { $UI.Board.Mode = 'log'; $UI.Board.Legend = '' }
}

# Asks while the screen is up: the screen pauses, the question goes on its bottom line.
function Read-BoardAnswer([string]$Question) {
    $B = $UI.Board
    $B.PausedAck = $false
    $B.Paused = $true
    for ($i = 0; $i -lt 40 -and -not $B.PausedAck; $i++) { Start-Sleep -Milliseconds 25 }
    $e = [char]27
    $row = (Get-ScreenSize)[1] - 1
    [Console]::Out.Write("$e[$row;1H$e[2K  $($UI.C.Accent)?$($UI.C.Reset) $Question $e[?25h")
    try { $answer = [Console]::In.ReadLine() }
    finally {
        [Console]::Out.Write("$e[?25l")
        $B.Repaint = $true
        $B.Paused = $false
    }
    "$answer"
}

# --- the drawing thread -------------------------------------------------------------------------
# Everything below runs in the runspace, with the board ($B) as its only link to the install.
$BoardScript = @'
param($B)
$ErrorActionPreference = 'Stop'
$e = [char]27
$G = $B.G
$R = "$e[0m"
function Fg([int]$r, [int]$g, [int]$b) { "$e[38;2;$r;$g;${b}m" }
$C = @{
    Fg = Fg 217 217 224; Dim = Fg 128 128 140; Faint = Fg 74 74 86; Acc = Fg 255 159 10; AccDim = Fg 168 106 16
    Ok = Fg 61 214 140; Warn = Fg 242 201 76; Red = Fg 255 92 80; White = "$e[1m" + (Fg 255 255 255)
    Border = Fg 52 52 64; Hot = Fg 112 74 20
}

# The logo (fastfetch/logo.txt, two characters to a square): 27 x 26 squares, drawn two to a
# character with half blocks, so 27 x 13 characters.
$Rows = @('###########################','###########################','##...........##..........##','##...........##..........##',
    '##..###########....####..##','##..###########....####..##','##..##...............##..##','##..##...............##..##',
    '##..##...............##..##','##..##...............##..##','##..##...............##..##','##..##...............##..##',
    '######...............##..##','######...............##..##','##..##...............##..##','##..##...............##..##',
    '##..##...............##..##','##..##...............##..##','##..##...............##..##','##..##...............##..##',
    '##..###################..##','##..###################..##','##...........##..........##','##...........##..........##',
    '###############..##########','###############..##########')
$LW = 27; $LH = 26
function On([int]$x, [int]$y) { $x -ge 0 -and $y -ge 0 -and $x -lt $LW -and $y -lt $LH -and $Rows[$y][$x] -eq '#' }
$IsOn = New-Object bool[] ($LW * $LH)
for ($y = 0; $y -lt $LH; $y++) { for ($x = 0; $x -lt $LW; $x++) { $IsOn[$y * $LW + $x] = $Rows[$y][$x] -eq '#' } }
$Shade = @(@(255, 196, 0), @(255, 168, 10), @(255, 140, 30), @(255, 112, 50), @(255, 84, 70), @(255, 58, 92))
$SnakeColors = @(@(255, 244, 194), @(255, 210, 63), @(255, 210, 63), @(224, 181, 42), @(224, 181, 42), @(168, 138, 30), @(168, 138, 30), @(107, 90, 32), @(107, 90, 32))
$Unlit = @(36, 36, 46)
$WaveColor = @(255, 233, 168)
$ShadeOf = @(for ($y = 0; $y -lt $LH; $y++) { , $Shade[[Math]::Min(5, [Math]::Floor($y / $LH * 6))] })

# The snake's road: depth first along the strokes from the bottom-left corner, straight on where
# it can, stepping back the way it came at a dead end, so every move is one square.
$walk = New-Object Collections.Generic.List[int]
$seen = @{}
$dirs = @(@(1, 0), @(0, 1), @(-1, 0), @(0, -1))
$stack = New-Object Collections.Generic.Stack[object]
$seen[25 * $LW] = $true; $walk.Add(25 * $LW)
$stack.Push(@{ X = 0; Y = 25; D = 3; I = 0 })
while ($stack.Count) {
    $f = $stack.Peek()
    if ($f.I -lt 4) {
        $k = @($f.D, (($f.D + 1) % 4), (($f.D + 3) % 4), (($f.D + 2) % 4))[$f.I]
        $f.I++
        $nx = $f.X + $dirs[$k][0]; $ny = $f.Y + $dirs[$k][1]
        if ((On $nx $ny) -and -not $seen[$ny * $LW + $nx]) {
            $seen[$ny * $LW + $nx] = $true; $walk.Add($ny * $LW + $nx)
            $stack.Push(@{ X = $nx; Y = $ny; D = $k; I = 0 })
        }
    } else {
        [void]$stack.Pop()
        if ($stack.Count) { $t = $stack.Peek(); $walk.Add($t.Y * $LW + $t.X) }
    }
}

function P($Color, $Text) { @{ C = $Color; T = [string]$Text } }
function Raw([string]$Text, [int]$Width) { @{ Raw = $Text; N = $Width } }
function Get-Width($Pieces) { $n = 0; foreach ($p in $Pieces) { if ($p.Raw) { $n += $p.N } else { $n += $p.T.Length } }; $n }
# The pieces as exactly $Width characters: cut short with an ellipsis, or padded.
function Fit($Pieces, [int]$Width) {
    $sb = New-Object Text.StringBuilder
    $n = 0
    foreach ($p in $Pieces) {
        if ($n -ge $Width) { break }
        if ($p.Raw) { if ($n + $p.N -le $Width) { [void]$sb.Append($p.Raw).Append($R); $n += $p.N }; continue }
        $t = $p.T
        if ($n + $t.Length -gt $Width) { $t = $t.Substring(0, [Math]::Max(0, $Width - $n - 1)) + $G.Ell }
        [void]$sb.Append($p.C).Append($t).Append($R)
        $n += $t.Length
    }
    if ($n -lt $Width) { [void]$sb.Append(' ' * ($Width - $n)) }
    $sb.ToString()
}
function Center($Pieces, [int]$Width) {
    $n = Get-Width $Pieces
    if ($n -ge $Width) { return Fit $Pieces $Width }
    Fit (@(P '' (' ' * [Math]::Floor(($Width - $n) / 2))) + @($Pieces)) $Width
}
function Wrap([string]$Text, [int]$Width) {
    $lines = @(); $line = ''
    foreach ($word in ($Text -split ' ')) {
        if ($line -and ($line.Length + 1 + $word.Length) -gt $Width) { $lines += $line; $line = $word }
        elseif ($line) { $line += ' ' + $word } else { $line = $word }
    }
    if ($line) { $lines += $line }
    $lines
}
function Dur([double]$s) {
    if ($s -lt 1) { return '<1s' }
    if ($s -lt 60) { return '{0}s' -f [int][Math]::Floor($s) }
    '{0}m {1:00}s' -f [int][Math]::Floor($s / 60), [int][Math]::Floor($s % 60)
}
function Clock([double]$s) { '{0:00}:{1:00}' -f [int][Math]::Floor($s / 60), [int][Math]::Floor($s % 60) }

# A box: rounded border, the title in its top edge, the lines inside (centered both ways if $Middle).
function Box($Title, $Lines, [int]$W, [int]$H, [bool]$Hot, [bool]$Middle) {
    $bc = if ($Hot) { $C.Hot } else { $C.Border }
    $cn = $G.Corners
    $out = New-Object string[] $H
    if ($Title) {
        $tn = [Math]::Min((Get-Width $Title), $W - 8)
        $out[0] = $bc + $cn[0] + $G.H + $R + ' ' + (Fit $Title $tn) + ' ' + $bc + ($G.H * ($W - 5 - $tn)) + $cn[1] + $R
    } else { $out[0] = $bc + $cn[0] + ($G.H * ($W - 2)) + $cn[1] + $R }
    $inner = $H - 2
    $iw = $W - 4
    $list = @($Lines)
    if ($list.Count -gt $inner) { $list = $list[0..($inner - 1)] }
    $top = if ($Middle) { [Math]::Floor(($inner - $list.Count) / 2) } else { 0 }
    for ($i = 0; $i -lt $inner; $i++) {
        $j = $i - $top
        $content = if ($j -ge 0 -and $j -lt $list.Count) { $list[$j] } else { @() }
        $text = if ($Middle) { Center $content $iw } else { Fit $content $iw }
        $out[$i + 1] = $bc + $G.V + $R + ' ' + $text + ' ' + $bc + $G.V + $R
    }
    $out[$H - 1] = $bc + $cn[2] + ($G.H * ($W - 2)) + $cn[3] + $R
    $out
}

$script:head = 0
$script:pw = 0; $script:ph = 0
$prev = @{}
$lastFull = Get-Date

function Draw {
    if ($env:OMAWIN_SCREEN -match '^(\d+)x(\d+)$') { $W = [int]$Matches[1] - 1; $H = [int]$Matches[2] }
    else { $W = [Console]::WindowWidth - 1; $H = [Console]::WindowHeight }
    $full = $false
    if ($W -ne $script:pw -or $H -ne $script:ph) { $script:pw = $W; $script:ph = $H; [Console]::Out.Write("$e[2J"); $full = $true }
    # Anything else that wrote to the console is painted over once in a while.
    if ($B.Repaint -or ((Get-Date) - $script:lastFull).TotalSeconds -gt 1.5) { $full = $true; $B.Repaint = $false }
    if ($full) { $script:prev = @{}; $script:lastFull = Get-Date }

    $now = Get-Date
    $spin = $G.Spin[[int][Math]::Floor($now.TimeOfDay.TotalMilliseconds / 90) % $G.Spin.Count]
    $steps = @($B.Steps.ToArray())
    $final = $B.Final
    $pkgs = @($B.Pkgs.ToArray())
    $log = @($B.Log.ToArray())
    $elapsed = ($now - $B.Started).TotalSeconds

    # --- how far along: each step by its expected seconds; the running one by its time so far,
    # or by its packages when it has a grid.
    $total = 0.0; $got = 0.0
    foreach ($s in $steps) {
        if ($s.Name -eq 'admin' -or $s.State -eq 'skipped') { continue }
        $wt = [double]$s.Expected
        if ($wt -le 0) { $wt = 5 }
        $total += $wt
        if ($s.State -in 'done', 'warned', 'failed') { $got += $wt }
        elseif ($s.State -eq 'running' -and $s.Start) {
            $f = [Math]::Min(0.95, ($now - $s.Start).TotalSeconds / $wt)
            if ($B.Mode -eq 'packages' -and $pkgs.Count) {
                $fin = @($pkgs | Where-Object { $_.State -in 'done', 'have', 'fail' }).Count
                $f = [Math]::Max($f, 0.9 * $fin / $pkgs.Count)
            }
            $got += $wt * $f
        }
    }
    $p = if ($total -gt 0) { $got / $total } else { 0 }
    $ok = $final -and -not $final.Failed
    if ($ok) { $p = 1 }

    # --- the logo
    $target = [int][Math]::Floor($p * ($walk.Count - 1))
    if ($script:head -lt $target) { $script:head += [Math]::Max(1, [Math]::Ceiling(($target - $script:head) / 6)) }
    elseif ($script:head -gt $target) { $script:head = $target }
    $lit = @{}
    for ($i = 0; $i -le $script:head; $i++) { $lit[$walk[$i]] = $true }
    $snake = @{}
    if (-not $final) {
        for ($k = 0; $k -lt $SnakeColors.Count -and $script:head - $k -ge 0; $k++) {
            $key = $walk[$script:head - $k]
            if (-not $snake.ContainsKey($key)) { $snake[$key] = $SnakeColors[$k] }
        }
    }
    $wave = if ($ok) { (($now - $final.At).TotalSeconds * 24) % 90 - 10 } else { -99 }
    $logo = @()
    for ($row = 0; $row -lt 13; $row++) {
        $sb = New-Object Text.StringBuilder
        for ($x = 0; $x -lt $LW; $x++) {
            $up = $null; $dn = $null
            foreach ($y in (2 * $row), (2 * $row + 1)) {
                $key = $y * $LW + $x
                if (-not $IsOn[$key]) { continue }
                $cell = if ($snake.ContainsKey($key)) { $snake[$key] }
                        elseif ([Math]::Abs($x + $y - $wave) -lt 1.5) { $WaveColor }
                        elseif ($lit.ContainsKey($key)) { $ShadeOf[$y] }
                        else { $Unlit }
                if ($y -eq 2 * $row) { $up = $cell } else { $dn = $cell }
            }
            if ($up -and $dn) { [void]$sb.Append("$e[38;2;$($up[0]);$($up[1]);$($up[2]);48;2;$($dn[0]);$($dn[1]);$($dn[2])m").Append($G.Upper) }
            elseif ($up) { [void]$sb.Append("$e[49;38;2;$($up[0]);$($up[1]);$($up[2])m").Append($G.Upper) }
            elseif ($dn) { [void]$sb.Append("$e[49;38;2;$($dn[0]);$($dn[1]);$($dn[2])m").Append($G.Lower) }
            else { [void]$sb.Append("$e[0m ") }
        }
        $logo += , @(Raw $sb.ToString() $LW)
    }

    # --- the right pane: the logo, the percentage, the steps
    $pct = if ($final -and $final.Failed) { 'stopped' } else { "$([int][Math]::Floor($p * 100))%" }
    $eta = ''
    $adminWaiting = @($steps | Where-Object { $_.Name -eq 'admin' -and $_.State -eq 'running' }).Count
    if (-not $final) {
        if ($adminWaiting) { $eta = '  ' + $G.Dot + ' waiting for you' }
        elseif ($p -gt 0.04) {
            $secsLeft = $elapsed / $p * (1 - $p)
            $eta = '  ' + $G.Dot + $(if ($secsLeft -lt 60) { ' under a minute left' } else { " about $([int][Math]::Round($secsLeft / 60))m left" })
        }
    }
    $stepLines = @()
    foreach ($s in $steps) {
        $name = ([string]$s.Name).PadRight(12)
        switch ($s.State) {
            'running' {
                $time = if ($s.Note) { $s.Note } elseif ($s.Start) { Dur ($now - $s.Start).TotalSeconds } else { '' }
                $stepLines += , @((P $C.Acc $spin), (P $C.White (' ' + $name)), (P $C.Acc $time.PadLeft(9)))
            }
            'background' { $stepLines += , @((P $C.AccDim $spin), (P $C.Dim (' ' + $name)), (P $C.AccDim ([string]$s.Note).PadLeft(9))) }
            'done' { $stepLines += , @((P $C.Ok $G.Done), (P $C.Dim (' ' + $name + (Dur $s.Seconds).PadLeft(9)))) }
            'warned' { $stepLines += , @((P $C.Warn '!'), (P $C.Dim (' ' + $name + (Dur $s.Seconds).PadLeft(9)))) }
            'failed' { $stepLines += , @((P $C.Red $G.Fail), (P $C.Red (' ' + $name + (Dur $s.Seconds).PadLeft(9)))) }
            'skipped' { $stepLines += , @((P $C.Faint ($G.Skip + ' ' + $name + ([string]$(if ($s.Note) { $s.Note } else { 'skipped' })).PadLeft(9)))) }
            default { $stepLines += , @((P $C.Faint ($G.Dot + ' ' + $name + (' ' * 9)))) }
        }
    }
    $top = 2; $boxH = $H - 4
    $inner = $boxH - 2
    $right = @() + $logo
    $gap = $inner -ge (13 + 3 + $stepLines.Count + 1)
    if ($gap) { $right += , @() }
    $right += , @((P $C.White $pct), (P $C.Dim $eta))
    $room = $inner - $right.Count - 1
    if ($room -lt $stepLines.Count -and $room -gt 0) {
        # Not every step fits: the ones around the running one.
        $at = 0; for ($i = 0; $i -lt $steps.Count; $i++) { if ($steps[$i].State -eq 'running') { $at = $i } }
        $from = [Math]::Max(0, [Math]::Min($at - [Math]::Floor($room / 2), $stepLines.Count - $room))
        $stepLines = $stepLines[$from..($from + $room - 1)]
    }
    if ($room -gt 0) { $right += , @(); $right += $stepLines }

    # --- the left pane: what the running step is doing
    # The right side needs the logo's 27 columns and a border; the left side takes what's left.
    $rightW = [Math]::Max(33, [int][Math]::Floor(($W - 5) * 0.42))
    $leftW = $W - 5 - $rightW
    $iw = $leftW - 4
    $left = @()
    $legend = if ($final) { if ($final.Failed) { @(P $C.Red "$($G.Fail) stopped") } else { @(P $C.Ok "$($G.Done) installed") } }
              elseif ($B.Mode -eq 'admin') { @(P $C.Acc 'admin') }
              elseif ($B.Legend) { @(P $C.Acc $B.Legend) } else { $null }
    if ($final) {
        $wn = @($final.Warnings).Count
        if ($final.Failed) { $left += , @(P $C.Red "$($G.Fail) install stopped at $($final.Failed)") }
        elseif ($wn) { $left += , @(P $C.Warn "$($G.Done) omawin is installed, with $wn warning$(if ($wn -ne 1) { 's' })") }
        else { $left += , @(P $C.Ok "$($G.Done) omawin is installed") }
        $left += , @(P $C.Dim $final.Line)
        $left += , @()
        if ($final.Error) { foreach ($l in (Wrap $final.Error ($iw - 2))) { $left += , @(P $C.Red ('  ' + $l)) }; $left += , @() }
        $budget = $inner - $left.Count - 2 - @($final.Next).Count
        foreach ($warning in @($final.Warnings)) {
            $wl = @(Wrap $warning ($iw - 2))
            if ($budget -lt $wl.Count) { break }
            $left += , @((P $C.Warn '! '), (P $C.Warn $wl[0]))
            for ($i = 1; $i -lt $wl.Count; $i++) { $left += , @(P $C.Warn ('  ' + $wl[$i])) }
            $budget -= $wl.Count
        }
        if ($wn) { $left += , @() }
        $left += , @(P $C.White 'Next')
        $n = 0
        foreach ($nx in @($final.Next)) {
            $n++
            $left += , @((P $C.Acc "$n  "), (P $C.White ([string]$nx[0]).PadRight(22)), (P $C.Dim $nx[1]))
        }
    } elseif ($B.Mode -eq 'admin') {
        $left += , @(P $C.White 'Windows asks for admin once, now.')
        $left += , @(P $C.Dim 'For what every account on this PC shares:')
        $left += , @()
        foreach ($item in @($B.AdminWhat)) {
            $parts = ([string]$item) -split '\|', 2
            $left += , @((P $C.Acc ($G.Dot + ' ')), (P $C.Fg $parts[0]), (P $C.Dim $(if ($parts.Count -gt 1) { '  ' + $parts[1] })))
        }
        $left += , @()
        foreach ($l in (Wrap 'Nothing asks again after this. Say no, and each one asks on its own as it installs.' $iw)) { $left += , @(P $C.Dim $l) }
        $left += , @()
        $left += , @((P $C.Acc $spin), (P $C.Fg ' waiting for the Windows prompt'))
    } elseif ($B.Mode -eq 'packages') {
        $cols = if ($iw -ge 66) { 3 } elseif ($iw -ge 30) { 2 } else { 1 }
        $cw = [Math]::Floor($iw / $cols)
        $rowsN = [Math]::Ceiling($pkgs.Count / $cols)
        for ($gr = 0; $gr -lt $rowsN; $gr++) {
            $line = @()
            for ($gc = 0; $gc -lt $cols; $gc++) {
                $i = $gc * $rowsN + $gr
                if ($i -ge $pkgs.Count) { break }
                $pk = $pkgs[$i]
                $cell = switch ($pk.State) {
                    'done' { @((P $C.Ok $G.Done), (P $C.Ok (' ' + $pk.N))) }
                    'have' { @((P $C.Dim $G.Done), (P $C.Dim (' ' + $pk.N))) }
                    'run' { @((P $C.Acc $spin), (P $C.Acc (' ' + $pk.N))) }
                    'fail' { @((P $C.Red $G.Fail), (P $C.Red (' ' + $pk.N))) }
                    default { @((P $C.Faint ($G.Dot + ' ' + $pk.N))) }
                }
                $line += Raw (Fit $cell ($cw - 1)) ($cw - 1)
                $line += P '' ' '
            }
            $left += , $line
        }
        $running = @($pkgs | Where-Object { $_.State -eq 'run' })
        if ($running) {
            $left += , @()
            $track = [Math]::Max(8, [Math]::Min(24, $iw - 34)); $seg = 5
            $i = 0
            foreach ($pk in $running) {
                $span = 2 * ($track - $seg)
                $q = [int][Math]::Floor($now.TimeOfDay.TotalSeconds * 14 + $i * 7) % $span
                $pos = if ($q -lt $track - $seg) { $q } else { $span - $q }
                $tag = '  ' + $(if ($pk.Admin) { 'admin ' + $G.Dot + ' ' } else { '' }) + (Dur ($now - $pk.T0).TotalSeconds)
                $left += , @((P $C.Acc $spin), (P $C.Fg (' ' + $(if (([string]$pk.N).Length -gt 15) { ([string]$pk.N).Substring(0, 14) + $G.Ell } else { ([string]$pk.N).PadRight(15) }) + ' ')), (P $C.Faint ($G.Track * $pos)), (P $C.Acc ($G.Seg * $seg)), (P $C.Faint ($G.Track * ($track - $seg - $pos))), (P $C.Dim $tag))
                $i++
            }
        }
        $notes = @($log | Where-Object { $_.K -ne 'head' -or $_.T -notmatch 'winget' })
        $roomL = $inner - $left.Count - 3
        if ($notes -and $roomL -gt 1) {
            $left += , @()
            foreach ($l in @($notes | Select-Object -Last ($roomL - 1))) { $left += , (Get-LogLine $l $spin ($l -eq $log[-1])) }
        }
        $fin = @($pkgs | Where-Object { $_.State -in 'done', 'have' }).Count
        $foot = "$fin of $($pkgs.Count) in"
        $nrun = @($pkgs | Where-Object { $_.State -eq 'run' -and -not $_.Admin }).Count
        if ($nrun) { $foot += " $($G.Dot) $nrun at once" }
        if ($B.PkgNote) { $foot += " $($G.Dot) $($B.PkgNote)" }
        while ($left.Count -lt $inner - 1) { $left += , @() }
        $left += , @(P $C.Dim $foot)
    } else {
        foreach ($l in @($log | Select-Object -Last $inner)) { $left += , (Get-LogLine $l $spin ($l -eq $log[-1])) }
    }

    # --- the frame
    $lines = New-Object string[] $H
    $clock = if ($final) { 'done in ' + (Dur $final.Seconds) } else { Clock $elapsed }
    $lines[0] = (Fit @((P '' '  '), (P $C.White 'omawin'), (P $C.Dim ('  ' + $B.Version + ' ' + $G.Dot + ' ' + $B.What))) ($W - $clock.Length - 2)) + $C.Dim + $clock + $R + '  '
    $lines[1] = ' ' * $W
    $lb = Box $legend $left $leftW $boxH (-not $final) $false
    $rb = Box $null $right $rightW $boxH $false $true
    for ($i = 0; $i -lt $boxH; $i++) { $lines[$top + $i] = '  ' + $lb[$i] + '  ' + $rb[$i] + ' ' }
    $foot = if ($final -and -not $final.Failed) { 'sign out and back in: the desktop starts at sign-in' } else { 'log  ' + $B.LogPath }
    $lines[$H - 2] = Fit @(P $C.Dim ('  ' + $foot)) $W
    $lines[$H - 1] = ' ' * $W

    $sb = New-Object Text.StringBuilder
    for ($i = 0; $i -lt $H; $i++) {
        if ($script:prev[$i] -ne $lines[$i]) { [void]$sb.Append("$e[$($i + 1);1H").Append($lines[$i]); $script:prev[$i] = $lines[$i] }
    }
    if ($sb.Length) { [void]$sb.Append("$e[1;1H"); [Console]::Out.Write($sb.ToString()) }
}

# One of the step's lines: a heading (the last one spins while the step runs), a note, a done
# line, a warning.
function Get-LogLine($l, $spin, [bool]$Last) {
    switch ($l.K) {
        'head' { if ($Last -and -not $B.Final) { @((P $C.Acc $spin), (P $C.White (' ' + $l.T))) } else { @((P $C.AccDim $G.Sub), (P $C.Fg (' ' + $l.T))) } }
        'ok' { @((P '' '  '), (P $C.Ok $G.Done), (P $C.Dim (' ' + $l.T))) }
        'warn' { @((P '' '  '), (P $C.Warn '!'), (P $C.Warn (' ' + $l.T))) }
        'done' { @((P '' '  '), (P $C.Ok $G.Done), (P $C.Fg (' ' + $l.T)), (P $C.Dim ('  ' + (Dur $l.S)))) }
        'fail' { @((P '' '  '), (P $C.Red $G.Fail), (P $C.Red (' ' + $l.T))) }
        default { @((P $C.Dim ('    ' + $l.T))) }
    }
}

# The install's own runspace: busy while it runs when started from a prompt (powershell -File shows
# it idle throughout, and there the process ending ends this thread too).
$wasBusy = $B.Main -and $B.Main.RunspaceAvailability -ne 'Available'
while ($true) {
    $stop = $B.Stop
    # The install was stopped (Ctrl+C) without closing the screen: draw once more and leave.
    if ($wasBusy -and $B.Main.RunspaceAvailability -eq 'Available') { $stop = $true }
    if ($B.Paused -and -not $stop) { $B.PausedAck = $true; Start-Sleep -Milliseconds 40; continue }
    try { Draw } catch { $B.Error = "$($_.Exception.Message) ($($_.ScriptStackTrace -replace "`r?`n", " < "))" }
    if ($stop) { break }
    Start-Sleep -Milliseconds 80
}
[Console]::Out.Write("$e[0m$e[$($script:ph - 1);1H$e[?25h")
[Console]::Out.WriteLine()
'@
