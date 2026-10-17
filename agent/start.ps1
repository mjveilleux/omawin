# omawin agent start (GlazeWM runs it at login): the herdr session "omawin-agent", headless, with
# OpenCode's service running in its first tab (server.ps1), so a request said with Ctrl+Win+H only
# has to attach to it. `omawin agent open` shows it; nothing opens here.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
Start-AgentSession (Get-AgentConfig) | Out-Null
