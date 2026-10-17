# Runs in a tab of the herdr session "omawin-agent" (workspace 9): a request said with
# Ctrl+Win+H (listen.ps1 put it in -RequestFile), done live by the backend from agent/config.json,
# in the open, with the backend's own interface after it for anything to add.
param([Parameter(Mandatory)][string]$RequestFile)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$request = (Get-Content -LiteralPath $RequestFile -Raw).Trim()
$config = Get-AgentConfig

Write-Host ''
Write-Host "  omawin · $($config.backend) $($config.backends.$($config.backend).model)" -ForegroundColor DarkGray
Write-Host "  $request" -ForegroundColor Cyan
Write-Host ''
Set-Location $AgentRepo
. (Get-AgentBackendFile $config)
Invoke-AgentLive -Settings $config.backends.$($config.backend) -Request $request -System (Get-AgentSystem $config 'Live') `
    -Allow $config.allow -Live $config.live -Repo $AgentRepo

# Done: the tab goes, so finished requests don't pile up (each held an agent's interface open).
# The conversation stays in the backend's history.
if ($env:HERDR_TAB_ID) { & (Get-Command herdr).Source tab close $env:HERDR_TAB_ID *> $null }
