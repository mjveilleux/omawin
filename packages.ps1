# Everything install.ps1 installs: a lean core, plus the presets you pick (opt-in groups).
# `omawin preset list|add <name>|remove <name>` picks them (bin/preset.ps1); the picks are kept in
# %LOCALAPPDATA%\launcher\presets.txt, and install.ps1 installs the core and those.
# Edit these lists to add or drop tools. No Chocolatey: everything is winget or its own installer.

# The core, winget (no admin needed for most). Id = winget package id.
$WingetPackages = @(
    # shell and terminal
    'Microsoft.PowerShell'          # pwsh 7
    'Microsoft.WindowsTerminal'     # the fallback (omawin default terminal wt)
    'Git.Git'                       # also gives Git Bash
    'GitHub.cli'
    'Starship.Starship'             # prompt
    'ajeetdsouza.zoxide'            # z / zi
    'junegunn.fzf'                  # Ctrl+R / Ctrl+T
    'sharkdp.fd'
    'BurntSushi.ripgrep.MSVC'
    'eza-community.eza'             # ls / ll / lt
    'sharkdp.bat'
    'jqlang.jq'
    'JesseDuffield.lazygit'         # lg
    'Fastfetch-cli.Fastfetch'       # system info with omawin's logo (fastfetch/)

    # desktop
    'glzr-io.glazewm'               # tiling window manager
    'glzr-io.zebar'                 # the top bar and the dock
    'Oven-sh.Bun'                   # omawin's JavaScript runtime

    # editor
    'Neovim.Neovim'
    'Neovide.Neovide'
    'tree-sitter.tree-sitter-cli'   # nvim-treesitter builds parsers with it
    'zig.zig'                       # C compiler for nvim-treesitter parsers
    'OpenJS.NodeJS.LTS'             # mason.nvim (Neovim) installs most LSPs with npm
    'astral-sh.uv'                  # Python tools (harlequin, the Fabric CLI) and mason.nvim's Python LSPs

    # The menu app and the music player run on it (they come prebuilt from the release). The
    # 1 GB SDK that builds them is installed only where one has to be built: no release for this
    # PC, or a checkout being worked on (bin/install-binaries.ps1, Install-DotNetSdk).
    'Microsoft.DotNet.DesktopRuntime.9'
)

# Opt-in groups. Winget: winget ids. Extras: names in $ExtraInstalls. Tools: the commands
# `omawin check` expects once the preset is in.
$Presets = [ordered]@{
    'python'         = @{ Summary = 'Python 3.13'; Winget = @('Python.Python.3.13'); Tools = @('python') }
    'node'           = @{ Summary = 'pnpm (Node.js itself is in the core)'; Winget = @('pnpm.pnpm'); Tools = @('pnpm') }
    'go'             = @{ Summary = 'Go'; Winget = @('GoLang.Go'); Tools = @('go') }
    'elixir'         = @{ Summary = "Erlang and Elixir (Neovim's IEx helpers)"; Winget = @('Erlang.ErlangOTP', 'Elixir.Elixir'); Tools = @('elixir') }
    'r'              = @{ Summary = 'R and RStudio'; Winget = @('RProject.R', 'Posit.RStudio') }
    'c'              = @{ Summary = 'gcc and mingw32-make (WinLibs)'; Winget = @('BrechtSanders.WinLibs.POSIX.UCRT'); Tools = @('gcc') }
    'data'           = @{ Summary = 'DuckDB, harlequin (SQL in the terminal), sqlcmd'; Winget = @('DuckDB.cli', 'Microsoft.Sqlcmd'); Extras = @('harlequin'); Tools = @('duckdb', 'harlequin') }
    'typst'          = @{ Summary = 'Typst (documents)'; Winget = @('Typst.Typst'); Tools = @('typst') }
    'docker'         = @{ Summary = 'lazydocker (Docker Desktop: Install > Development)'; Winget = @('JesseDuffield.Lazydocker'); Tools = @('lazydocker') }
    'cli-extras'     = @{ Summary = 'make, cloc, wget'; Winget = @('ezwinports.make', 'AlDanial.Cloc', 'JernejSimoncic.Wget'); Tools = @('make', 'cloc', 'wget') }
    'ai-agents'      = @{ Summary = 'Claude Code, opencode'; Extras = @('claude', 'opencode'); Tools = @('claude') }
    'music'          = @{ Summary = "yt-dlp and mpv, for the bar's music player (music/)"; Winget = @('yt-dlp.yt-dlp', 'shinchiro.mpv'); Tools = @('yt-dlp', 'mpv') }
    'dictation'      = @{ Summary = 'Handy: local dictation on Win+H'; Winget = @('cjpais.Handy'); Tools = @('handy') }
    'wsl'            = @{ Summary = "Linux in WSL, Arch (Omarchy's packages) or Ubuntu, with omawin's Neovim, herdr and your agents (wsl/setup.sh)"; Extras = @('linux') }
    'powerbi-fabric' = @{ Summary = 'Power BI Desktop, DAX Studio, Azure CLI, Fabric CLI'; Winget = @('Microsoft.PowerBI', 'DaxStudio.DaxStudio', 'Microsoft.AzureCLI'); Extras = @('fab') }
}

