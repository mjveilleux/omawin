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
Get-Process zebar, voice-meter, cpu-cores, theme-watch -ErrorAction SilentlyContinue | Where-Object SessionId -eq (Get-Process -Id $PID).SessionId | Stop-Process -Force
Get-CimInstance Win32_Process -Filter "Name = 'msedgewebview2.exe'" | Where-Object CommandLine -match 'zebar' |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
$env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = '--disable-gpu'
Start-Process zebar -ArgumentList 'startup' -WindowStyle Hidden
