# The repo's folders moved, after Omarchy's layout (plans/repo-layout.md): the app configs to
# config\, omawin's data (the launcher's files, the bar's layout) to default\, the menu app to
# shell\, the command, music player, agent and bundle builder to src\, the tests to test\.
# Safe on a new PC and twice.
#
# What the install's relink already redoes (it runs before the migrations): the links into the repo
# (Neovim, fastfetch, Neovide), STARSHIP_CONFIG and HERDR_CONFIG_PATH, the PowerShell profile's,
# Git Bash's and noctty's stubs, git's include of omawin's settings, and WSL's ~/.config/nvim.
# What's left is here:
#
#   - git's include of the old config\git\gitconfig path is dropped (the new one is added by the
#     relink; git ignores a missing include, but a dead line in your ~/.gitconfig is confusing).
#   - ~/omawin\launcher and ~/omawin\bar become links to their new places, for a menu app built
#     before the move (one taken from an older release, on a PC with no .NET SDK to rebuild it):
#     it reads its menus and the bar's layout there. Ignored by git (.gitignore). Once every PC
#     runs a menu app from after the move, a later migration removes them.
$repo = Split-Path $PSScriptRoot

if (Get-Command git -ErrorAction SilentlyContinue) {
    $old = (Join-Path $repo 'git\gitconfig') -replace '\\', '/'
    $includes = @(& { $ErrorActionPreference = 'Continue'; git config --global --get-all include.path 2>$null })
    if ($includes -contains $old) {
        & { $ErrorActionPreference = 'Continue'; git config --global --unset-all include.path "^$([regex]::Escape($old))$" 2>$null }
        "~/.gitconfig: dropped the include of $old (moved to config/git/gitconfig)"
    }
    $oldIgnore = (Join-Path $repo 'git\ignore') -replace '\\', '/'
    if ((& { $ErrorActionPreference = 'Continue'; git config --global --get core.excludesFile 2>$null }) -eq $oldIgnore) {
        git config --global core.excludesFile ((Join-Path $repo 'config\git\ignore') -replace '\\', '/')
        "~/.gitconfig: core.excludesFile now config/git/ignore"
    }
}

foreach ($name in 'launcher', 'bar') {
    $link = Join-Path $repo $name
    $target = Join-Path $repo "default\$name"
    if (-not (Test-Path $target)) { continue }
    $item = Get-Item -LiteralPath $link -Force -ErrorAction SilentlyContinue
    if ($item) {
        # Already our link, or a folder left behind with files of yours in it: left as it is.
        if ($item.LinkType -and @($item.Target)[0].TrimEnd('\') -ne $target) { "! $link links elsewhere ($(@($item.Target)[0])); left alone" }
        continue
    }
    New-Item -ItemType Junction -Path $link -Target $target | Out-Null
    "linked $link -> default\$name (for a menu app from before the move)"
}