# Packages that install for the whole PC (into Program Files, with Windows' admin prompt each).
# install.ps1 hands them to its one admin step (install/admin.ps1) instead, which installs them
# all after a single prompt; the rest install for this account alone. A package missing here may
# raise a prompt of its own; one listed that has no whole-PC installer falls back to this
# account's. Declined, the listed ones install for this account where they can (--scope user).
$WholePC = @(
    'Microsoft.PowerShell', 'Git.Git', 'GitHub.cli', 'glzr-io.glazewm', 'glzr-io.zebar', 'Neovim.Neovim',
    'Neovide.Neovide', 'OpenJS.NodeJS.LTS', 'Microsoft.DotNet.DesktopRuntime.9',
    'GoLang.Go', 'Erlang.ErlangOTP', 'Elixir.Elixir', 'RProject.R', 'Posit.RStudio', 'Microsoft.Sqlcmd',
    'Microsoft.PowerBI', 'DaxStudio.DaxStudio', 'Microsoft.AzureCLI'
)

# Installer arguments for packages whose defaults don't fit (passed as winget --override).
$WingetOverrides = @{
    # Python's installer leaves PATH alone by default, so `python` would hit the Store stub.
    'Python.Python.3.13' = '/quiet InstallAllUsers=0 PrependPath=1 Include_launcher=1'
}

# A package counts as installed when its command is on the PATH, however it got there (an
# older install, another package manager), so it isn't installed a second time.
$WingetCommands = @{
    'GoLang.Go' = 'go'; 'pnpm.pnpm' = 'pnpm'; 'zig.zig' = 'zig'; 'astral-sh.uv' = 'uv'; 'DuckDB.cli' = 'duckdb'
    'Typst.Typst' = 'typst'; 'Erlang.ErlangOTP' = 'erl'; 'Elixir.Elixir' = 'elixir'; 'BrechtSanders.WinLibs.POSIX.UCRT' = 'gcc'
    'JesseDuffield.Lazydocker' = 'lazydocker'; 'ezwinports.make' = 'make'; 'AlDanial.Cloc' = 'cloc'; 'JernejSimoncic.Wget' = 'wget'
    'jqlang.jq' = 'jq'; 'junegunn.fzf' = 'fzf'; 'sharkdp.fd' = 'fd'; 'BurntSushi.ripgrep.MSVC' = 'rg'; 'JesseDuffield.lazygit' = 'lazygit'
    'Git.Git' = 'git'; 'Microsoft.PowerShell' = 'pwsh'; 'GitHub.cli' = 'gh'; 'OpenJS.NodeJS.LTS' = 'node'; 'Neovim.Neovim' = 'nvim'
}

# A package with no command on the PATH counts as installed when its file is there (a new install
# doesn't read winget's whole list: install/software.ps1, Get-WingetInstalled).
$WingetFiles = @{
    'glzr-io.zebar'                     = "$env:ProgramFiles\glzr.io\Zebar\zebar.exe"
    'glzr-io.glazewm'                   = "$env:ProgramFiles\glzr.io\GlazeWM\glazewm.exe"
    'Microsoft.DotNet.DesktopRuntime.9' = "$env:ProgramFiles\dotnet\shared\Microsoft.WindowsDesktop.App\9.*"
}

# Tools with their own installers (run after the winget ones, so uv exists). herdr is the core's
# (the launcher's project windows use it); the rest belong to presets.
$ExtraInstalls = [ordered]@{
    # herdr (agent workspaces; the launcher's project windows use it). Official installer:
    # verifies the download's SHA-256 and installs under ~/.herdr, on the user PATH.
    'herdr'     = { Invoke-RestMethod https://herdr.dev/install.ps1 | Invoke-Expression }
    'claude'    = { Invoke-RestMethod https://claude.ai/install.ps1 | Invoke-Expression }
    # opencode's official installer is a bash script; Git Bash runs it (lands in ~/.opencode/bin).
    'opencode'  = { & "$env:ProgramFiles\Git\bin\bash.exe" -lc 'curl -fsSL https://opencode.ai/install | bash' }
    'harlequin' = { uv tool install 'harlequin[odbc]' }
    'fab'       = { uv tool install ms-fabric-cli }
    # This account's Linux in WSL (omawin default linux), set up by wsl/setup.sh: the wsl preset.
    # WSL itself comes from the one admin prompt; just installed, it works after a restart, and
    # the setup then finishes at the next sign-in (Get-WslRestart: install/admin.ps1, bin/preset.ps1).
    'linux'     = { & "$Repo\wsl\install.ps1" -RestartPending:$(if (Get-Command Get-WslRestart -ErrorAction SilentlyContinue) { Get-WslRestart } else { $false }) }
}
$CoreExtras = @('herdr')

# Nerd Font used by Windows Terminal ("JetBrainsMono NF") and the eza/starship glyphs.
$NerdFont = 'JetBrainsMono'
