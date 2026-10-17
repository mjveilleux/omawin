# The first tab of the herdr session "omawin-agent" (agent/start.ps1 opens it at login): OpenCode's
# background service, run here, in the open, instead of hidden. Every `opencode run` and
# `opencode -s` finds the service on its own, so the requests said with Ctrl+Win+H (the session's
# other tabs) and `omawin agent ask` are clients of this one: nothing starts a server per request.
#
# `opencode serve --service` returns at once when a service is already up: a client (an open
# OpenCode window) starts one hidden whenever it finds none. So this tab stops that one and takes
# over; if a client keeps winning the restart, it waits for that service to end instead of
# fighting over it.

$opencode = (Get-Command opencode -ErrorAction SilentlyContinue).Source
if (-not $opencode) { $opencode = Join-Path $HOME '.opencode\bin\opencode.exe' }
$Host.UI.RawUI.WindowTitle = 'opencode service'

function Get-OpenCodeService {
    $url = (& $opencode service status 2>$null | Out-String).Trim()
    if ($url -match '^https?://') { $url }
}

$tries = 0
while ($true) {
    $url = Get-OpenCodeService
    if ($url) {
        if ($tries -lt 5) {
            Write-Host "  Taking over the OpenCode service at $url" -ForegroundColor DarkGray
            & $opencode service stop *> $null
            $tries++
        } else {
            Write-Host "  An OpenCode window keeps its own service running at $url; waiting for it to end" -ForegroundColor DarkGray
            while (Get-OpenCodeService) { Start-Sleep -Seconds 10 }
            $tries = 0
        }
    }
    $ran = [Diagnostics.Stopwatch]::StartNew()
    & $opencode serve --service
    if ($ran.Elapsed.TotalSeconds -gt 10) {
        $tries = 0
        Write-Host "  The service stopped; starting it again" -ForegroundColor Yellow
        Start-Sleep -Seconds 2
    }
}
