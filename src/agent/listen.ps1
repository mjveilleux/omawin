# Ctrl+Win+H's second half. omawin-listen.exe (src/agent/omawin-listen.cs) records while the bar says
# "omawin is listening"; on the second press it hands the recording here, hidden:
#
#   1. Handy turns it into words (handy --transcribe-file: Handy's model, without Handy typing
#      anything into the window you're in)
#   2. the herdr session "omawin-agent" (started at login by src/agent/start.ps1, with OpenCode's
#      service in its first tab) gets a new tab, in this repo, running live.ps1 with the words:
#      the agent does it there, in the open, as a client of that service
#   3. a terminal attached to that session is opened if there isn't one; a GlazeWM window rule
#      puts it on workspace 9, so where you are stays as it is
param([Parameter(Mandatory)][string]$Wav)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

try {
    # 1. The words.
    Set-AgentState 'working' 'Listening to you'
    $handy = (Get-Command handy -ErrorAction SilentlyContinue).Source
    if (-not $handy) { $handy = Join-Path $env:LOCALAPPDATA 'Handy\handy.exe' }
    $out = Join-Path $AgentDir 'request.json'
    # handy.exe is a windowed program: PowerShell neither waits for it nor reads its output, so
    # it's started with its output in a file.
    $p = Start-Process $handy -ArgumentList '--transcribe-file', "`"$Wav`"", '--json' -Wait -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $out -RedirectStandardError "$out.log"
    $text = try { ((Get-Content $out -Raw | ConvertFrom-Json).text).Trim() } catch { '' }
    if (-not $text) { Set-AgentState 'failed' "Didn't catch that (Handy: exit $($p.ExitCode))"; exit 1 }

    $requests = Join-Path $AgentDir 'requests'
    New-Item -ItemType Directory -Force $requests | Out-Null
    $file = Join-Path $requests ("{0:yyyy-MM-dd_HHmmss}.txt" -f (Get-Date))
    [IO.File]::WriteAllText($file, $text)
    Set-AgentState 'working' $text @{ request = $text }

    # 2. The herdr session (agent start, at login, has usually started it) and a tab for this request.
    $session = Start-AgentSession (Get-AgentConfig)
    $label = (($text -split '\s+') | Select-Object -First 4) -join ' '
    $created = if ($session.free) { $session.free } else {
        (Invoke-AgentHerdr tab create --workspace $session.workspace --cwd $AgentRepo --label $label | ConvertFrom-Json).result
    }
    $pane = $created.root_pane.pane_id
    $tab = $created.root_pane.tab_id
    if (-not $pane) { throw "herdr made no tab" }
    Invoke-AgentHerdr tab rename $tab $label | Out-Null
    Invoke-AgentHerdr tab focus $tab | Out-Null
    Invoke-AgentHerdr pane run $pane "pwsh -NoLogo -NoProfile -File `"$PSScriptRoot\live.ps1`" -RequestFile `"$file`"" | Out-Null

    # 3. A window on it, unless one is open.
    Open-AgentWindow | Out-Null
    Set-AgentState 'done' "Sent to $AgentSession (workspace 9): $text" @{ request = $text }
} catch {
    Set-AgentState 'failed' $_.Exception.Message
    exit 1
}
