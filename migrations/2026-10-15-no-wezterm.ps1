# WezTerm is no longer supported: noctty is the terminal (Windows Terminal remains a choice). Remove
# what omawin set up for it: the ~/.config/wezterm junction into the repo (the link only; its
# target is gone from the repo anyway), its state files and spare shells in
# %LOCALAPPDATA%\launcher, the projects list only its sessionizer read, and a default terminal pick
# of wezterm (back to noctty). The WezTerm app itself stays installed.
$link = "$HOME\.config\wezterm"
$item = Get-Item $link -Force -ErrorAction SilentlyContinue
if ($item -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    $target = @($item.Target) | Select-Object -First 1
    if ($target -and $target.TrimEnd('\') -match '\\(omawin|windows-dotfiles)\\wezterm$') {
        # Directory.Delete without recursion removes the junction itself, never what it points to.
        [IO.Directory]::Delete($item.FullName)
        Write-Host "  removed $link"
    }
}

$state = Join-Path $env:LOCALAPPDATA 'launcher'
foreach ($name in 'wezterm-focus', 'wezterm-warm', 'wezterm-request', 'projects.tsv', 'projects.tsv.part') {
    Remove-Item (Join-Path $state $name) -Force -ErrorAction SilentlyContinue
}
Remove-Item (Join-Path $state 'spares') -Recurse -Force -ErrorAction SilentlyContinue

$pick = Join-Path $state 'defaults\terminal'
if ((Test-Path $pick) -and (Get-Content $pick -Raw).Trim() -eq 'wezterm') {
    Set-Content $pick 'noctty' -NoNewline -Encoding ascii
    Write-Host '  default terminal: wezterm -> noctty'
}
