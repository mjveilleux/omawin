# The agent on OpenCode (v2): its always-running background service does the work, and
# `opencode run` is only a client of it, so a request starts in ~1 s, not a server's start-up.
#
# The service reads its agents from the project, so two agents are written to
# .opencode/opencode.json in this repo (git-ignored) whenever they change: "omawin" (the
# background: config.json's allow list, everything else denied) and "omawin-live" (Ctrl+Win+H's
# herdr session: the same, plus editing files when config.json's live.edit says so). The model is
# passed with -m on every run: OpenCode ignores an agent's own model. Its shell tool is "shell",
# and each part of a chained command is checked on its own, so the prompt asks for one plain
# command at a time. `opencode run` can't ask the user anything (it rejects what isn't allowed),
# which is why the live agent's edits are allowed outright.

function Write-OpenCodeAgents($Allow, $Live, [string]$Repo, [hashtable]$Systems) {
    function Rules([bool]$edit) {
        # Later rules win, so everything is denied first.
        $rules = [Collections.Generic.List[object]]::new()
        $rules.Add([ordered]@{ action = '*'; resource = '*'; effect = 'deny' })
        foreach ($c in $Allow.commands) {
            $rules.Add([ordered]@{ action = 'shell'; resource = $c; effect = 'allow' })
            $rules.Add([ordered]@{ action = 'shell'; resource = "$c *"; effect = 'allow' })
        }
        $actions = @()
        if ($Allow.read) { $actions += 'read', 'glob', 'grep', 'list', 'external_directory' }
        if ($Allow.web) { $actions += 'webfetch', 'websearch' }
        if ($edit) { $actions += 'edit', 'todowrite' }
        foreach ($a in $actions) { $rules.Add([ordered]@{ action = $a; resource = '*'; effect = 'allow' }) }
        , $rules
    }
    # Each run writes its own agent and keeps the other one as it was.
    $file = Join-Path $Repo '.opencode\opencode.json'
    $old = if (Test-Path $file) { [IO.File]::ReadAllText($file) } else { '' }
    $agents = [ordered]@{}
    try { foreach ($p in ($old | ConvertFrom-Json).agents.PSObject.Properties) { $agents[$p.Name] = $p.Value } } catch { }
    foreach ($name in $Systems.Keys) {
        $agents[$name] = [ordered]@{
            description = "omawin's own agent ($name); written by agent/backends/opencode.ps1"
            mode        = 'primary'
            system      = $Systems[$name]
            permissions = Rules ($name -eq 'omawin-live' -and $Live.edit)
        }
    }
    $config = [ordered]@{ '$schema' = 'https://opencode.ai/config.json'; agents = $agents } | ConvertTo-Json -Depth 8
    if ($old -ne $config) {
        New-Item -ItemType Directory -Force (Split-Path $file) | Out-Null
        [IO.File]::WriteAllText($file, $config)
    }
}

function Get-OpenCode {
    $opencode = (Get-Command opencode -ErrorAction SilentlyContinue).Source
    if ($opencode) { $opencode } else { Join-Path $HOME '.opencode\bin\opencode.exe' }
}

function Get-OpenCodeTitle([string]$Request) { 'omawin: ' + $Request.Substring(0, [Math]::Min(60, $Request.Length)) }

# In the background: the answer in one line, the steps as they happen.
function Invoke-AgentBackend {
    param($Settings, [string]$Request, [string]$System, $Allow, $Live, [string]$Repo, [string]$Log, [scriptblock]$OnStep)
    Write-OpenCodeAgents $Allow $Live $Repo @{ omawin = $System }

    $result = @{ ok = $false; answer = ''; session = $null; cost = 0.0 }
    & (Get-OpenCode) run --agent omawin -m $Settings.model --format json --title (Get-OpenCodeTitle $Request) $Request 2>&1 | ForEach-Object {
        $line = "$_"
        Add-Content -LiteralPath $Log -Value $line -Encoding utf8
        try { $msg = $line | ConvertFrom-Json } catch { return }
        if ($msg.sessionID) { $result.session = $msg.sessionID }
        $part = $msg.part
        switch ($msg.type) {
            'tool_use'    { & $OnStep $part.tool $part.state.input }
            'text'        { if ("$($part.text)".Trim()) { $result.answer = "$($part.text)".Trim(); $result.ok = $true } }
            'step_finish' { if ($part.cost) { $result.cost += [double]$part.cost } }
            'error'       { $result.ok = $false; $result.answer = "$($msg.error.message)" }
        }
    }
    if ($result.session) { $result.resume = "opencode -s $($result.session)" }
    $result
}

# Live, in this terminal (herdr's pane): the run as OpenCode prints it, then OpenCode's own
# interface on the same session, for anything to add.
function Invoke-AgentLive {
    param($Settings, [string]$Request, [string]$System, $Allow, $Live, [string]$Repo)
    Write-OpenCodeAgents $Allow $Live $Repo @{ 'omawin-live' = $System }
    $opencode = Get-OpenCode
    $session = $null
    & $opencode run --agent omawin-live -m $Settings.model --format json --title (Get-OpenCodeTitle $Request) $Request 2>&1 | ForEach-Object {
        $line = "$_"
        try { $msg = $line | ConvertFrom-Json } catch { Write-Host $line; return }
        if ($msg.sessionID) { $session = $msg.sessionID }
        $part = $msg.part
        switch ($msg.type) {
            'tool_use' {
                $what = Get-AgentStep $part.tool $part.state.input
                $mark = if ($part.state.status -eq 'error') { '✗' } else { '•' }
                Write-Host "  $mark $what" -ForegroundColor DarkGray
            }
            'text'  { if ("$($part.text)".Trim()) { Write-Host ''; Write-Host "$($part.text)".Trim(); Write-Host '' } }
            'error' { Write-Host "$($msg.error.message)" -ForegroundColor Red }
        }
    }
    # OpenCode's interface is a client of the one service, ~100 MB while it's open, so it opens
    # only when asked: a finished request leaves nothing running (live.ps1 then closes the tab).
    if (-not $session) { return }
    Write-Host "  Enter: keep going in OpenCode · any other key: done  (later, anywhere: opencode -s $session)" -ForegroundColor DarkGray
    if ([Console]::ReadKey($true).Key -eq 'Enter') { & $opencode -s $session }
}
