# install.ps1 step: Neovim, Neovide and R / RStudio.
# Dot-sourced by install.ps1, so its functions (install/lib.ps1) and $Repo are in scope.

Write-Step 'Neovim'
Set-Junction "$env:LOCALAPPDATA\nvim" "$Repo\nvim"
# Neovide uses the same Neovim config; this is only its own settings (config.toml).
Set-Junction "$env:APPDATA\neovide" "$Repo\neovide"

Write-Step 'R / RStudio'
# A one-line ~/.Rprofile (R's ~ is Documents) sourcing r/rprofile.R: RStudio follows the
# desktop theme. An .Rprofile of your own (more than that one line) is left alone.
$rprofile = Join-Path ([Environment]::GetFolderPath('MyDocuments')) '.Rprofile'
$source = "source('$("$Repo\r\rprofile.R" -replace '\\', '/')')"
$current = if (Test-Path $rprofile) { (Get-Content $rprofile -Raw).Trim() }
if ($current -eq $source) { Write-Note "ok  $rprofile" }
elseif (-not $current -or $current -match '^source\(.*rprofile\.R.*\)$') { Set-Content $rprofile $source -Encoding utf8; Write-Note "wrote $rprofile" }
else { Write-Note "$rprofile is yours; add this line to it for themes: $source" }
