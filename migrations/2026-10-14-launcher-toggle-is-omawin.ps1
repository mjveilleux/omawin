# launcher-toggle.exe became omawin.exe (bin/omawin.cs): the keys, the menu and the dock call
# `omawin <command>` now. Remove the old exe, and the terminal launcher's fzf port file (that
# launcher was retired on 2026-10-13).
Remove-Item "$HOME\.local\bin\launcher-toggle.exe" -Force -ErrorAction SilentlyContinue
Remove-Item "$env:LOCALAPPDATA\launcher\fzf-listen" -Force -ErrorAction SilentlyContinue
