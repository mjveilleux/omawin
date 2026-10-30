# What ask.ps1 (in the background), listen.ps1 (Ctrl+Win+H) and live.ps1 (in herdr) share: the
# config, the system prompt, the state the bar reads. Dot-sourced.

$AgentRepo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$AgentDir = Join-Path $env:LOCALAPPDATA 'launcher\agent'
New-Item -ItemType Directory -Force $AgentDir | Out-Null

# src/agent/config.json, with the account folder's agent/config.json on top: backend, allow and live
# replace the repo's; a backend's own settings (its model) merge into the repo's. Between the two,
# the account's agent default (`omawin default agent claude`, the one Neovim's split opens) picks
# the backend, when there's a backend of that name.
function Get-AgentConfig {
    $config = Get-Content (Join-Path $PSScriptRoot 'config.json') -Raw | ConvertFrom-Json
    $pick = Join-Path $env:LOCALAPPDATA 'launcher\defaults\agent'
    if (Test-Path $pick) {
        $name = (Get-Content $pick -Raw).Trim()
        if ($name -and (Test-Path (Join-Path $PSScriptRoot "backends\$name.ps1"))) { $config.backend = $name }
    }
    $mine = Join-Path (& "$AgentRepo\bin\user-layer.ps1") 'agent\config.json'
    if (Test-Path $mine) {
        $over = Get-Content $mine -Raw | ConvertFrom-Json
        foreach ($key in 'backend', 'allow', 'live') { if ($over.$key) { $config.$key = $over.$key } }
        foreach ($b in @($over.backends.PSObject.Properties)) {
            $base = $config.backends.$($b.Name)
            if (-not $base) { $config.backends | Add-Member $b.Name $b.Value; continue }
            foreach ($p in $b.Value.PSObject.Properties) { $base | Add-Member $p.Name $p.Value -Force }
        }
    }
    if (-not $config.backends.$($config.backend)) { $config.backends | Add-Member $config.backend ([pscustomobject]@{}) -Force }
    $config
}

# The backend's script (src/agent/backends/<name>.ps1), dot-sourced by the caller.
function Get-AgentBackendFile($config) {
    $file = Join-Path $PSScriptRoot "backends\$($config.backend).ps1"
    if (-not (Test-Path $file)) { throw "no backend '$($config.backend)' (src/agent/backends/)" }
    $file
}

