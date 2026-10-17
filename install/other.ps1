# install.ps1 step: WSL's config, Claude Code's rules, skills, settings and plugins.
# Dot-sourced by install.ps1, so its functions (install/lib.ps1), $Repo and $UserLayer are in scope.
# Each file comes from your user layer when it has its own copy (docs/work.md), else from omawin.

Write-Step 'WSL'
# A file, not a folder, so it's copied rather than linked; re-run -LinkOnly after editing.
$wslConfig = "$HOME\.wslconfig"
$wslSource = if (Test-Path "$UserLayer\wsl\.wslconfig") { "$UserLayer\wsl\.wslconfig" } else { "$Repo\wsl\.wslconfig" }
$source = Get-Content $wslSource -Raw
if ((Test-Path $wslConfig) -and (Get-Content $wslConfig -Raw) -eq $source) { Write-Note 'ok  ~/.wslconfig' }
else {
    Move-Aside $wslConfig
    Copy-Item $wslSource $wslConfig
    Write-Note "wrote ~/.wslconfig from $wslSource (applies next time WSL starts)"
}

Write-Step 'Claude Code'
# Your rules, skills, settings and plugins, from the shared layer and this account's
# (bin/claude-sync.ps1; Claude Code rewrites settings.json itself, so it's merged, not linked).
try { & "$Repo\bin\claude-sync.ps1" | ForEach-Object { Write-Note $_ } } catch { Write-Warn "claude sync: $_" }
