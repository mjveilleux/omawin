<#
.SYNOPSIS
    omawin dev remove: take away a development environment bin/install-dev-env.ps1 installed,
    like Omarchy's omarchy-remove-dev-env (Remove > Development).

.DESCRIPTION
        remove-dev-env.ps1 node
        remove-dev-env.ps1 docker-db [MySQL ...]   (no names: pick the containers with fzf)

    Names: ruby docker-db node bun deno go php laravel symfony python elixir phoenix zig rust
    java dotnet ocaml clojure scala. It's done when install-dev-env.ps1 -Check no longer finds it.
#>
param(
    [Parameter(Position = 0)][string]$Name,
    [Parameter(Position = 1, ValueFromRemainingArguments = $true)][string[]]$Rest
)
$ErrorActionPreference = 'Stop'
$e = [char]27
$Names = 'ruby', 'docker-db', 'node', 'bun', 'deno', 'go', 'php', 'laravel', 'symfony', 'python',
    'elixir', 'phoenix', 'zig', 'rust', 'java', 'dotnet', 'ocaml', 'clojure', 'scala'
if (-not $Name -or $Names -notcontains $Name) {
    Write-Host "Usage: remove-dev-env.ps1 <$($Names -join '|')>" -ForegroundColor Red
    exit 1
}
$MiseData = if ($env:MISE_DATA_DIR) { $env:MISE_DATA_DIR } else { Join-Path $env:LOCALAPPDATA 'mise' }
$LocalBin = Join-Path $HOME '.local\bin'
$Labels = @{
    ruby = 'Ruby on Rails'; 'docker-db' = 'Docker DB'; node = 'Node.js'; bun = 'Bun'; deno = 'Deno'; go = 'Go'
    php = 'PHP'; laravel = 'Laravel'; symfony = 'Symfony'; python = 'Python'; elixir = 'Elixir'; phoenix = 'Phoenix'
    zig = 'Zig'; rust = 'Rust'; java = 'Java'; dotnet = '.NET'; ocaml = 'OCaml'; clojure = 'Clojure'; scala = 'Scala'
}

function Say([string]$Text) { Write-Host "$e[1m$Text$e[0m`n" }

function Try-Run {
    # Run a removal step and carry on if it fails, as Omarchy's script does (there may be
    # nothing to remove); whether the whole thing worked is judged at the end by -Check.
    # Plain $args, so flags like -e and -y reach the program, not PowerShell.
    $exe, $rest = $args
    Write-Host "$e[2m> $exe $($rest -join ' ')$e[0m"
    & $exe @rest
}

function Remove-Mise([string[]]$Tools) {
    if (-not (Get-Command mise -ErrorAction SilentlyContinue)) {
        $bin = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\jdx.mise_*\mise\bin\mise.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $bin) { Write-Host 'mise is not installed, so there is nothing of it to remove.'; return }
        $env:Path = "$($bin.DirectoryName);$env:Path"
    }
    # Out of the global config first (that prunes what nothing else asks for), then every
    # version left, as Omarchy's `mise uninstall --all` + `mise rm -g`.
    foreach ($t in $Tools) { Try-Run mise unuse --global $t }
    foreach ($t in $Tools) {
        $dir = Join-Path $MiseData "installs\$($t -replace '[:/.]', '-')"
        if (-not (Test-Path $dir)) { continue }
        Try-Run mise uninstall $t --all
        # mise leaves the version aliases (latest, 26, ...) as junctions to nothing; with no
        # version left, the folder goes too, so the menu stops counting it as installed.
        $left = Get-ChildItem -LiteralPath $dir -Directory -ErrorAction SilentlyContinue |
            Where-Object { -not ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) }
        if (-not $left) { Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue }
    }
    if ($Tools) { Try-Run mise reshim }
}

function Remove-Winget([string]$Prefix) {
    # Every installed package whose id starts with $Prefix (PHP.PHP.8.4 and 8.5, say).
    $out = winget list --id $Prefix --accept-source-agreements --disable-interactivity 2>$null | Out-String
    $ids = [regex]::Matches($out, '(?<=\s)' + [regex]::Escape($Prefix) + '[\w.+-]*') | ForEach-Object Value | Sort-Object -Unique
    foreach ($id in $ids) { Try-Run winget uninstall --id $id -e --disable-interactivity }
}

