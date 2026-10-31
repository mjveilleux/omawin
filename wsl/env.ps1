# Windows' folders for Linux, for wsl/setup.sh: dot-sourced by wsl/install.ps1 and install/other.ps1
# before they run it. Passed through WSLENV with /p, so WSL hands them over as Linux paths
# (C:\Users\you -> /mnt/c/Users/you):
#
#   OMAWIN_STATE    %LOCALAPPDATA%\launcher: the theme for Linux (theme-linux.sh), the file index
#   OMAWIN_ACCOUNT  this account's folder (docs/work.md): its wsl/wsl.conf overrides
#   USERPROFILE     the home: omawin.exe for the `omawin` wrapper in Linux
#   LOCALAPPDATA    where npiperelay goes (the SSH agent bridge), VS Code's per-user install
#
# Whatever WSLENV held already is kept. Runs under Windows PowerShell 5.1 too.
$env:OMAWIN_STATE = Join-Path $env:LOCALAPPDATA 'launcher'
$env:OMAWIN_ACCOUNT = & (Join-Path (Split-Path $PSScriptRoot) 'bin\user-layer.ps1')
$omawinWslEnv = 'OMAWIN_STATE/p', 'OMAWIN_ACCOUNT/p', 'USERPROFILE/p', 'LOCALAPPDATA/p'
$env:WSLENV = (@("$env:WSLENV" -split ':' | Where-Object { $_ -and ($omawinWslEnv -notcontains $_) }) + $omawinWslEnv) -join ':'
