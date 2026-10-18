# Starts Zebar (the bar and the dock). Its widgets are web pages in Edge's WebView2; without a GPU
# process they draw in software, which looks the same for flat islands like these and costs
# no more CPU, but uses ~100 MB less (measured: 395 -> 290 MB).
# Measured again 2026-10-15 with music playing: the GPU on costs more of both (CPU 14.7% -> 17.5%
# of a core, +112 MB), so it stays off. The flag only reaches Zebar's
# webviews (it's read from this process's environment), not other apps'.
#
# `omawin restart zebar` runs this: GlazeWM at login and the system menu's Restart > Zebar. A
# theme switch doesn't: the bar and dock follow theme.css by themselves (tokyo-islands/theme.js).
# A Zebar that's already running is stopped first (its webviews too, or the new one can come
# up blank).
# Their colors are theme.css, which themes/render.ps1 writes; made here if it was never written.
$render = Join-Path $PSScriptRoot '..\themes\render.ps1'
if (-not (Test-Path "$PSScriptRoot\tokyo-islands\theme.css") -and (Test-Path $render)) { & $render }
# This account's only: another account signed in on the PC has its own bar.
$session = (Get-Process -Id $PID).SessionId
Get-Process zebar, voice-meter, cpu-cores, theme-watch -ErrorAction SilentlyContinue | Where-Object SessionId -eq $session | Stop-Process -Force
$webviews = @(Get-CimInstance Win32_Process -Filter "Name = 'msedgewebview2.exe'" | Where-Object { $_.CommandLine -match 'zebar' -and $_.SessionId -eq $session })
$webviews | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
# Started before the old webviews are gone, the new bar can come up blank (it shares their
# profile folder): wait for them, up to 5 s.
for ($i = 0; $i -lt 50; $i++) {
    if (-not ($webviews | Where-Object { Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue }) -and
        -not (Get-Process zebar -ErrorAction SilentlyContinue | Where-Object SessionId -eq $session)) { break }
    Start-Sleep -Milliseconds 100
}
$env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = '--disable-gpu'
# Its widgets load from a local server on its port. Right after a restart the old one's
# connections can still hold the port, and the new Zebar then comes up with blank widgets
# (its server never started): it's checked, and Zebar started again if so.
$port = if ($env:ZEBAR_ASSET_PORT) { [int]$env:ZEBAR_ASSET_PORT } else { 6124 }
for ($attempt = 1; $attempt -le 4; $attempt++) {
    $z = Start-Process zebar -ArgumentList 'startup' -WindowStyle Hidden -PassThru
    $serving = $false
    for ($i = 0; $i -lt 40 -and -not $serving; $i++) {
        Start-Sleep -Milliseconds 150
        $serving = [bool](Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue | Where-Object OwningProcess -eq $z.Id)
    }
    if ($serving) { break }
    Stop-Process -Id $z.Id -Force -ErrorAction SilentlyContinue
    Get-CimInstance Win32_Process -Filter "Name = 'msedgewebview2.exe'" | Where-Object { $_.CommandLine -match 'zebar' -and $_.SessionId -eq $session } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 2
}
