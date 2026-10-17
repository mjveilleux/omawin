<#
.SYNOPSIS
    omawin profile terminal: where the time goes when a terminal opens, measured.

.DESCRIPTION
    Opens terminals the way the keys do, -Runs times, on workspace -Workspace (8 by default),
    and reads the timing marks every step writes to %LOCALAPPDATA%\launcher\trace.log while
    trace-on exists. Every mark is a QueryPerformanceCounter value, the one clock all processes
    share, so they line up exactly:

      driver   this script: the key (it asks GlazeWM's CLI to run the key's command)
      omawin   omawin.exe (bin/omawin.cs): created, running, asked the terminal
      noctty   our noctty fork (src/os/omawin_trace.zig): got the request, made the window,
               took the spare shell (or started one), showed the window, the shell's first
               output, the first frame on screen
      pwsh     the PowerShell profile: created, profile ran, first prompt

    -Scenario
      window   Win+Enter with a terminal in front (default): a spare shell is ready
      cold     Win+Enter on an empty workspace with noctty just started and no window opened
               yet, as for the first terminal after login: no spare shell yet
      tab      a new tab in the terminal in front (noctty.com +new-tab; the Ctrl+Shift+T key
               does the same inside noctty, without the request from outside)

    -Video saves a 60 fps recording of the whole screen alongside. It closes the terminals it
    opened on that workspace and switches back afterwards.
#>
param([int]$Runs = 5, [string]$Workspace = '8', [ValidateSet('window', 'cold', 'tab')][string]$Scenario = 'window', [string]$Video)

$ErrorActionPreference = 'Stop'
$state = Join-Path $env:LOCALAPPDATA 'launcher'
$traceOn = Join-Path $state 'trace-on'
$traceLog = Join-Path $state 'trace.log'
$freq = [Diagnostics.Stopwatch]::Frequency
$nocttyCom = Join-Path $env:LOCALAPPDATA 'Programs\noctty\noctty.com'

function Mark([string]$Label) { [IO.File]::AppendAllText($traceLog, "driver $Label`t$([Diagnostics.Stopwatch]::GetTimestamp())`n") }
function Count([string]$Prefix) { if (-not (Test-Path $traceLog)) { return 0 }; @((Get-Content $traceLog) | Where-Object { $_.StartsWith($Prefix) }).Count }
function TerminalWindows {
    $ws = (glazewm query workspaces | ConvertFrom-Json).data.workspaces | Where-Object name -eq $Workspace
    function Walk($c) { foreach ($x in $c.children) { if ($x.type -eq 'window') { $x } else { Walk $x } } }
    @(Walk $ws | Where-Object { $_.processName -eq 'noctty' })
}
function CloseTerminals { foreach ($w in TerminalWindows) { glazewm command focus --container-id $w.id | Out-Null; Start-Sleep -Milliseconds 150; glazewm command close | Out-Null } }
# The background noctty (omawin start terminal) started afresh: no spare shell until a window opens.
function RestartBackgroundNoctty {
    Get-CimInstance Win32_Process -Filter "Name='noctty.exe'" | Where-Object CommandLine -match 'initial-window=false' | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
    Start-Sleep -Milliseconds 500
    glazewm command shell-exec --hide-window omawin.exe start terminal | Out-Null
    Start-Sleep 2
}
# Done when the terminal's first frame is up and, if a shell started from scratch, its prompt.
function WaitDone([int]$frames, [int]$prompts, [bool]$needPrompt) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt 8000) {
        if ((Count 'noctty first-frame') -gt $frames -and (-not $needPrompt -or (Count 'pwsh first-prompt') -gt $prompts)) { return }
        Start-Sleep -Milliseconds 5
    }
}

