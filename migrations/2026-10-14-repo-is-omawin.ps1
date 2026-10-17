# The repo was renamed windows-dotfiles -> omawin (on GitHub, and its folder ~/omawin).
# install.ps1 relinks everything to the new folder; this drops what still points at the old
# one: ~/.gitconfig's include of the old git/gitconfig.
if (Get-Command git -ErrorAction SilentlyContinue) {
    $old = @(git config --global --get-all include.path 2>$null) | Where-Object { $_ -match '/windows-dotfiles/git/gitconfig$' }
    foreach ($path in $old) { git config --global --unset-all include.path ([regex]::Escape($path)) }
}
if (Test-Path "$HOME\windows-dotfiles") {
    Write-Host "  ~/windows-dotfiles is still there; the setup now lives in ~/omawin. Delete the old folder once you've checked it has nothing you need."
}