function Remove-DockerDb {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { Write-Host 'Docker is not installed.'; return }
    $known = 'mysql8', 'postgres18', 'redis', 'mongodb', 'mariadb11', 'mssql'
    $have = @(docker ps -a --format '{{.Names}}' 2>$null | Where-Object { $known -contains $_ })
    if (-not $have) { Write-Host 'None of the Docker DB containers are here.'; return }
    $pick = @($Rest | Where-Object { $_ })
    if (-not $pick) { $pick = @($have | fzf --multi --prompt 'remove > ' --header 'Tab pick several  Enter remove  Esc quit' --height 40% --reverse) }
    foreach ($c in $pick) { Try-Run docker rm --force --volumes $c }
}

switch ($Name) {
    'ruby' {
        Say 'Removing Ruby...'
        Remove-Winget 'RubyInstallerTeam.'
        Remove-Mise 'ruby'
        Remove-Item "$HOME\.gemrc" -Force -ErrorAction SilentlyContinue
    }
    'docker-db' { Say 'Removing Docker databases...'; Remove-DockerDb }
    'node' { Say 'Removing Node.js...'; Remove-Mise 'node' }
    'bun' { Say 'Removing Bun...'; Remove-Mise 'bun' }
    'deno' { Say 'Removing Deno...'; Remove-Mise 'deno' }
    'go' { Say 'Removing Go...'; Remove-Mise 'go' }
    'php' {
        Say 'Removing PHP...'
        Remove-Winget 'PHP.PHP.'
        # winget leaves the folder behind when it holds a file it didn't unpack (our php.ini).
        Remove-Item "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\PHP.PHP.*" -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item "$LocalBin\composer.phar", "$LocalBin\composer.cmd" -Force -ErrorAction SilentlyContinue
    }
    'laravel' {
        Say 'Removing Laravel...'
        if (Get-Command composer -ErrorAction SilentlyContinue) { Try-Run composer global remove laravel/installer }
        Remove-Item "$LocalBin\laravel", "$LocalBin\laravel.bat", "$env:APPDATA\Composer\vendor\bin\laravel*" -Force -ErrorAction SilentlyContinue
    }
    'symfony' { Say 'Removing Symfony CLI...'; Remove-Mise 'github:symfony-cli/symfony-cli' }
    'python' { Say 'Removing Python...'; Remove-Mise 'python' }
    { $_ -in 'elixir', 'phoenix' } {
        Say 'Removing Elixir/Erlang...'
        Remove-Winget 'Elixir.Elixir'
        Remove-Winget 'Erlang.ErlangOTP'
        Remove-Mise 'elixir', 'erlang'
        # Phoenix's project generator outlives Elixir in ~/.mix; without Elixir it's dead weight.
        Remove-Item "$HOME\.mix\archives\phx_new*" -Recurse -Force -ErrorAction SilentlyContinue
    }
    'zig' { Say 'Removing Zig...'; Remove-Mise 'zig', 'zls' }
    'rust' {
        Say 'Removing Rust...'
        # By path: ~/.cargo/bin may be off the PATH, and mise's rust shims would shadow rustup.
        $rustup = "$HOME\.cargo\bin\rustup.exe"
        if (Test-Path $rustup) { Try-Run $rustup self uninstall -y }
        Remove-Winget 'Rustlang.Rustup'
        Remove-Item "$MiseData\installs\rust" -Recurse -Force -ErrorAction SilentlyContinue
        if (Get-Command mise -ErrorAction SilentlyContinue) { Try-Run mise reshim }
    }
    'java' { Say 'Removing Java...'; Remove-Mise 'java' }
    'dotnet' { Say 'Removing .NET...'; Remove-Mise 'dotnet' }
    'ocaml' {
        Say 'Removing OCaml...'
        if (Get-Command opam -ErrorAction SilentlyContinue) { Try-Run opam switch remove default -y }
        Remove-Item "$env:LOCALAPPDATA\opam", "$HOME\.opam" -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Winget 'OCaml.opam'
    }
    'clojure' {
        Say 'Removing Clojure...'
        Remove-Mise 'github:borkdude/deps.clj'
        Remove-Item "$LocalBin\clojure.cmd", "$LocalBin\clj.cmd" -Force -ErrorAction SilentlyContinue
    }
    'scala' { Say 'Removing Scala...'; Remove-Mise 'scala', 'scala-cli' }
}

# Docker DB removes the containers picked, not necessarily all of them, so it has no check.
if ($Name -ne 'docker-db') { & "$PSScriptRoot\install-dev-env.ps1" -Check $Name } else { $LASTEXITCODE = 1 }
if ($LASTEXITCODE -eq 0) {
    Write-Host "`n$e[31m$([char]0x2717) $($Labels[$Name]) is still here: see the errors above.$e[0m"
    exit 1
}
Write-Host "`n$e[32m$([char]0x2713) $($Labels[$Name]) is removed.$e[0m"
exit 0