$previous = ((glazewm query workspaces | ConvertFrom-Json).data.workspaces | Where-Object hasFocus).name
New-Item -ItemType Directory -Force $state | Out-Null
$recorder = $null
try {
    glazewm command focus --workspace $Workspace | Out-Null
    Start-Sleep -Milliseconds 500
    if (-not (Get-Process noctty -ErrorAction SilentlyContinue)) { RestartBackgroundNoctty }
    if ($Scenario -ne 'cold') { Start-Process omawin.exe -ArgumentList 'launch', 'terminal' -WindowStyle Hidden; Start-Sleep 2.5 } # the terminal in front
    Set-Content $traceOn ''
    Remove-Item $traceLog -ErrorAction SilentlyContinue

    if ($Video) {
        $ff = (Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\Gyan.FFmpeg*\*\bin\ffmpeg.exe" | Select-Object -First 1).FullName
        $recorder = New-Object Diagnostics.Process
        $recorder.StartInfo.FileName = $ff
        $recorder.StartInfo.Arguments = "-y -loglevel error -f lavfi -i ddagrab=framerate=60:draw_mouse=0,hwdownload,format=bgra -c:v libx264 -preset ultrafast -crf 14 -pix_fmt yuv420p `"$Video`""
        $recorder.StartInfo.UseShellExecute = $false
        $recorder.StartInfo.RedirectStandardInput = $true
        $recorder.StartInfo.CreateNoWindow = $true
        $null = $recorder.Start()
        Start-Sleep 1
    }

    for ($run = 1; $run -le $Runs; $run++) {
        if ($Scenario -eq 'cold') { CloseTerminals; RestartBackgroundNoctty }
        $frames = Count 'noctty first-frame'; $prompts = Count 'pwsh first-prompt'
        Mark "run-$run"
        Mark 'key'
        if ($Scenario -eq 'tab') { Start-Process $nocttyCom -ArgumentList '+new-tab' -WindowStyle Hidden }
        else { glazewm command shell-exec --hide-window omawin.exe terminal here | Out-Null }
        WaitDone $frames $prompts ($Scenario -eq 'cold')
        Start-Sleep -Milliseconds 1200 # let the next spare shell start before the next run
    }
} finally {
    if ($recorder) { $recorder.StandardInput.Write('q'); $recorder.WaitForExit(15000) | Out-Null }
    Remove-Item $traceOn -ErrorAction SilentlyContinue
    CloseTerminals
    if ($previous -and $previous -ne $Workspace) { glazewm command focus --workspace $previous | Out-Null }
}

# --- the numbers -------------------------------------------------------------------------------
$marks = foreach ($line in Get-Content $traceLog) {
    $label, $at = $line -split "`t"
    if ($at) { [pscustomobject]@{ Label = $label; At = [long]$at } }
}
$labels = [ordered]@{
    'omawin process-created'    = 'omawin.exe created by Windows'
    'omawin main terminal here' = 'omawin code running'
    'omawin found-noctty'       = 'omawin: noctty in front'
    'omawin noctty-listed'      = 'omawin: got its folder (+list-windows)'
    'omawin noctty-new-window'  = 'omawin: asks noctty (+new-window)'
    'noctty ipc-new-window'     = 'noctty got the request'
    'noctty surface-create'     = 'noctty creating the window/tab'
    'noctty gl-start'           = 'noctty: OpenGL context...'
    'noctty gl-done'            = '...made'
    'noctty fonts-start'        = 'noctty: fonts...'
    'noctty fonts-done'         = '...loaded'
    'noctty spare-taken'        = 'noctty took the spare shell'
    'noctty shell-ready'        = 'noctty shell ready (spare or new)'
    'noctty window-shown'       = 'noctty window shown'
    'noctty first-output'       = "the shell's first output read"
    'noctty first-frame'        = 'first frame on screen'
    'noctty window-placed'      = 'GlazeWM placed it'
    'noctty revealed'           = 'faded in, fully visible'
    'noctty spare-start'        = 'noctty starts the next spare shell'
    'pwsh process-created'      = 'pwsh.exe created (spare: the next one)'
    'pwsh profile-start'        = 'pwsh running the profile'
    'pwsh profile-end'          = 'profile done'
    'pwsh first-prompt'         = 'first prompt written'
}
$timelines = @()
$keys = @($marks | Where-Object Label -eq 'driver key')
for ($i = 0; $i -lt $keys.Count; $i++) {
    $key = $keys[$i].At
    $next = if ($i + 1 -lt $keys.Count) { $keys[$i + 1].At } else { [long]::MaxValue }
    $t = @{}
    foreach ($label in $labels.Keys) {
        $m = $marks | Where-Object { $_.Label -eq $label -and $_.At -ge $key -and $_.At -lt $next } | Sort-Object At | Select-Object -First 1
        if ($m) { $t[$label] = ($m.At - $key) * 1000.0 / $freq }
    }
    # The prompt is on screen at the first frame that has the shell's output in it: a spare
    # shell's prompt was already written, so that's its output read and a frame after it; a
    # shell started now writes it at "first prompt".
    $written = if ($t.ContainsKey('noctty spare-taken')) { $t['noctty first-output'] } else { $t['pwsh first-prompt'] }
    if ($null -ne $written -and $t.ContainsKey('noctty first-frame')) { $t['prompt-on-screen'] = [math]::Max($written, $t['noctty first-frame']) }
    $timelines += , $t
}
function Median($values) { $v = @($values | Where-Object { $null -ne $_ } | Sort-Object); if ($v) { $v[[math]::Floor(($v.Count - 1) / 2)] } }
function Row([string]$name, $values) {
    if (-not @($values | Where-Object { $null -ne $_ }).Count) { return }
    $line = '{0,-40}' -f $name
    foreach ($v in $values) { $line += if ($null -ne $v) { '{0,7:0}' -f $v } else { '{0,7}' -f '-' } }
    $line += '{0,8:0}' -f (Median $values)
    Write-Host $line
}
$head = '{0,-40}' -f ''
for ($i = 1; $i -le $timelines.Count; $i++) { $head += '{0,7}' -f "run $i" }
Write-Host "`nScenario: $Scenario. Timeline: ms since the key`n"
Write-Host ($head + '{0,8}' -f 'median')
foreach ($label in $labels.Keys) { Row $labels[$label] ($timelines | ForEach-Object { $_[$label] }) }

function Span($r, $a, $b) { if (($null -eq $a -or $r.ContainsKey($a)) -and $r.ContainsKey($b)) { $from = if ($null -eq $a) { 0 } else { $r[$a] }; if ($r[$b] -ge $from) { $r[$b] - $from } } }
$spans = [ordered]@{
    'key -> omawin.exe created'           = @($null, 'omawin process-created')
    'omawin start-up (.NET)'              = @('omawin process-created', 'omawin main terminal here')
    'omawin: ask noctty the folder'       = @('omawin found-noctty', 'omawin noctty-listed')
    'omawin -> noctty got the request'    = @('omawin noctty-new-window', 'noctty ipc-new-window')
    'noctty: request -> window shown'     = @('noctty ipc-new-window', 'noctty window-shown')
    'noctty: window shown -> first frame' = @('noctty window-shown', 'noctty first-frame')
    'new shell: pwsh start-up'            = @('pwsh process-created', 'pwsh profile-start')
    'new shell: profile'                  = @('pwsh profile-start', 'pwsh profile-end')
    'new shell: profile -> prompt'        = @('pwsh profile-end', 'pwsh first-prompt')
    'TOTAL key -> window shown'           = @($null, 'noctty window-shown')
    'TOTAL key -> first frame'            = @($null, 'noctty first-frame')
    'TOTAL key -> prompt on screen'       = @($null, 'prompt-on-screen')
    'TOTAL key -> fully visible (fade)'   = @($null, 'noctty revealed')
}
Write-Host "`nSteps (ms)`n"
Write-Host ($head + '{0,8}' -f 'median')
foreach ($name in $spans.Keys) {
    $a, $b = $spans[$name]
    # A spare shell's pwsh marks belong to the next spare starting in the background.
    Row $name ($timelines | ForEach-Object { if ($name -like 'new shell*' -and $_.ContainsKey('noctty spare-taken')) { $null } else { Span $_ $a $b } })
}
