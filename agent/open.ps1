# omawin agent open: the agent's herdr session (OpenCode's service, the requests), on workspace 9,
# started first if it isn't running.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
Start-AgentSession (Get-AgentConfig) | Out-Null
Open-AgentWindow | Out-Null
glazewm command focus --workspace 9 | Out-Null
