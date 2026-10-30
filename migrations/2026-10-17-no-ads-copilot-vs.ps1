# no-ads.ps1 now also turns off the Copilot app in the background (and its taskbar button) and
# Visual Studio's background update downloads. Installs before this ran no-ads without them:
# your part runs now. Visual Studio's switch needs admin, which a migration doesn't ask for:
# `omawin no-ads` does it. Safe on a new PC (each part only acts on what's there) and twice.
& (Join-Path (Split-Path $PSScriptRoot) 'bin\no-ads.ps1') -UserOnly
