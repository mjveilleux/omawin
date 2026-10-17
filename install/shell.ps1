# install.ps1 step: PowerShell profile, Windows Terminal, noctty, starship, git, Git Bash.
# Dot-sourced by install.ps1, so its functions (install/lib.ps1) and $Repo are in scope.

Write-Step 'PowerShell profile'
# Documents may be redirected to OneDrive; ask Windows where it really is. Documents\PowerShell is
# a folder of its own (pwsh writes there: powershell.config.json, Install-Module's Modules), with
# two stubs that load omawin's profile from the repo. Your own lines go in your user layer
# (docs/work.md), not in the stubs: they're rewritten here.
$docs = [Environment]::GetFolderPath('MyDocuments')
$psDir = "$docs\PowerShell"
$psItem = Get-Item -LiteralPath $psDir -Force -ErrorAction SilentlyContinue
if ($psItem -and $psItem.LinkType -and (@($psItem.Target)[0].TrimEnd('\') -eq "$Repo\powershell")) {
    # An older install linked the folder into the repo; the migration (writable-files-out) makes it a folder of its own.
    Write-Note "$psDir still links into the repo; the migrations below move it out"
} else {
    if ($psItem -and $psItem.LinkType) { [IO.Directory]::Delete($psDir); Write-Note "removed stale link $psDir" }  # the link only
    New-Item -ItemType Directory -Force -Path $psDir | Out-Null
    foreach ($name in 'profile.ps1', 'Microsoft.PowerShell_profile.ps1') {
        $profilePath = ("$Repo\powershell\$name") -replace "'", "''"
        Set-FileContent "$psDir\$name" "# Managed by omawin: loads $Repo\powershell\$name. Your own lines go in your user layer's powershell\ (omawin docs/work.md).`nif (Test-Path '$profilePath') { . '$profilePath' }`n"
    }
    # The execution policy (RemoteSigned), seeded once; pwsh rewrites it (Set-ExecutionPolicy).
    if (-not (Test-Path "$psDir\powershell.config.json")) { Copy-Item "$Repo\powershell\powershell.config.json" $psDir; Write-Note "seeded $psDir\powershell.config.json" }
}
$cache = "$HOME\.cache\pwsh"
if (Test-Path $cache) { Remove-Item $cache -Recurse -Force; Write-Note 'cleared starship/zoxide init cache (rebuilt on next start)' }

Write-Step 'Windows Terminal'
# Terminal writes its own settings.json (its settings UI), so it lives in Terminal's LocalState, not
# in the repo: omawin's windows-terminal/settings.json seeds it once (Terminal's generated one is
# backed up), and after that it's yours. The colors come from a fragment (themes/render.ps1).
# Seeded again only if the file is gone; %LOCALAPPDATA%\launcher\seeded-windows-terminal says it was.
function Set-TerminalSettings([string]$LocalState) {
    $item = Get-Item -LiteralPath $LocalState -Force -ErrorAction SilentlyContinue
    if ($item -and $item.LinkType) {
        # An older install linked LocalState into the repo; the migration (writable-files-out) moves it out.
        Write-Note "$LocalState still links into the repo; the migrations below move it out"
        return
    }
    $settings = Join-Path $LocalState 'settings.json'
    $marker = "$env:LOCALAPPDATA\launcher\seeded-windows-terminal"
    if ((Test-Path $settings) -and (Test-Path $marker)) { Write-Note "ok  $settings (yours)"; return }
    New-Item -ItemType Directory -Force -Path $LocalState, (Split-Path $marker) | Out-Null
    Move-Aside $settings
    Copy-Item "$Repo\windows-terminal\settings.json" $settings
    [IO.File]::WriteAllText($marker, "$settings`n")
    Write-Note "seeded $settings from windows-terminal/settings.json"
}
$wtPackage = Get-ChildItem "$env:LOCALAPPDATA\Packages" -Filter 'Microsoft.WindowsTerminal_*' -Directory -ErrorAction SilentlyContinue |
    Select-Object -First 1
if ($wtPackage) {
    try { Set-TerminalSettings "$($wtPackage.FullName)\LocalState" }
    catch { Write-Warn "couldn't seed Windows Terminal's settings (is it open?). Close it and run: .\install.ps1 -LinkOnly from another shell. $_" }
} elseif (Get-Command wt -ErrorAction SilentlyContinue) {
    # Installed but never opened: its settings folder doesn't exist yet. Open it once
    # (minimized) so Windows creates it, close it, and seed.
    Start-Process wt -WindowStyle Minimized
    for ($i = 0; $i -lt 30 -and -not ($wtPackage = Get-ChildItem "$env:LOCALAPPDATA\Packages" -Filter 'Microsoft.WindowsTerminal_*' -Directory -ErrorAction SilentlyContinue | Select-Object -First 1); $i++) { Start-Sleep 1 }
    Start-Sleep 3
    Get-Process WindowsTerminal -ErrorAction SilentlyContinue | Where-Object SessionId -eq (Get-Process -Id $PID).SessionId | Stop-Process -Force
    Start-Sleep 1
    if ($wtPackage) {
        try { Set-TerminalSettings "$($wtPackage.FullName)\LocalState" }
        catch { Write-Warn "couldn't seed Windows Terminal's settings: $_. Run .\install.ps1 -LinkOnly from another shell." }
    } else { Write-Warn 'Windows Terminal never created its settings folder. Open it once, close it, then run .\install.ps1 -LinkOnly' }
} else {
    Write-Warn 'Windows Terminal not installed. Install it, then run .\install.ps1 -LinkOnly'
}

Write-Step 'noctty'
# The default terminal (omawin default terminal). Its config is in this repo; noctty reads a stub
# that includes it, then the current theme's colors (rendered by themes/render.ps1).
Set-FileContent "$env:LOCALAPPDATA\noctty\config.ghostty" "# omawin: the config is in the repo; the colors are the current theme's (themes/render.ps1).`nconfig-file = $Repo\noctty\config.ghostty`nconfig-file = ?$env:LOCALAPPDATA\launcher\theme.ghostty`n"

Write-Step 'starship'
$starship = "$Repo\starship\starship.toml"
if ([Environment]::GetEnvironmentVariable('STARSHIP_CONFIG', 'User') -ne $starship) {
    [Environment]::SetEnvironmentVariable('STARSHIP_CONFIG', $starship, 'User')
    Write-Note "set STARSHIP_CONFIG=$starship"
} else { Write-Note 'ok  STARSHIP_CONFIG' }
$env:STARSHIP_CONFIG = $starship
# A leftover ~/.config/starship.toml would just be confusing: it's no longer read.
Move-Aside "$HOME\.config\starship.toml"

Write-Step 'fastfetch'
Set-Junction "$HOME\.config\fastfetch" "$Repo\fastfetch"

Write-Step 'herdr'
# herdr (project windows are herdr sessions) reads its config from this repo: HERDR_CONFIG_PATH.
# %APPDATA%\herdr also holds its sessions and logs, so it isn't linked; its own config.toml there
# is no longer read.
$herdr = "$Repo\herdr\config.toml"
if ([Environment]::GetEnvironmentVariable('HERDR_CONFIG_PATH', 'User') -ne $herdr) {
    [Environment]::SetEnvironmentVariable('HERDR_CONFIG_PATH', $herdr, 'User')
    Write-Note "set HERDR_CONFIG_PATH=$herdr"
} else { Write-Note 'ok  HERDR_CONFIG_PATH' }
$env:HERDR_CONFIG_PATH = $herdr

Write-Step 'git'
$gitRepoConfig = "$Repo\git\gitconfig" -replace '\\', '/'
$gitIgnore = "$Repo\git\ignore" -replace '\\', '/'
if (Get-Command git -ErrorAction SilentlyContinue) {
    $includes = @(git config --global --get-all include.path)
    if ($includes -notcontains $gitRepoConfig) {
        git config --global --add include.path $gitRepoConfig
        Write-Note "~/.gitconfig now includes $gitRepoConfig"
    } else { Write-Note 'ok  include.path' }
    if ((git config --global --get core.excludesFile) -ne $gitIgnore) {
        git config --global core.excludesFile $gitIgnore
        Write-Note "core.excludesFile = $gitIgnore"
    } else { Write-Note 'ok  core.excludesFile' }
    # Your user layer's git settings (your identity, say), included after omawin's, if it has any.
    $gitUserConfig = "$UserLayer\git\gitconfig" -replace '\\', '/'
    if ((Test-Path $gitUserConfig) -and ($includes -notcontains $gitUserConfig)) {
        git config --global --add include.path $gitUserConfig
        Write-Note "~/.gitconfig now includes $gitUserConfig"
    }
    # Your name and email: never in the repo. Asked once, into your own ~/.gitconfig.
    # (--includes: an identity from an included file counts.)
    if (git config --global --includes --get user.name) { Write-Note "ok  user.name $(git config --global --includes --get user.name)" }
    elseif (Test-Interactive) {
        $name = (Read-Host '    git: your name (for commits; Enter to skip)').Trim()
        $email = if ($name) { (Read-Host '    git: your email').Trim() }
        if ($name) { git config --global user.name $name; Write-Note "user.name = $name" }
        if ($email) { git config --global user.email $email; Write-Note "user.email = $email" }
    } else { Write-Warn 'no git identity: git config --global user.name "Your Name"; git config --global user.email you@example.com' }
} else { Write-Warn 'git not found; re-run after installing it' }

Write-Step 'Git Bash'
$bashrc = "$Repo\bash\bashrc" -replace '\\', '/'
Set-FileContent "$HOME\.bashrc" "# Managed by omawin: edit $bashrc instead.`nsource '$bashrc'`n"
# Login shells (Git Bash opens one) load ~/.bashrc too: bash/bash_profile, copied.
Set-FileContent "$HOME\.bash_profile" ([IO.File]::ReadAllText("$Repo\bash\bash_profile"))
