# omawin agent ask <request>: hand a request to omawin's agent, which does it in the background
# and answers in one line. Typed in a terminal it prints its steps and the answer.
#
# What runs it is a backend (agent/backends/<name>.ps1: opencode, claude), picked in
# agent/config.json with its model, and the same allow list for all of them. Your account folder's
# agent/config.json (docs/work.md), if there is one, overrides it: {"backend": "claude"}, or
# {"backends": {"opencode": {"model": "opencode/glm-5.3-flash"}}}.
#
# What it's doing is written to %LOCALAPPDATA%\launcher\agent\state, one JSON line the bar reads:
#   {"state":"working","step":"Searching the web for Hardcore History","request":…,"backend":…}
#   then "done" (step = the agent's one-line answer) or "failed" (step = why).
# Each run is kept: agent\runs\<time>.jsonl (the backend's whole stream) and a line in
# agent\history.jsonl, with the command that carries on with it in a terminal.
param([Parameter(ValueFromRemainingArguments)][string[]]$Words)

$ErrorActionPreference = 'Stop'
$request = ($Words -join ' ').Trim()
if (-not $request) { Write-Host 'omawin agent ask <request>, e.g. omawin agent ask add Hardcore History to podcasts'; exit 1 }
. (Join-Path $PSScriptRoot 'common.ps1')

$config = Get-AgentConfig
$backend = $config.backend
$settings = $config.backends.$backend
$started = Get-Date
$runs = Join-Path $AgentDir 'runs'
New-Item -ItemType Directory -Force $runs | Out-Null

$script:last = $null
function Set-State($state, $step) {
    $script:last = @{ state = $state; step = $step }
    Set-AgentState $state $step @{ request = $request; backend = $backend }
    Write-Host "  $state  $step"
}
$onStep = { param($tool, $in) Set-State 'working' (Get-AgentStep $tool $in) }

Set-State 'working' 'Thinking'
$utf8 = [Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = $utf8
$OutputEncoding = $utf8
$log = Join-Path $runs ("{0:yyyy-MM-dd_HHmmss}-{1}.jsonl" -f $started, $backend)
$result = $null
Push-Location $AgentRepo
try {
    . (Get-AgentBackendFile $config)
    $result = Invoke-AgentBackend -Settings $settings -Request $request -System (Get-AgentSystem $config 'Background') `
        -Allow $config.allow -Live $config.live -Repo $AgentRepo -Log $log -OnStep $onStep
} catch {
    Set-State 'failed' $_.Exception.Message
} finally { Pop-Location }

if ($script:last.state -ne 'failed') {
    $answer = if ($result.answer) { ("$($result.answer)".Trim() -split "`n")[-1].Trim() } else { '' }
    if ($result.ok -and $answer) { Set-State 'done' $answer }
    elseif ($answer) { Set-State 'failed' $answer }
    else { Set-State 'failed' "$backend gave no answer" }
}

$entry = @{
    time = $started.ToString('s'); request = $request; backend = $backend; model = $settings.model
    state = $script:last.state; answer = $script:last.step; resume = $result.resume
    seconds = [int]((Get-Date) - $started).TotalSeconds; cost = $result.cost
} | ConvertTo-Json -Compress
Add-Content -LiteralPath (Join-Path $AgentDir 'history.jsonl') -Value $entry -Encoding utf8
