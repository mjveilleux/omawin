# PowerShell profile: same look and tools as the Ubuntu dotfiles
# (starship prompt, zoxide, fzf), plus eza and bat.
#
# Kept fast: starship and zoxide init scripts are generated once into ~/.cache/pwsh
# and dot-sourced from there, so startup launches no extra processes.
# After upgrading starship/zoxide or editing starship.toml, run: Update-ShellCache

# Scripts, agents and CI (redirected output) get a plain shell, nothing interactive.
if ([Console]::IsOutputRedirected -or -not [Environment]::UserInteractive) { return }

# Timing marks while omawin is profiling (omawin profile terminal; see Trace in bin/omawin.cs):
# when this process was created, when the profile started and finished, the first prompt.
$OmawinTrace = if (Test-Path "$env:LOCALAPPDATA\launcher\trace-on") { "$env:LOCALAPPDATA\launcher\trace.log" }
function Write-OmawinTrace([string]$Label, [long]$At = [Diagnostics.Stopwatch]::GetTimestamp()) {
    if ($OmawinTrace) { [IO.File]::AppendAllText($OmawinTrace, "pwsh $Label`t$At`n") }
}
if ($OmawinTrace) {
    $age = [DateTime]::UtcNow - (Get-Process -Id $PID).StartTime.ToUniversalTime()
    Write-OmawinTrace 'process-created' ([Diagnostics.Stopwatch]::GetTimestamp() - [long]($age.TotalSeconds * [Diagnostics.Stopwatch]::Frequency))
    Write-OmawinTrace 'profile-start'
}

[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()  # glyphs from eza/bat/starship
$env:VIRTUAL_ENV_DISABLE_PROMPT = 1  # starship shows the venv instead

# --- cached init scripts --------------------------------------------------------
$ShellCache = Join-Path $HOME '.cache\pwsh'

function Update-ShellCache {
    # Regenerate the starship and zoxide init scripts (run after upgrading either tool).
    New-Item -ItemType Directory -Force -Path $ShellCache | Out-Null
    if (Get-Command starship -ErrorAction SilentlyContinue) {
        # Starship's init launches starship.exe at load time for the continuation prompt;
        # bake that string in once instead.
        $continuation = (starship prompt --continuation) -replace "'", "''"
        $init = (starship init powershell --print-full-init) -join "`n"
        $init = $init -replace '(?s)Set-PSReadLineOption -ContinuationPrompt \(.*?\n    \)', "Set-PSReadLineOption -ContinuationPrompt '$continuation'"
        # Its random session key is a slow Get-Random pipeline (~35 ms); a GUID is instant.
        $init = $init -replace '(?m)^(\s*)\$ENV:STARSHIP_SESSION_KEY = .*$', '$1$ENV:STARSHIP_SESSION_KEY = [guid]::NewGuid().ToString("N").Substring(0, 16)'
        Set-Content -Path "$ShellCache\starship.ps1" -Value $init -Encoding utf8
    }
    if (Get-Command zoxide -ErrorAction SilentlyContinue) {
        zoxide init powershell | Out-String | Set-Content -Path "$ShellCache\zoxide.ps1" -Encoding utf8
    }
    Write-Host "shell cache rebuilt in $ShellCache (open a new tab)"
}
if (-not (Test-Path "$ShellCache\starship.ps1")) { Update-ShellCache | Out-Null }

# --- PSReadLine: fish-style suggestions and history ---------------------------------
# PSReadLine reads the whole history file at every startup and never trims it, so keep
# it bounded: past 400 KB, rewrite it as the newest 5000 distinct commands.
$historyFile = (Get-PSReadLineOption).HistorySavePath
if ((Test-Path $historyFile) -and (Get-Item $historyFile).Length -gt 400KB) {
    # A multi-line command is stored as lines ending in a backtick; keep those together.
    $entries = [Collections.Generic.List[string]]::new(); $current = ''
    foreach ($line in [IO.File]::ReadAllLines($historyFile)) {
        $current = if ($current) { "$current`n$line" } else { $line }
        if (-not $line.EndsWith('`')) { if ($current.Trim()) { $entries.Add($current) }; $current = '' }
    }
    $seen = [Collections.Generic.HashSet[string]]::new()
    $keep = [Collections.Generic.List[string]]::new()
    for ($i = $entries.Count - 1; $i -ge 0 -and $keep.Count -lt 5000; $i--) {
        if ($seen.Add($entries[$i])) { $keep.Add($entries[$i]) }
    }
    $keep.Reverse()
    [IO.File]::WriteAllText($historyFile, ($keep -join "`n") + "`n")
}
Set-PSReadLineOption -EditMode Windows -BellStyle None -HistoryNoDuplicates -MaximumHistoryCount 5000 `
    -PredictionSource History -PredictionViewStyle InlineView  # F2 toggles the list view
# The terminal's own 16 colors (ANSI), not fixed ones: the terminal takes them from the theme,
# so the command line follows a theme switch like everything else.
$e = [char]27
Set-PSReadLineOption -Colors @{
    Command          = "$e[34m"  # blue
    Parameter        = "$e[36m"  # cyan
    String           = "$e[32m"  # green
    Number           = "$e[93m"  # bright yellow
    Operator         = "$e[96m"  # bright cyan
    Variable         = "$e[35m"  # magenta
    Type             = "$e[94m"  # bright blue
    Keyword          = "$e[35m"  # magenta
    Comment          = "$e[90m"  # bright black: the theme's dim text
    InlinePrediction = "$e[90m"
}
Set-PSReadLineKeyHandler -Key UpArrow -Function HistorySearchBackward    # type a prefix, then Up
Set-PSReadLineKeyHandler -Key DownArrow -Function HistorySearchForward
Set-PSReadLineKeyHandler -Key Tab -Function MenuComplete
Set-PSReadLineKeyHandler -Key Ctrl+RightArrow -Function ForwardWord     # accept one word of the suggestion

# --- fzf: Ctrl+R history, Ctrl+T insert a file path --------------------------------
$env:FZF_DEFAULT_COMMAND = 'fd --type f --hidden --strip-cwd-prefix --exclude .git'
# fzf in the terminal's 16 colors too (its "16" base scheme), so it follows the theme.
$env:FZF_DEFAULT_OPTS = '--height 40% --layout reverse --border rounded --info inline-right ' +
'--color=16,border:8,info:8,gutter:-1'

Set-PSReadLineKeyHandler -Chord Ctrl+r -BriefDescription FzfHistory -ScriptBlock {
    $line = $null; $cursor = $null
    [Microsoft.PowerShell.PSConsoleReadLine]::GetBufferState([ref]$line, [ref]$cursor)
    $items = [Microsoft.PowerShell.PSConsoleReadLine]::GetHistoryItems().CommandLine
    [array]::Reverse($items)
    $pick = $items | Where-Object { $_ -notmatch "`n" } | Select-Object -Unique |
        fzf --scheme history --no-sort --query "$line" --prompt 'history> '
    if ($pick) {
        [Microsoft.PowerShell.PSConsoleReadLine]::RevertLine()
        [Microsoft.PowerShell.PSConsoleReadLine]::Insert($pick)
    }
    [Microsoft.PowerShell.PSConsoleReadLine]::InvokePrompt()
}
Set-PSReadLineKeyHandler -Chord Ctrl+t -BriefDescription FzfFile -ScriptBlock {
    $pick = fzf --prompt 'file> ' --preview 'bat --color=always --style=numbers --line-range :200 {}'
    if ($pick) {
        $quoted = if ($pick -match '\s') { "'$pick'" } else { $pick }
        [Microsoft.PowerShell.PSConsoleReadLine]::Insert($quoted)
    }
    [Microsoft.PowerShell.PSConsoleReadLine]::InvokePrompt()
}

