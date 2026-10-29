# omawin agent start (GlazeWM runs it at login): the herdr session "omawin-agent", headless, with
# OpenCode's service running in its first tab (server.ps1), so a request said with Ctrl+Win+H only
# has to attach to it. `omawin agent open` shows it; nothing opens here.
# -After waits that many seconds first: at login, so it isn't starting while the desktop comes up
# (a request before then starts the session itself, agent/listen.ps1).
param([int]$After = 0)
$ErrorActionPreference = 'Stop'
if ($After -gt 0) { Start-Sleep -Seconds $After }
. (Join-Path $PSScriptRoot 'common.ps1')
Start-AgentSession (Get-AgentConfig) | Out-Null