# src/agent/prompt.md, how this run works (Background: nobody watching; Live: in herdr, watched),
# and what the config lets it do.
function Get-AgentSystem($config, [ValidateSet('Background', 'Live')]$Mode) {
    $allow = $config.allow
    $can = @($allow.commands | ForEach-Object { "``$_``" })
    if ($allow.read) { $can += 'read any file' }
    if ($allow.web) { $can += 'search and read the web' }
    if ($Mode -eq 'Live' -and $config.live.edit) { $can += "edit this repo's files" }

    $how = if ($Mode -eq 'Background') { @'
## This run: in the background

You run with no window and nobody watching: nobody can answer a question, so never ask one. Do the job, or say why you couldn't. Never edit files in this repo from the background: the configs are live. Work on omawin itself goes to a worktree with its own session, or to an issue.

Your last message is shown in the bar, so it's one short line, no markdown: what you did ("Added Hardcore History to podcasts: 40 episodes"), or what stopped you ("Couldn't find a YouTube playlist called Hardcore History").
'@ } else { @'
## This run: live, in herdr on workspace 9

The user asked by voice (Ctrl+Win+H) and can watch you in a herdr session on workspace 9, but is usually busy elsewhere: work on your own, and don't stop to ask.

Quick changes to omawin are your job here: a config, a key, a menu row, a wording, a small fix. Make them in this repo, following AGENTS.md: the configs are live, so a change takes effect at once (or after the restart AGENTS.md's "Doing things" table names: run it). Keep the manual current when behavior changes. Something bigger, or anything you can't test: a worktree with its own session (Jobs below).

End with a short summary: what changed (files) and how to see it. Don't commit.
'@ }

    (Get-Content (Join-Path $PSScriptRoot 'prompt.md') -Raw) + "`n" + $how + @"

## Your tools

You may run $($can -join ', '). Anything else is refused without asking: when the job needs it, say so ("needs you: …").

Run each command on its own, exactly as it's named: no pipes, no redirects, no ``2>&1``, no ``echo``, no chaining with ``;`` or ``&&``. Each part of a command is checked on its own, so anything added gets the whole command refused.
"@
}

# The herdr session "omawin-agent", where the agent works in the open: its first tab runs
# OpenCode's service (server.ps1), and each request said with Ctrl+Win+H gets a tab of its own.
$AgentSession = 'omawin-agent'

function Invoke-AgentHerdr {
    $herdr = (Get-Command herdr -ErrorAction SilentlyContinue).Source
    if (-not $herdr) { throw "herdr isn't installed" }
    # Run from inside a herdr pane (a test from a terminal), herdr's variables would point it at
    # that session, and a window opened from here would refuse to nest.
    Get-ChildItem env: | Where-Object Name -like 'HERDR_*' | ForEach-Object { Remove-Item "env:$($_.Name)" }
    & $herdr --session $AgentSession @args 2>&1 | Out-String
}

# Starts the session headless if it isn't running, with its workspace and, on OpenCode, the
# service's tab. Returns the workspace's id. Safe to run any number of times.
function Start-AgentSession($config) {
    if ((Invoke-AgentHerdr tab list) -notmatch '"result"') {
        $herdr = (Get-Command herdr).Source
        Start-Process $herdr -ArgumentList '--session', $AgentSession, 'server' -WindowStyle Hidden -WorkingDirectory $AgentRepo
        $waited = [Diagnostics.Stopwatch]::StartNew()
        while ((Invoke-AgentHerdr tab list) -notmatch '"result"') {
            if ($waited.ElapsedMilliseconds -gt 8000) { throw "herdr's session $AgentSession didn't start" }
            Start-Sleep -Milliseconds 100
        }
    }
    $workspace = @((Invoke-AgentHerdr workspace list | ConvertFrom-Json).result.workspaces)[0]
    $fresh = $null
    if (-not $workspace) {
        $fresh = (Invoke-AgentHerdr workspace create --cwd $AgentRepo --label $AgentSession | ConvertFrom-Json).result
        $id = $fresh.root_pane.workspace_id
    } else { $id = $workspace.workspace_id }

    if ($config.backend -eq 'opencode') {
        $tabs = (Invoke-AgentHerdr tab list | ConvertFrom-Json).result.tabs
        if (-not ($tabs | Where-Object label -eq 'opencode')) {
            # A new workspace's first tab is free for it; otherwise a tab of its own.
            $tab = if ($fresh) { $fresh } else { (Invoke-AgentHerdr tab create --workspace $id --cwd $AgentRepo --label opencode | ConvertFrom-Json).result }
            Invoke-AgentHerdr tab rename $tab.root_pane.tab_id opencode | Out-Null
            Invoke-AgentHerdr pane run $tab.root_pane.pane_id "pwsh -NoLogo -NoProfile -File `"$PSScriptRoot\server.ps1`"" | Out-Null
            $fresh = $null
        }
    }
    @{ workspace = $id; free = $fresh }
}

# A window on the session, unless one is open (its title is the session's name); a GlazeWM rule
# puts it on workspace 9, so where you are stays as it is.
function Open-AgentWindow {
    $windows = (glazewm query windows | ConvertFrom-Json).data.windows
    if ($windows | Where-Object { $_.title -match [regex]::Escape($AgentSession) }) { return $false }
    Get-ChildItem env: | Where-Object Name -like 'HERDR_*' | ForEach-Object { Remove-Item "env:$($_.Name)" }
    $herdr = (Get-Command herdr).Source
    # Detached, so nothing waits on the terminal's output.
    Start-Process omawin.exe -ArgumentList 'launch', 'terminal', '--title', $AgentSession, '--', "`"$herdr`"", '--session', $AgentSession -WindowStyle Hidden
    $true
}

# %LOCALAPPDATA%\launcher\agent\state: one JSON line for the bar (omawin-listen.exe writes it too).
function Set-AgentState([string]$State, [string]$Step, [hashtable]$More = @{}) {
    $line = @{ state = $State; step = $Step; time = (Get-Date).ToString('s') } + $More | ConvertTo-Json -Compress
    # Written whole then moved into place, so the bar never reads half a line.
    $tmp = Join-Path $AgentDir "state.$PID.tmp"
    [IO.File]::WriteAllText($tmp, $line)
    Move-Item -Force $tmp (Join-Path $AgentDir 'state')
}

# A step, in words, from a tool call: the backends name their tools differently.
function Get-AgentStep($tool, $in) {
    switch ("$tool".ToLowerInvariant()) {
        { $_ -in 'bash', 'shell', 'powershell' } { if ($in.description) { $in.description } else { $in.command } }
        'websearch' { "Searching the web for $($in.query)" }
        'webfetch'  { "Reading $(([uri]$in.url).Host)" }
        'read'      { $f = if ($in.file_path) { $in.file_path } else { $in.filePath }; "Reading $(Split-Path "$f" -Leaf)" }
        { $_ -in 'grep', 'glob', 'list' } { 'Looking through omawin' }
        default     { "$tool" }
    }
}
