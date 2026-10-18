<#
.SYNOPSIS
    omawin dev install: a development environment through mise, like Omarchy's
    omarchy-install-dev-env (Install > Development).

.DESCRIPTION
    Most languages come from mise (jdx.mise, installed here if it's missing) with
    `mise use --global <tool>@latest`, as on Omarchy, and run through mise's shims, which this
    puts first on the user PATH (they work in every terminal and in GUI programs, no shell hook).
    Where mise can't install a language on Windows it takes the Windows-native route instead:
    Ruby from RubyInstaller, PHP from php.net's builds, Erlang and Elixir from their installers,
    Rust from rustup, OCaml from opam (docs/dev-envs.md has the table).

        install-dev-env.ps1 node                  install one
        install-dev-env.ps1 docker-db [MySQL ...]  a database in Docker (no names: pick with fzf)
        install-dev-env.ps1 -Check node           exit 0 if it's installed, 1 if not
        install-dev-env.ps1 -Check                the installed ones, one a line (for the menu's check marks)

    Names: ruby docker-db node bun deno go php laravel symfony python elixir phoenix zig rust
    java dotnet ocaml clojure scala. bin/remove-dev-env.ps1 takes them away again.
#>
param(
    [Parameter(Position = 0)][string]$Name,
    [Parameter(Position = 1, ValueFromRemainingArguments = $true)][string[]]$Rest,
    [switch]$Check
)
$ErrorActionPreference = 'Stop'
$e = [char]27
$Names = 'ruby', 'docker-db', 'node', 'bun', 'deno', 'go', 'php', 'laravel', 'symfony', 'python',
    'elixir', 'phoenix', 'zig', 'rust', 'java', 'dotnet', 'ocaml', 'clojure', 'scala'

# mise keeps its tools here on Windows (MISE_DATA_DIR moves it).
$MiseData = if ($env:MISE_DATA_DIR) { $env:MISE_DATA_DIR } else { Join-Path $env:LOCALAPPDATA 'mise' }
$MiseInstalls = Join-Path $MiseData 'installs'
$LocalBin = Join-Path $HOME '.local\bin'
# The databases Omarchy's omarchy-install-docker-dbs offers, by container name.
$DockerDbs = [ordered]@{ MySQL = 'mysql8'; PostgreSQL = 'postgres18'; Redis = 'redis'; MongoDB = 'mongodb'; MariaDB = 'mariadb11'; MSSQL = 'mssql' }

# --- Is it installed? (Omarchy's `disabled` checks, for Windows) -------------------------------

function Test-MiseTool([string]$Pattern) {
    # A tool mise installed has a folder with at least one version in it; github: tools are
    # named after their repo, dots made dashes (github-borkdude-deps-clj), hence the pattern.
    # Only real folders count: the aliases beside them (latest, 26, ...) are junctions, which
    # `mise uninstall` leaves behind pointing at nothing.
    foreach ($dir in @(Get-ChildItem -Path $MiseInstalls -Directory -Filter $Pattern -ErrorAction SilentlyContinue)) {
        $real = Get-ChildItem -LiteralPath $dir.FullName -Directory -ErrorAction SilentlyContinue |
            Where-Object { -not ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) } | Select-Object -First 1
        if ($real) { return $true }
    }
    $false
}

function Test-Env([string]$Env) {
    switch ($Env) {
        # RubyInstaller puts each Ruby in C:\Ruby<ver>-x64.
        'ruby'      { [bool](Get-ChildItem 'C:\Ruby*\bin\ruby.exe' -ErrorAction SilentlyContinue) -or (Test-MiseTool 'ruby') }
        'docker-db' {
            if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { return $false }
            $have = @(docker ps -a --format '{{.Names}}' 2>$null)
            [bool]($DockerDbs.Values | Where-Object { $have -contains $_ })
        }
        'php'       { [bool](Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\PHP.PHP.*\php.exe" -ErrorAction SilentlyContinue) }
        'laravel'   { (Test-Path "$LocalBin\laravel.bat") -or (Test-Path "$env:APPDATA\Composer\vendor\bin\laravel.bat") }
        'symfony'   { Test-MiseTool '*symfony-cli*' }
        'elixir'    { (Test-Path "$env:ProgramFiles\Elixir\bin\elixir.bat") -or (Test-MiseTool 'elixir') }
        'phoenix'   { [bool](Get-ChildItem "$HOME\.mix\archives\phx_new*" -ErrorAction SilentlyContinue) }
        'rust'      { Test-Path "$HOME\.rustup" }
        # opam keeps its root in %LOCALAPPDATA%\opam on Windows (~/.opam elsewhere).
        'ocaml'     { (Test-Path "$env:LOCALAPPDATA\opam\config") -or (Test-Path "$HOME\.opam\config") }
        'clojure'   { Test-MiseTool '*deps-clj*' }
        default     { Test-MiseTool $Env }
    }
}

if ($Check) {
    if ($Name) {
        if ($Names -notcontains $Name) { Write-Host "Unknown environment: $Name" -ForegroundColor Red; exit 2 }
        if (Test-Env $Name) { exit 0 } else { exit 1 }
    }
    # Every installed one at once, so the menu needs one process, not nineteen.
    foreach ($n in $Names) { if (Test-Env $n) { $n } }
    exit 0
}

if (-not $Name -or $Names -notcontains $Name) {
    Write-Host "Usage: install-dev-env.ps1 <$($Names -join '|')>" -ForegroundColor Red
    exit 1
}

# --- Helpers ----------------------------------------------------------------------------------

function Say([string]$Text) { Write-Host "$e[1m$Text$e[0m`n" }

function Run {
    # Show a command dimmed, run it, and stop the install if it fails (native commands don't
    # throw on their own). Plain $args, not a param block, so flags like -e and -y reach the
    # program instead of being taken as PowerShell parameters.
    $exe, $rest = $args
    Write-Host "$e[2m> $exe $($rest -join ' ')$e[0m"
    & $exe @rest
    if ($LASTEXITCODE) { throw "$exe $($rest -join ' ') failed (exit $LASTEXITCODE)" }
}

function Update-SessionPath {
    # Pick up what an installer just added to the PATH, with mise's shims first, so the next
    # step finds the new tool without a new terminal.
    $machine = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $user = [Environment]::GetEnvironmentVariable('Path', 'User')
    $env:Path = (@((Join-Path $MiseData 'shims'), $user, $machine) | Where-Object { $_ }) -join ';'
}

function Add-UserPath([string]$Dir, [switch]$Prepend) {
    # Put $Dir on the user PATH unless it's there already (the same as install/lib.ps1's). The
    # PATH is read and written raw (REG_EXPAND_SZ), so entries like %LOCALAPPDATA%\mise\shims
    # stay unexpanded, and entries are compared expanded, so C:\...\shims and
    # %LOCALAPPDATA%\mise\shims count as the same one. Returns $true if it was added.
    $key = Get-Item 'HKCU:\Environment'
    $raw = [string]$key.GetValue('Path', '', 'DoNotExpandEnvironmentNames')
    $norm = { param($p) [Environment]::ExpandEnvironmentVariables($p).TrimEnd('\').ToLowerInvariant() }
    $want = & $norm $Dir
    foreach ($entry in $raw -split ';') { if ($entry -and (& $norm $entry) -eq $want) { return $false } }
    $raw = $raw.Trim(';')
    $new = if (-not $raw) { $Dir } elseif ($Prepend) { "$Dir;$raw" } else { "$raw;$Dir" }
    Set-ItemProperty 'HKCU:\Environment' -Name Path -Value $new -Type ExpandString
    # Tell running programs (Explorer) the environment changed, as SetEnvironmentVariable does.
    [Environment]::SetEnvironmentVariable('OMAWIN_PATH_NUDGE', '1', 'User')
    [Environment]::SetEnvironmentVariable('OMAWIN_PATH_NUDGE', $null, 'User')
    return $true
}

function Install-Winget([string]$Id) {
    Write-Host "$e[2m> winget install --id $Id$e[0m"
    winget install --id $Id -e --source winget --accept-source-agreements --accept-package-agreements --disable-interactivity
    # Already installed with nothing newer is fine too, and so is installed but wanting a restart
    # (Erlang's and RubyInstaller's installers can ask for one), as pkg-install.ps1 has it.
    if ($LASTEXITCODE -eq -1978334967) { Write-Host "$e[33m$Id is installed; Windows wants a restart to finish it.$e[0m" }
    elseif ($LASTEXITCODE -and $LASTEXITCODE -ne -1978335189 -and $LASTEXITCODE -ne -1978335135) {
        throw "winget install $Id failed (exit $LASTEXITCODE)"
    }
    Update-SessionPath
}

function Add-ToolPath([string]$Tool) {
    # An installer that doesn't put its tool on the PATH (Erlang/OTP's leaves erl off it): found in
    # Program Files, and its folder put on the user PATH.
    if (Get-Command $Tool -ErrorAction SilentlyContinue) { return }
    $found = Get-ChildItem $env:ProgramFiles, ${env:ProgramFiles(x86)} -Filter "$Tool.*" -Recurse -Depth 3 -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -in '.exe', '.bat', '.cmd' } | Sort-Object { $_.FullName.Length } | Select-Object -First 1
    if (-not $found) { return }
    if (Add-UserPath $found.DirectoryName) { Write-Host "$e[2mput $($found.DirectoryName) on your PATH$e[0m" }
    Update-SessionPath
}

function Get-NewestWingetId([string]$Prefix) {
    # The newest of a family of versioned winget ids (PHP.PHP.8.4, PHP.PHP.8.5, ...), so this
    # doesn't go stale when a new major version ships.
    $out = winget search --id $Prefix --source winget --accept-source-agreements --disable-interactivity 2>$null | Out-String
    # winget shortens a long id to fit the console ("RubyInstallerTeam.RubyWithDevKit.3…"): an
    # id cut short isn't taken, and the one picked is checked to exist before it's used.
    $rx = [regex]::Escape($Prefix) + '(\d+(?:\.\d+)*)(?![.\d]|…)'
    $found = [regex]::Matches($out, $rx) | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique { [version]($_ + '.0') } -Descending
    foreach ($version in $found) {
        winget show --id "$Prefix$version" -e --source winget --accept-source-agreements --disable-interactivity *> $null
        if ($LASTEXITCODE -eq 0) { return "$Prefix$version" }
    }
    throw "winget has no $Prefix* package"
}

function Initialize-Mise {
    # Find mise, or install it (user scope) and find it there; then put its shims first on the
    # user PATH. Shims on the PATH, not `mise activate`, so GUI programs (editors, the launcher)
    # see the same tools as a terminal, and a new shell costs nothing.
    if (-not (Get-Command mise -ErrorAction SilentlyContinue)) {
        Update-SessionPath
        if (-not (Get-Command mise -ErrorAction SilentlyContinue)) {
            Say 'Installing mise...'
            Install-Winget 'jdx.mise'
        }
    }
    if (-not (Get-Command mise -ErrorAction SilentlyContinue)) {
        # winget's PATH change can lag; its package folder is where it always is.
        $bin = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\jdx.mise_*\mise\bin\mise.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $bin) { throw 'mise is not installed and winget could not install it (winget install jdx.mise)' }
        $env:Path = "$($bin.DirectoryName);$env:Path"
    }
    if (Add-UserPath '%LOCALAPPDATA%\mise\shims' -Prepend) { Write-Host "$e[2mput mise's shims first on your PATH (new terminals see them)$e[0m" }
    Update-SessionPath
}

function Use-Mise([string]$Tool) { Run mise use --global $Tool }

function Warn-Shadowed([string]$Command) {
    # Windows puts the system PATH before the user's, so a copy installed for all users (say
    # C:\Program Files\nodejs) wins over mise's shim. Say so, rather than leave a mystery.
    # This session's PATH has the shims first, so look at the system PATH itself.
    $first = foreach ($dir in [Environment]::GetEnvironmentVariable('Path', 'Machine') -split ';') {
        if (-not $dir) { continue }
        foreach ($ext in '.exe', '.cmd', '.bat') {
            $p = Join-Path ([Environment]::ExpandEnvironmentVariables($dir)) "$Command$ext"
            if (Test-Path -LiteralPath $p) { $p; break }
        }
    }
    $first = @($first)[0]
    if ($first) {
        Write-Host "`n$e[33mNote: new terminals still run $first for '$Command': the system PATH comes before mise's shims."
        Write-Host "Uninstall that copy (omawin pkg remove) so mise's version is the one you get.$e[0m"
    }
}

# --- The environments (Omarchy's, in its order) -----------------------------------------------

function Install-Php {
    # mise's PHP compiles from source (Unix only) and Omarchy's static builds are Linux and macOS
    # only, so PHP comes from php.net's Windows build through winget (a portable zip).
    $id = Get-NewestWingetId 'PHP.PHP.'
    Install-Winget $id
    $php = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\$id`_*\php.exe" | Select-Object -First 1
    if (-not $php) { throw "PHP installed, but php.exe isn't where winget puts it" }

    # php.net's zip has no php.ini, so no extensions load (Composer needs openssl). Start from
    # its development ini and switch on what Composer, Laravel and Symfony expect.
    $ini = Join-Path $php.DirectoryName 'php.ini'
    if (-not (Test-Path $ini)) {
        $text = Get-Content (Join-Path $php.DirectoryName 'php.ini-development') -Raw
        $text = $text -replace '(?m)^;\s*extension_dir = "ext"', 'extension_dir = "ext"'
        foreach ($ext in 'curl', 'fileinfo', 'gd', 'intl', 'mbstring', 'openssl', 'pdo_mysql', 'pdo_pgsql', 'pdo_sqlite', 'sqlite3', 'zip') {
            $text = $text -replace "(?m)^;extension=$ext\s*$", "extension=$ext"
        }
        Set-Content -LiteralPath $ini -Value $text -Encoding ascii
        Write-Host "$e[2mwrote $ini (extensions on)$e[0m"
    }
    $env:Path = "$($php.DirectoryName);$env:Path"

    # Composer as Omarchy has it: its global tools land in ~/.local/bin, which is on the PATH.
    New-Item -ItemType Directory -Force -Path $LocalBin | Out-Null
    [void](Add-UserPath '%USERPROFILE%\.local\bin')
    if (-not (Test-Path "$LocalBin\composer.phar")) {
        Write-Host "$e[2m> download composer.phar$e[0m"
        Invoke-WebRequest 'https://getcomposer.org/download/latest-stable/composer.phar' -OutFile "$LocalBin\composer.phar" -UseBasicParsing
        Set-Content -LiteralPath "$LocalBin\composer.cmd" -Value '@php "%~dp0composer.phar" %*' -Encoding ascii
    }
    $env:Path = "$LocalBin;$env:Path"
    # Forward slashes: Composer drops the backslashes of a Windows path given here.
    Run composer global config bin-dir ($LocalBin -replace '\\', '/')
}

function Install-Node { Say 'Installing Node.js...'; Use-Mise 'node'; Warn-Shadowed 'node' }

function Install-Erlang-Elixir {
    # mise builds Erlang with kerl (Unix only), so Erlang/OTP and Elixir come from their own
    # Windows installers (they ask for admin once). mix then works as on Omarchy.
    Install-Winget 'Erlang.ErlangOTP'
    Install-Winget 'Elixir.Elixir'
    # Erlang/OTP's installer leaves erl off the PATH, and mix needs it.
    Add-ToolPath 'erl'
    Add-ToolPath 'mix'
}

function Install-DockerDb {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw 'Docker is not installed. Install Docker Desktop (winget install Docker.DockerDesktop), start it, and run this again.'
    }
    docker info *> $null
    if ($LASTEXITCODE) { throw 'Docker is installed but not running. Start Docker Desktop and run this again.' }

    $choices = @($Rest | Where-Object { $_ })
    if (-not $choices) {
        $choices = @($DockerDbs.Keys | fzf --multi --prompt 'database > ' --header 'Tab pick several  Enter install  Esc quit' --height 40% --reverse)
    }
    if (-not $choices) { Write-Host 'No databases selected for installation.'; exit 0 }
    foreach ($db in $choices) {
        Say "Installing $db..."
        # Omarchy's containers: the same images, ports (local only) and dev-only passwords.
        switch ($db) {
            'MySQL'      { Run docker run -d --restart unless-stopped -p '127.0.0.1:3306:3306' --name=mysql8 -e MYSQL_ROOT_PASSWORD= -e MYSQL_ALLOW_EMPTY_PASSWORD=true mysql:8.4 }
            'PostgreSQL' { Run docker run -d --restart unless-stopped -p '127.0.0.1:5432:5432' --name=postgres18 -e POSTGRES_HOST_AUTH_METHOD=trust postgres:18 }
            'MariaDB'    { Run docker run -d --restart unless-stopped -p '127.0.0.1:3306:3306' --name=mariadb11 -e MARIADB_ROOT_PASSWORD= -e MARIADB_ALLOW_EMPTY_ROOT_PASSWORD=true mariadb:11.8 }
            'Redis'      { Run docker run -d --restart unless-stopped -p '127.0.0.1:6379:6379' --name=redis redis:7 }
            'MongoDB'    { Run docker run -d --restart unless-stopped -p '127.0.0.1:27017:27017' --name mongodb -e MONGO_INITDB_ROOT_USERNAME=admin -e MONGO_INITDB_ROOT_PASSWORD=admin123 mongo:noble }
            'MSSQL'      { Run docker run -d --restart unless-stopped -p '127.0.0.1:1433:1433' --name mssql -e MSSQL_PID=Developer -e ACCEPT_EULA=Y -e 'MSSQL_SA_PASSWORD=@dmin123' mcr.microsoft.com/mssql/server:2022-CU12-ubuntu-22.04 }
            default      { throw "Unknown database: $db (pick from $($DockerDbs.Keys -join ', '))" }
        }
    }
}

$Labels = @{
    ruby = 'Ruby on Rails'; 'docker-db' = 'Docker DB'; node = 'Node.js'; bun = 'Bun'; deno = 'Deno'; go = 'Go'
    php = 'PHP'; laravel = 'Laravel'; symfony = 'Symfony'; python = 'Python'; elixir = 'Elixir'; phoenix = 'Phoenix'
    zig = 'Zig'; rust = 'Rust'; java = 'Java'; dotnet = '.NET'; ocaml = 'OCaml'; clojure = 'Clojure'; scala = 'Scala'
}

try {
    # mise only for the environments that use it (the rest come from their own installers).
    if ($Name -in 'node', 'bun', 'deno', 'go', 'laravel', 'symfony', 'python', 'zig', 'java', 'dotnet', 'clojure', 'scala') { Initialize-Mise }
    switch ($Name) {
        'ruby' {
            Say 'Installing Ruby on Rails...'
            # mise's Ruby compiles from source or uses Linux/macOS-only builds, so Ruby comes
            # from RubyInstaller, with MSYS2 for the gems (Rails among them) that compile C.
            Install-Winget (Get-NewestWingetId 'RubyInstallerTeam.RubyWithDevKit.')
            Run ridk install 3
            Set-Content -LiteralPath "$HOME\.gemrc" -Value 'gem: --no-document' -Encoding ascii
            Run gem install rails --no-document
            Write-Host "`nYou can now run: rails new myproject"
        }
        'docker-db' { Install-DockerDb }
        'node' { Install-Node }
        'bun' { Say 'Installing Bun...'; Use-Mise 'bun@latest'; Warn-Shadowed 'bun' }
        'deno' { Say 'Installing Deno...'; Use-Mise 'deno@latest'; Warn-Shadowed 'deno' }
        'go' { Say 'Installing Go...'; Use-Mise 'go@latest'; Warn-Shadowed 'go' }
        'php' { Say 'Installing PHP...'; Install-Php }
        'laravel' {
            Say 'Installing PHP and Laravel...'
            Install-Php
            Install-Node
            Run composer global require laravel/installer
            Write-Host "`nYou can now run: laravel new myproject"
        }
        'symfony' {
            Say 'Installing PHP and Symfony...'
            Install-Php
            # Omarchy's symfony-cli package; on Windows, its release binary through mise.
            Use-Mise 'github:symfony-cli/symfony-cli@latest'
            Write-Host "`nYou can now run: symfony new --webapp myproject"
        }
        'python' {
            Say 'Installing Python...'
            Use-Mise 'python@latest'
            Say "`nInstalling uv..."
            Use-Mise 'uv@latest'
            Warn-Shadowed 'python'
        }
        'elixir' {
            Say 'Installing Elixir...'
            Install-Erlang-Elixir
            Run mix local.hex --force
        }
        'phoenix' {
            Say 'Installing Phoenix Framework...'
            Install-Erlang-Elixir
            Run mix local.hex --force
            Run mix local.rebar --force
            Run mix archive.install hex phx_new --force
            Write-Host "`nYou can now run: mix phx.new my_app"
        }
        'zig' { Say 'Installing Zig...'; Use-Mise 'zig@latest'; Use-Mise 'zls@latest' }
        'rust' {
            Say 'Installing Rust...'
            # rustup, as on Omarchy (mise would only wrap it); its winget package installs the
            # stable toolchain into ~/.rustup and ~/.cargo.
            Install-Winget 'Rustlang.Rustup'
            Run "$HOME\.cargo\bin\rustup.exe" default stable
            # The default (MSVC) toolchain links with Visual Studio's C++ build tools; say how to
            # get them rather than pull gigabytes in unasked.
            $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
            $vc = if (Test-Path $vswhere) { & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath }
            if (-not $vc) {
                Write-Host "`n$e[33mRust needs the Visual Studio C++ build tools to link. Install them with:"
                Write-Host "  winget install Microsoft.VisualStudio.2022.BuildTools --override `"--quiet --wait --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended`"$e[0m"
            }
        }
        'java' { Say 'Installing Java...'; Use-Mise 'java@latest'; Warn-Shadowed 'java' }
        'ocaml' {
            Say 'Installing OCaml...'
            # opam runs natively on Windows since 2.2; `opam init` sets up its own Cygwin and a
            # MinGW compiler, then builds OCaml (this takes a while).
            Install-Winget 'OCaml.opam'
            Run opam init --yes --cygwin-internal-install
            (& opam env --shell=powershell) -join "`n" | Invoke-Expression
            Run opam install ocaml-lsp-server odoc ocamlformat utop --yes
        }
        'dotnet' { Say 'Installing .NET...'; Use-Mise 'dotnet@latest'; Warn-Shadowed 'dotnet' }
        'clojure' {
            Say 'Installing Clojure...'
            # mise's clojure plugin runs the Unix install script, so on Windows the Clojure CLI is
            # deps.clj (a native port of it, the one clojure.org points Windows users to), on
            # Java from mise, answering to `clojure` and `clj` as on Omarchy.
            Use-Mise 'java@latest'
            Use-Mise 'github:borkdude/deps.clj@latest'
            New-Item -ItemType Directory -Force -Path $LocalBin | Out-Null
            [void](Add-UserPath '%USERPROFILE%\.local\bin')
            foreach ($cmd in 'clojure', 'clj') { Set-Content -LiteralPath "$LocalBin\$cmd.cmd" -Value '@deps %*' -Encoding ascii }
        }
        'scala' {
            Say 'Installing Scala...'
            Use-Mise 'java@latest'
            Use-Mise 'scala@latest'
            Use-Mise 'scala-cli@latest'
        }
    }
    if ($Name -ne 'docker-db' -and -not (Test-Env $Name)) { throw "the install finished, but $($Labels[$Name]) isn't where it should be" }
    Write-Host "`n$e[32m$([char]0x2713) $($Labels[$Name]) is installed.$e[0m Open a new terminal to use it."
    exit 0
} catch {
    Write-Host "`n$e[31m$([char]0x2717) $($Labels[$Name]) failed: $($_.Exception.Message)$e[0m"
    exit 1
}