# --- prompt: starship (config: $env:STARSHIP_CONFIG -> omawin\starship\starship.toml) -------
# Win+Enter (omawin terminal here) opens a terminal in the focused tab's folder. noctty
# (TERM_PROGRAM ghostty) is told the folder directly (OSC 7; its new splits and tabs start
# there too). For Windows Terminal it reads the tab title and looks up the full path
# recorded here.
$TermDirs = Join-Path $env:LOCALAPPDATA 'launcher\dirs'
if (-not (Test-Path $TermDirs)) { New-Item -ItemType Directory -Path $TermDirs -Force | Out-Null }
function Invoke-Starship-PreCommand {
    # (Checked now, not at start-up: a spare shell started long before the profiling did.)
    if (-not $script:OmawinPrompted) {
        $script:OmawinPrompted = $true
        if (Test-Path "$env:LOCALAPPDATA\launcher\trace-on") { [IO.File]::AppendAllText("$env:LOCALAPPDATA\launcher\trace.log", "pwsh first-prompt`t$([Diagnostics.Stopwatch]::GetTimestamp())`n") }
    }
    $leaf = Split-Path -Leaf $PWD
    $host.UI.RawUI.WindowTitle = $leaf  # tab title = current folder
    if ($PWD.Provider.Name -ne 'FileSystem') { return }
    if ($env:TERM_PROGRAM -eq 'ghostty') {
        $uri = 'file://' + $env:COMPUTERNAME + '/' + ($PWD.ProviderPath -replace '\\', '/')
        [Console]::Write("$([char]27)]7;$uri$([char]27)\")
    } elseif ($leaf -notmatch '[\\/:*?"<>|]') {
        [IO.File]::WriteAllText((Join-Path $TermDirs $leaf), $PWD.ProviderPath)
    }
}
foreach ($init in "$ShellCache\starship.ps1", "$ShellCache\zoxide.ps1") {
    if (Test-Path $init) { . $init }  # zoxide: `z <part-of-path>` jumps, `zi` picks with fzf
}

# --- eza / bat ------------------------------------------------------------------
if (Test-Path Alias:ls) { Remove-Item Alias:ls -Force }
function ls { eza --icons --group-directories-first @args }
function ll { eza -la --icons --group-directories-first --git @args }
function lt { eza --tree --level 2 --icons --git-ignore @args }
$env:BAT_THEME = 'ansi'  # the terminal's 16 colors, so it follows the theme

# --- small helpers ----------------------------------------------------------------
function .. { Set-Location .. }
function ... { Set-Location ..\.. }
function which([string]$Name) { (Get-Command $Name -ErrorAction SilentlyContinue).Source }
function lg { lazygit @args }
function reload { . $PROFILE }

# `iex` is PowerShell's alias for Invoke-Expression; make it Elixir's IEx instead, except
# when something is piped in, so installer one-liners like `irm <url> | iex` still work.
if (Test-Path Alias:iex) { Remove-Item Alias:iex -Force }
function iex {
    if ($MyInvocation.ExpectingInput) { $input | Invoke-Expression } else { iex.bat @args }
}

# --- your own additions ---------------------------------------------------------------
# Your own shortcuts and tool paths live in your user layer (docs/work.md), if it's here.
# profile.ps1 (run first, in every host) set $OmawinUserLayer.
$workProfile = "$OmawinUserLayer\powershell\profile.ps1"
if (Test-Path $workProfile) { . $workProfile }
Remove-Variable workProfile

function gacp {
    # git add, commit and push in one go. Example: gacp fix the thing
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Message)
    git add .
    git commit -m ($Message -join ' ')
    git push
}

Write-OmawinTrace 'profile-end'
