# install.ps1 step: the core winget packages and the chosen presets' (install/packages.ps1), other
# installers, the Nerd Font, then Neovim's and Neovide's config. -LinkOnly skips the installs and
# keeps the config.
# Dot-sourced by install.ps1, so its functions (install/lib.ps1), $Stamp and the package lists
# (install/packages.ps1) are in scope. install.ps1 (to plan the admin step) and bin/preset.ps1 (to install
# one preset) dot-source it with $SoftwareFunctionsOnly set, for the functions alone.

# The winget packages installed on this PC, from one `winget export` (every installed package it
# knows) rather than a `winget list` per package (1-2 s each). Asked once per install; $null if
# winget can't say, and then each package is asked after all. -Fresh asks again.
function Get-WingetInstalled([switch]$Fresh) {
    if ($script:WingetInstalledDone -and -not $Fresh) { return $script:WingetInstalled }
    $script:WingetInstalledDone = $true
    $script:WingetInstalled = $null
    # winget's whole list takes 10-20 s on a new account. A PC omawin has never been on (no
    # migrations.txt) goes by commands and files instead (Test-PackageInstalled); a reinstall or an
    # update reads it, so nothing of yours is installed twice.
    if (-not $Fresh -and -not (Test-Path "$env:LOCALAPPDATA\launcher\migrations.txt")) { $script:WingetListSkipped = $true; return $null }
    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if (-not $winget) { return $null }
    $export = Join-Path $env:TEMP "omawin-winget-$PID.json"
    try {
        $null = & $winget.Source export --output $export --accept-source-agreements --disable-interactivity 2>$null
        if (Test-Path $export) {
            $found = @{}
            foreach ($source in (Get-Content $export -Raw | ConvertFrom-Json).Sources) {
                foreach ($package in $source.Packages) { $found[$package.PackageIdentifier] = $true }
            }
            $script:WingetInstalled = $found
        }
    } catch { } finally { Remove-Item $export -ErrorAction SilentlyContinue }
    $script:WingetInstalled
}

# Installed: its command is on the PATH ($WingetCommands: however it got there), or winget has it.
function Test-PackageInstalled([string]$Id, $Installed) {
    $command = $WingetCommands[$Id]
    if ($command -and (Get-Command $command -ErrorAction SilentlyContinue)) { return $true }
    $file = $WingetFiles[$Id]
    if ($file -and (Test-Path $file)) { return $true }
    if ($Installed) { return $Installed.ContainsKey($Id) }
    # A new install didn't read winget's list (Get-WingetInstalled): what neither its command nor
    # its file shows isn't here, and winget says so in a second if it is.
    if ($script:WingetListSkipped) { return $false }
    & { $ErrorActionPreference = 'Continue'; winget list --id $Id --exact --accept-source-agreements *> $null }
    $LASTEXITCODE -eq 0
}

# A package's short name for the screen: its id without the publisher (Git.Git -> Git).
function Get-PackageName([string]$Id) {
    $parts = $Id -split '\.'
    if ($parts.Count -lt 2) { return $Id }
    $name = ($parts[1..($parts.Count - 1)] -join '.') -replace '\.(MSVC|LTS)$'
    # A name that says nothing on its own (GitHub.cli) keeps its publisher.
    if ($name -in 'cli', 'app') { $parts[0] } else { $name }
}

# Installs $Ids with winget, up to $Width at once, each with its output captured (to the log, and
# its last lines on a failure). $Tick runs between looks (the admin step's progress). Returns the
# ones that failed.
function Invoke-WingetBatch([string]$Winget, [string[]]$Ids, [int]$Width, [scriptblock]$Tick) {
    $queue = New-Object Collections.Generic.Queue[string]
    foreach ($id in $Ids) { $queue.Enqueue($id) }
    $total = $queue.Count
    if (-not $total) { return }
    $running = @{}
    $failed = @()
    $done = 0
    $t0 = Get-Date
    $frame = 0
    $line = $UI.Fancy -and -not $UI.Board
    if ($line) { [Console]::Out.Write($UI.C.HideCursor) }
    try {
        while ($queue.Count -or $running.Count) {
            while ($queue.Count -and $running.Count -lt $Width) {
                $id = $queue.Dequeue()
                $wingetArgs = @('install', '--id', $id, '--exact', '--silent', '--accept-source-agreements', '--accept-package-agreements', '--disable-interactivity')
                if ($WingetOverrides[$id]) { $wingetArgs += '--override', $WingetOverrides[$id] }
                # After the one admin prompt (install.ps1), a whole-PC package installs for this
                # account if it can, or fails, but never raises a prompt of its own.
                if ($env:OMAWIN_NO_ADMIN -and $WholePC -contains $id) { $wingetArgs += '--scope', 'user' }
                $psi = New-Object Diagnostics.ProcessStartInfo
                $psi.FileName = $Winget
                $psi.Arguments = (@($wingetArgs | ForEach-Object { ConvertTo-NativeArgument $_ }) -join ' ')
                $psi.UseShellExecute = $false
                $psi.RedirectStandardOutput = $true
                $psi.RedirectStandardError = $true
                $psi.StandardOutputEncoding = [Text.Encoding]::UTF8
                $psi.StandardErrorEncoding = [Text.Encoding]::UTF8
                $p = [Diagnostics.Process]::Start($psi)
                $running[$id] = @{ P = $p; Out = $p.StandardOutput.ReadToEndAsync(); Err = $p.StandardError.ReadToEndAsync(); Start = Get-Date }
                Set-BoardPackage $id 'run'
                if (-not $UI.Fancy) { Write-Note "installing $id" }
            }
            foreach ($id in @($running.Keys)) {
                $r = $running[$id]
                if (-not $r.P.HasExited) { continue }
                $r.P.WaitForExit()
                $output = $r.Out.Result + $r.Err.Result
                $seconds = ((Get-Date) - $r.Start).TotalSeconds
                Write-Log ("    $ winget install --id $id  (exit $($r.P.ExitCode), $([int]$seconds) s)")
                $running.Remove($id)
                $done++
                if ($line) { [Console]::Out.Write($UI.C.Clear) }
                # 0x8A15002B / 0x8A150061: winget found it installed after all (its export can miss one);
                # 0x8A150109: installed, Windows wants a restart.
                if ($r.P.ExitCode -in 0, -1978335189, -1978335135, -1978334967) { Set-BoardPackage $id 'done'; Write-Done $id $seconds; Set-PackageSource $id 'winget' $id }
                else {
                    if ($output.Trim()) { Write-Log ($output.Trim() -replace "`r", '') }
                    Set-BoardPackage $id 'wait'
                    $failed += [pscustomobject]@{ Id = $id; ExitCode = $r.P.ExitCode; Output = $output }
                }
            }
            if ($Tick) { & $Tick }
            if ($line -and $running.Count) {
                $names = (@($running.Keys) | Sort-Object) -join ', '
                $room = [Math]::Max(10, $UI.Width - 40)
                if ($names.Length -gt $room) { $names = $names.Substring(0, $room - 3) + '...' }
                $label = '{0}/{1} done, {2} at once  {3}' -f $done, $total, $running.Count, $names
                [Console]::Out.Write(("{0}      {1}{2}{3} {4}  {5}{6}{3}" -f $UI.C.Clear, $UI.C.Accent, $UI.G.Spin[$frame % $UI.G.Spin.Count], $UI.C.Reset, $label, $UI.C.Dim, (Format-Duration ((Get-Date) - $t0).TotalSeconds)))
                $frame++
            }
            Start-Sleep -Milliseconds 100
        }
    } finally { if ($line) { [Console]::Out.Write($UI.C.Clear + $UI.C.ShowCursor) } }
    $failed
}

# A download to a file, fast: curl.exe (in Windows since 10 1803) when it's there, else
# Invoke-WebRequest with its progress bar off (Windows PowerShell redraws it so often that a
# 100 MB file takes minutes).
function Save-Download([string]$Url, [string]$OutFile) {
    $curl = Join-Path $env:WINDIR 'System32\curl.exe'
    if (Test-Path $curl) {
        & { $ErrorActionPreference = 'Continue'; & $curl -fsSL --retry 3 -o $OutFile $Url 2>$null }
        if ($LASTEXITCODE -eq 0) { return }
    }
    $before = $ProgressPreference
    $ProgressPreference = 'SilentlyContinue'
    try { Invoke-WebRequest $Url -OutFile $OutFile -UseBasicParsing } finally { $ProgressPreference = $before }
}

# winget's package list (the "winget" source) is an app package of its own, registered for an
# account at its first desktop sign-in. An account that hasn't had one yet (made moments ago,
# or signed in some other way) gets "0x8a15000f: Data required by the source is missing" for
# every package. Registering it is per-user and needs no admin.
function Register-WingetSource {
    # When the PC has no copy to register (Windows Server, a trimmed image), Microsoft's own signed
    # package list is downloaded and added for this account instead.
    Use-Appx
    if (Get-AppxPackage -Name Microsoft.Winget.Source -ErrorAction SilentlyContinue) { return }
    try {
        Add-AppxPackage -RegisterByFamilyName -MainPackage Microsoft.Winget.Source_8wekyb3d8bbwe -ErrorAction Stop
        Write-Note "registered winget's package list for this account"
        return
    } catch { }
    $msix = Join-Path $env:TEMP "winget-source-$Stamp.msix"
    try {
        Save-Download 'https://cdn.winget.microsoft.com/cache/source.msix' $msix
        Add-AppxPackage -Path $msix -ErrorAction Stop
        Write-Note "added winget's package list for this account"
    } catch { Write-Warn "winget's package list isn't set up for this account ($($_.Exception.Message)); sign out and in, then run install.ps1 again" }
    finally { Remove-Item $msix -Force -ErrorAction SilentlyContinue }
}

# --- the tools bundle (roadmap 2.0, install/tools.txt) -------------------------------------------
# Omarchy installs from its ISO's offline package cache; omawin's release carries the core tools
# the same way, built by CI from each tool's own release: a zip per tool, all fetched at once and
# unpacked side by side into %LOCALAPPDATA%\omawin\tools. No winget, no installers, no admin.
# What isn't in the bundle (or a release without one) still goes through winget below.
$ToolsDir = Join-Path $env:LOCALAPPDATA 'omawin\tools'

# Where the release's files are: OMAWIN_RELEASE_URL (a folder or URL: a locally built release, a
# mirror), else this repo's GitHub release (OMAWIN_RELEASE's tag, or the newest). The names carry
# no version, so the newest is releases/latest/download/<name>, with no API call.
function Get-ReleaseBase {
    if ($env:OMAWIN_RELEASE_URL) { return $env:OMAWIN_RELEASE_URL.TrimEnd('/', '\') }
    $repoName = if ($env:OMAWIN_REPO) { $env:OMAWIN_REPO } else { 'mjveilleux/omawin' }
    if ($env:OMAWIN_RELEASE -and $env:OMAWIN_RELEASE -ne 'latest') { return "https://github.com/$repoName/releases/download/$env:OMAWIN_RELEASE" }
    "https://github.com/$repoName/releases/latest/download"
}

# One release file: copied from a folder, or downloaded. $null when it isn't there.
function Get-ReleaseText([string]$Base, [string]$Name) {
    if ($Base -notmatch '^[a-z]+://') {
        $path = Join-Path $Base $Name
        if (Test-Path -LiteralPath $path) { return [IO.File]::ReadAllText($path) } else { return $null }
    }
    $tmp = Join-Path $env:TEMP "omawin-$Stamp-$Name"
    try { Save-Download "$Base/$Name" $tmp; return [IO.File]::ReadAllText($tmp) } catch { return $null }
    finally { Remove-Item $tmp -Force -ErrorAction SilentlyContinue }
}

# The bundle's list (omawin-tools.txt), checked against the release's SHA256SUMS.txt; $null when
# the release has none (an older one), or it doesn't match (then nothing in it is trusted).
# Whether Windows' own tar reads zstd (Windows 11 23H2 on): then a tool split into parts is
# unpacked from them, all at once; otherwise from its single file.
function Test-TarZstd {
    if ($null -eq $script:TarZstd) {
        $script:TarZstd = [bool](& "$env:WINDIR\System32\tar.exe" --version 2>$null | Select-String 'libzstd' -Quiet)
    }
    $script:TarZstd
}

function Read-ToolBundle {
    if ($script:ToolBundleRead) { return $script:ToolBundle }
    $script:ToolBundleRead = $true
    $base = Get-ReleaseBase
    $text = Get-ReleaseText $base 'omawin-tools.txt'
    if (-not $text) { return $null }
    $sums = Get-ReleaseText $base 'SHA256SUMS.txt'
    $want = if ($sums) { ($sums -split "`n" | Where-Object { $_ -match '^\s*([0-9a-fA-F]{64})\s+\*?omawin-tools\.txt\s*$' } | ForEach-Object { $Matches[1].ToLower() }) | Select-Object -First 1 }
    $sha = [Security.Cryptography.SHA256]::Create()
    $have = -join ($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($text)) | ForEach-Object { $_.ToString('x2') })
    if (-not $want -or $want -ne $have) { Write-Warn "the release's tool list doesn't match its SHA256SUMS: not used"; return $null }
    $script:ToolBundle = @(foreach ($line in $text -split "`r?`n") {
            $f = $line -split "`t"
            if ($f.Count -lt 6 -or $f[0] -notmatch '^[a-z0-9-]+$' -or $f[3] -notmatch '^[0-9a-f]{64}$') { continue }
            $dir = if ($f.Count -gt 6 -and $f[6]) { Join-Path $env:LOCALAPPDATA $f[6] } else { Join-Path $ToolsDir $f[0] }
            [pscustomobject]@{ Name = $f[0]; Version = $f[1]; File = $f[2]; Sha = $f[3]; Bin = $f[4]; Provides = $f[5]; Dir = $dir; Base = $base; Later = ($f.Count -gt 7 -and ($f[7] -split ' ') -contains 'later'); Merge = ($f.Count -gt 7 -and ($f[7] -split ' ') -contains 'merge'); Sfx = ($f.Count -gt 7 -and ($f[7] -split ' ') -contains 'sfx'); Optional = ($f.Count -gt 7 -and ($f[7] -split ' ') -contains 'optional')
                Parts = $(if ($f.Count -gt 9 -and $f[8] -and $f[9]) {
                        $files = $f[8] -split ','; $shas = $f[9] -split ','
                        if ($files.Count -eq $shas.Count -and -not ($shas | Where-Object { $_ -notmatch '^[0-9a-f]{64}$' })) {
                            @(for ($k = 0; $k -lt $files.Count; $k++) { [pscustomobject]@{ File = $files[$k]; Sha = $shas[$k] } })
                        }
                    }) }
        })
    $script:ToolBundle
}

# The winget ids the bundle stands in for (the admin step and the winget batch leave them out).
function Get-BundleProvides { @(Read-ToolBundle | Where-Object Provides | ForEach-Object Provides) }

# A tool you put in yourself, some other way: its command on the PATH, or in winget's list. Only
# what's already known: asking winget about each one would cost a second apiece.
function Test-ToolTheirs($Tool, $Installed) {
    $exe = if ($WingetCommands[$Tool.Provides]) { $WingetCommands[$Tool.Provides] } else { $Tool.Name }
    if (Get-Command $exe -CommandType Application -ErrorAction SilentlyContinue) { return $true }
    return [bool]($Installed -and $Installed.ContainsKey($Tool.Provides))
}

# A tool's line on the install screen: the package row it stands in for, or its own.
function Set-ToolItem($Tool, [string]$State) {
    if ($Tool.Provides) { Set-BoardPackage $Tool.Provides $State } else { Add-InstallItem 'Tools' "tool:$($Tool.Name)" $Tool.Name $State }
}

# Puts the bundle's tools in, in two halves so the rest of the install goes on meanwhile:
# Start-ToolBundle (as early as the install can) decides what's needed and hands the downloads
# and unzips to a PowerShell of its own (install/unpack-tools.ps1: all at once, curl --parallel
# and tar.exe); Complete-ToolBundle (before anything needs the tools) waits for it, then writes
# the PATH once and records where each came from. A tool already here at the same version is
# left alone; one you installed yourself some other way (its command on the PATH, or in winget's
# list) is used as it is.
function Start-ToolBundle($Installed, [string[]]$Only) {
    if ($script:BundleStart) { return }
    # An optional tool (the plugins' QML host) only when it's asked for by name, or already here
    # (so an update updates it).
    $tools = @(Read-ToolBundle | Where-Object {
            if ($Only) { $Only -contains $_.Name }
            else { -not $_.Optional -or (Test-Path -LiteralPath (Join-Path $_.Dir '.omawin-version')) }
        })
    $script:BundleHave = @()
    $script:BundleTheirs = @()   # winget ids already here some other way: winget leaves them alone too
    $todo = @()
    foreach ($t in $tools) {
        # Its own name: PowerShell's variable names ignore case, and the run's timestamp is in use.
        $versionFile = Join-Path $t.Dir '.omawin-version'
        $label = if ($t.Provides) { Get-PackageName $t.Provides } else { $t.Name }
        $mine = (Test-Path -LiteralPath $versionFile) -and ((Get-Content -LiteralPath $versionFile -Raw).Trim() -eq "$($t.Version) $($t.Sha)")
        if ($mine) { $script:BundleHave += $t; Set-ToolItem $t 'have'; continue }
        if (-not (Test-Path -LiteralPath $versionFile) -and $t.Provides -and (Test-ToolTheirs $t $Installed)) {
            Write-Log "    $label (yours)"; Set-ToolItem $t 'have'; $script:BundleTheirs += $t.Provides
            if ($Installed -and $Installed.ContainsKey($t.Provides)) { Set-PackageSource $t.Provides 'winget' $t.Provides }
            elseif (-not (Get-PackageSources)[$t.Provides]) { Set-PackageSource $t.Provides 'found' $t.Provides }
            continue
        }
        $todo += $t
        Set-ToolItem $t 'wait'
    }
    $script:BundleStart = @{ Tools = $tools; Todo = $todo; Process = $null; Work = $null; Clock = [Diagnostics.Stopwatch]::StartNew() }
    if (-not $todo) { return }
    $work = Join-Path $env:TEMP "omawin-tools-$Stamp"
    New-Item -ItemType Directory -Force $work | Out-Null
    $plan = foreach ($t in $todo) {
        $remote = $t.Base -match '^[a-z]+://'
        # Split into parts, and this tar reads zstd: the parts, unpacked at once (one tar each).
        $parts = if ($t.Parts -and (Test-TarZstd)) {
            @($t.Parts | ForEach-Object {
                    [pscustomobject]@{
                        Sha = $_.Sha
                        Url = $(if ($remote) { "$($t.Base)/$($_.File)" } else { $null })
                        Zip = $(if ($remote) { Join-Path $work $_.File } else { Join-Path $t.Base $_.File })
                    }
                })
        }
        [pscustomobject]@{
            Name = $t.Name; Version = $t.Version; Sha = $t.Sha; Dir = $t.Dir; Merge = [bool]$t.Merge; Sfx = [bool]$t.Sfx
            Url = $(if ($remote) { "$($t.Base)/$($t.File)" } else { $null })
            Zip = $(if ($remote) { Join-Path $work $t.File } else { Join-Path $t.Base $t.File })
            Parts = $parts
        }
    }
    @($plan) | ConvertTo-Json -AsArray -Depth 6 | Set-Content (Join-Path $work 'plan.json') -Encoding utf8
    # Not under $work: a tool marked later may still be unpacking when this install has finished.
    $done = Join-Path $env:TEMP "omawin-tools-done-$Stamp"
    New-Item -ItemType Directory -Force $done | Out-Null
    $p = Start-Process (Join-Path $PSHOME 'pwsh.exe') -WindowStyle Hidden -PassThru -ArgumentList '-NoLogo', '-NoProfile', '-ExecutionPolicy', 'Bypass',
        '-File', "`"$Repo\install\unpack-tools.ps1`"", '-Plan', "`"$work\plan.json`"", '-Done', "`"$done`""
    $null = $p.Handle  # read now, so its ExitCode is there later
    $script:BundleStart.Process = $p
    $script:BundleStart.Work = $work
    $script:BundleStart.DoneDir = $done
    foreach ($t in $todo) { Set-ToolItem $t 'run' }
}

# The winget ids the bundle takes care of: the tools it's putting in, and the ones already here.
function Get-BundleCovered {
    if (-not $script:BundleStart) { return @() }
    @(@($script:BundleStart.Tools | Where-Object { $_.Provides -and ($script:BundleTheirs -notcontains $_.Provides) } | ForEach-Object Provides) + @($script:BundleTheirs))
}

# Waits for some of the bundle's tools only (a step that needs them before the rest has landed:
# the desktop step's GlazeWM), and puts them on this session's PATH; the user's PATH is written
# once, when the bundle is collected (Complete-ToolBundle, the tools step).
function Wait-BundleTools([string[]]$Names) {
    $b = $script:BundleStart
    if (-not $b -or -not $b.Process -or $b.Done) { return }
    $want = @($b.Todo | Where-Object { $Names -contains $_.Name })
    if (-not $want) { return }
    while ($want | Where-Object { -not (Test-Path (Join-Path $b.DoneDir "$($_.Name).json")) }) {
        if ($b.Process.HasExited) { break }
        Start-Sleep -Milliseconds 100
    }
    foreach ($t in $want) {
        if ($t.Bin -ne '-') { $env:Path = "$(if ($t.Bin -eq '.') { $t.Dir } else { Join-Path $t.Dir $t.Bin });$env:Path" }
    }
    Write-Log ("    {0} from the bundle, {1:0.0} s after it started" -f ($Names -join ', '), $b.Clock.Elapsed.TotalSeconds)
}

function Complete-ToolBundle {
    $b = $script:BundleStart
    if (-not $b -or $b.Done) { return }
    $b.Done = $true
    if (-not $b.Tools) { return }
    Write-Step 'tools (the bundle)'
    if ($b.Process) {
        # Each tool says when it's done (a file in DoneDir). The ones marked later aren't waited for:
        # they finish in the background, already on the PATH.
        $wait = @($b.Todo | Where-Object { -not $_.Later })
        while ($wait | Where-Object { -not (Test-Path (Join-Path $b.DoneDir "$($_.Name).json")) }) {
            if ($b.Process.HasExited) { break }
            Start-Sleep -Milliseconds 100
        }
        foreach ($t in $b.Todo) {
            $file = Join-Path $b.DoneDir "$($t.Name).json"
            $r = if (Test-Path $file) { Get-Content $file -Raw | ConvertFrom-Json }
            if ($r -and $r.Ok) { $script:BundleHave += $t; Set-ToolItem $t 'done'; Write-Log "    unpacked $($t.Name) in $($r.Seconds) s" }
            elseif (-not $r -and $t.Later -and -not $b.Process.HasExited) { $script:BundleHave += $t; Set-ToolItem $t 'done'; Write-Note "$($t.Name) finishes unpacking in the background" }
            else { Write-Warn "$($t.Name): $(if ($r) { $r.Error } else { 'the unpacking stopped (exit ' + $b.Process.ExitCode + ')' })"; Set-ToolItem $t 'fail' }
        }
        Write-Log ("    waited for the bundle until {0:0.0} s after it started" -f $b.Clock.Elapsed.TotalSeconds)
        if ($b.Process.HasExited) { Remove-Item $b.DoneDir -Recurse -Force -ErrorAction SilentlyContinue }
    }
    foreach ($t in $script:BundleHave) { Set-PackageSource $t.Name 'bundle' $t.Name $t.Version $t.Dir }
    $bins = @($script:BundleHave | Where-Object { $_.Bin -ne '-' } | ForEach-Object { if ($_.Bin -eq '.') { $_.Dir } else { Join-Path $_.Dir $_.Bin } })
    if ($bins) {
        $added = Add-UserPaths $bins
        if ($added) { Write-Note "$added tool folder(s) on the PATH" }
        Update-SessionPath
    }
    Write-Note "$(@($script:BundleHave).Count) of $(@($b.Tools).Count) tool(s) from the bundle, $(@($script:BundleTheirs).Count) yours already"
}

# The helpers step's downloads (omawin.exe and the bar's helpers, the menu app, the music player:
# bin/install-binaries.ps1) start with the bundle too, in a PowerShell of their own; the helpers
# step collects them (install/helpers.ps1).
function Start-EarlyHelpers {
    if ($script:EarlyHelpers) { return }
    $log = Join-Path $env:TEMP "omawin-helpers-$Stamp.log"
    $p = Start-Process (Join-Path $PSHOME 'pwsh.exe') -WindowStyle Hidden -PassThru -RedirectStandardOutput $log -RedirectStandardError "$log.err" `
        -ArgumentList '-NoLogo', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$Repo\bin\install-binaries.ps1`"", '-What', 'all'
    $null = $p.Handle  # read now, so its ExitCode is there later
    $script:EarlyHelpers = @{ Process = $p; Log = $log }
}

# Both halves at once (omawin pkg update).
function Install-ToolBundle($Installed, [string[]]$Only) { Start-ToolBundle $Installed $Only; Complete-ToolBundle }

function Install-WingetPackages([string[]]$Ids) {
    $bundled = @(Get-BundleCovered)
    # Everything left came from the bundle: winget isn't asked anything (nor looked for).
    if (-not @($Ids | Where-Object { $bundled -notcontains $_ })) { return }
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        Write-Warn 'winget not found. Install "App Installer" from the Microsoft Store, then re-run.'
        return
    }
    $winget = (Get-Command winget).Source
    $installed = Get-WingetInstalled
    Register-WingetSource

    $todo = New-Object Collections.Generic.List[string]
    foreach ($id in $Ids) {
        if ($bundled -contains $id) { continue }
        if (Test-PackageInstalled $id $installed) {
            Set-BoardPackage $id 'have'; Write-Ok $id
            # winget's own list has it: updates go through winget. Otherwise it's yours from elsewhere.
            if ($installed -and $installed.ContainsKey($id)) { Set-PackageSource $id 'winget' $id } elseif (-not (Get-PackageSources)[$id]) { Set-PackageSource $id 'found' $id }
        }
        else { Set-BoardPackage $id 'wait'; $todo.Add($id) }
    }

    # The whole-PC ones the admin step took (install/admin.ps1) install there, alongside these.
    $admin = @()
    if ($AdminBatch -and $AdminBatch.Plan.Packages) { $admin = @($todo | Where-Object { $AdminBatch.Plan.Packages -contains $_ }) }
    foreach ($id in $admin) { Set-BoardPackage $id 'wait' -Admin }
    $mine = @($todo | Where-Object { $admin -notcontains $_ })
    $seen = @{}
    $tick = {
        if (-not $admin) { return }
        Update-AdminStatus
        foreach ($id in $admin) {
            $state = Get-AdminState $id
            if ($seen[$id] -eq $state) { continue }
            $seen[$id] = $state
            switch ($state) {
                'run' { Set-BoardPackage $id 'run' -Admin }
                'done' { Set-BoardPackage $id 'done' -Admin; Write-Log "    done  $id (admin step)"; Set-PackageSource $id 'winget' $id }
            }
        }
        Set-BoardPackageNote $(if ($admin) { "$(@($admin | Where-Object { (Get-AdminState $_) -in 'wait', 'run' }).Count) left in the admin step" })
    }  # run from Invoke-WingetBatch and Wait-AdminItems, called from here: $admin and $seen are in reach

    # Several at once: downloads and portable tools overlap. Windows runs one MSI installer at a
    # time, so one that finds another in progress fails; anything that failed gets one more go at
    # the end, alone, before it counts as failed.
    $failed = @(Invoke-WingetBatch $winget $mine 4 $tick)
    if ($admin) { Wait-AdminItems $admin $tick }
    # The admin step couldn't: this account installs them for itself where they can be (above).
    foreach ($id in $admin) {
        if ((Get-AdminState $id) -ne 'done') {
            Write-Log "    admin step: $id $(Get-AdminState $id) $(Get-AdminText $id)"
            $failed += [pscustomobject]@{ Id = $id; ExitCode = 'in the admin step'; Output = (Get-AdminText $id) }
        }
    }
    Set-BoardPackageNote $null
    # The second go is for what failed by bad luck (an MSI already running). "No installer that
    # fits" (-1978335216: this package has no per-user one, and admin was declined) won't change.
    $again = @($failed | Where-Object { $_.ExitCode -ne -1978335216 })
    if ($again) { $failed = @(@($failed | Where-Object { $_.ExitCode -eq -1978335216 }) + @(Invoke-WingetBatch $winget @($again | ForEach-Object Id) 1)) }
    foreach ($f in $failed) {
        Set-BoardPackage $f.Id 'fail'
        if ($env:OMAWIN_NO_ADMIN -and $WholePC -contains $f.Id) { Write-Warn "$($f.Id) installs for the whole PC only, which takes admin: run install.ps1 again and approve its one prompt" }
        else { Write-Warn "winget install $($f.Id) exited with $($f.ExitCode)"; Write-OutputTail $f.Output }
    }
    Write-OkCount
    Update-SessionPath
    # (Not the bundle's tools: they go on the PATH when it's collected, and looking for one before
    # that searched Program Files for nothing.)
    Add-ToolPaths @(Get-WantedTools | Where-Object { @(Read-ToolBundle | ForEach-Object Name) -notcontains $_ })
}

# The commands the core and the chosen presets are for (install/packages.ps1's Tools); bun is the core's.
function Get-WantedTools {
    @('bun') + @(foreach ($name in Get-ChosenPresets) { $Presets[$name].Tools }) |
        Where-Object { $_ } | Select-Object -Unique
}

# A tool its package put off the PATH (bun's winget package in its own folder; shinchiro.mpv's
# installer in Program Files\MPV Player, for every account) is found where installers put things,
# and that folder goes on this account's PATH. Runs on every install and update, so a package that
# changes where it installs is caught on PCs that already have it. Nothing to do: no search.
function Add-ToolPaths([string[]]$Tools) {
    $added = $false
    foreach ($tool in $Tools) {
        if (Get-Command $tool -ErrorAction SilentlyContinue) { continue }
        $exe = @(
            foreach ($root in $env:ProgramFiles, ${env:ProgramFiles(x86)}, "$env:LOCALAPPDATA\Programs") {
                if ($root -and (Test-Path $root)) { Get-ChildItem $root -Filter "$tool.exe" -Recurse -Depth 2 -ErrorAction SilentlyContinue }
            }
            Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages" -Filter "$tool.exe" -Recurse -Depth 3 -ErrorAction SilentlyContinue
        ) | Select-Object -First 1
        if ($exe -and (Add-UserPath $exe.DirectoryName)) { Write-Note "$tool on the PATH: $($exe.DirectoryName)"; $added = $true }
    }
    if ($added) { Update-SessionPath }
}

# The core's own installers (herdr's) don't wait for winget or the bundle: each starts at the start
# of the install (install.ps1) in a PowerShell of its own, and Install-Extras collects it.
if (-not $EarlyExtras) { $EarlyExtras = @{} }   # kept when the software step loads this file again
function Start-EarlyExtras([string[]]$Names) {
    foreach ($name in $Names) {
        if ($EarlyExtras.ContainsKey($name)) { continue }   # started already (install.ps1, at the start)
        if (Get-Command $name -ErrorAction SilentlyContinue) { continue }
        $log = Join-Path $env:TEMP "omawin-extra-$name-$Stamp.log"
        $code = "`$ErrorActionPreference = 'Stop'; & { $($ExtraInstalls[$name]) } *> '$log'"
        $p = Start-Process (Join-Path $PSHOME 'pwsh.exe') -ArgumentList '-NoLogo', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', $code -WindowStyle Hidden -PassThru
        $null = $p.Handle  # read now, so its ExitCode is there later
        $EarlyExtras[$name] = @{ Process = $p; Log = $log }
    }
}

function Install-Extras([string[]]$Names) {
    foreach ($name in $Names) { Add-InstallItem 'Tools' "tool:$name" $name 'wait' }
    foreach ($name in $Names) {
        if ($EarlyExtras[$name]) {
            $e = $EarlyExtras[$name]
            Add-InstallItem 'Tools' "tool:$name" $name 'run'
            $e.Process.WaitForExit()
            Get-Content $e.Log -ErrorAction SilentlyContinue | ForEach-Object { if ("$_".Trim()) { Write-Note "$_" } }
            Remove-Item $e.Log -Force -ErrorAction SilentlyContinue
            if ($e.Process.ExitCode -eq 0) { Add-InstallItem 'Tools' "tool:$name" $name 'done'; Set-PackageSource $name 'script' $name }
            else { Write-Warn "$name failed (exit $($e.Process.ExitCode))"; Add-InstallItem 'Tools' "tool:$name" $name 'fail' }
            Update-SessionPath
            continue
        }
        if (Get-Command $name -ErrorAction SilentlyContinue) { Write-Note "ok  $name"; Add-InstallItem 'Tools' "tool:$name" $name 'have'; continue }
        Write-Note "installing $name"
        Add-InstallItem 'Tools' "tool:$name" $name 'run'
        try { & $ExtraInstalls[$name] | ForEach-Object { if ("$_".Trim()) { Write-Note "$_" } }; Add-InstallItem 'Tools' "tool:$name" $name 'done'; Set-PackageSource $name 'script' $name }
        catch { Write-Warn "$name failed: $_"; Add-InstallItem 'Tools' "tool:$name" $name 'fail' }
        Update-SessionPath
    }
}

# The presets this PC picked, minus names install/packages.ps1 no longer has (said once, not fatal).
function Get-ChosenPresets {
    foreach ($name in Get-Presets) {
        if ($Presets.Contains($name)) { $name } else { Write-Warn "preset '$name' (in $PresetsFile) doesn't exist any more; omawin preset remove $name" }
    }
}

# Every winget id this install wants: the core and the chosen presets'.
function Get-WantedPackages {
    @($WingetPackages) + @(foreach ($name in Get-ChosenPresets) { $Presets[$name].Winget }) | Where-Object { $_ } | Select-Object -Unique
}

# First install: no presets file yet. Ask which presets and which Linux for WSL (a person is
# there), else start with none. install.ps1 asks this before anything starts, with its other
# questions, so the one admin prompt can include WSL.
function Initialize-Presets {
    if (Test-Path $PresetsFile) { return }
    $picked = @()
    if (Test-Interactive) {
        Write-Note 'Presets: opt-in groups on top of the core'
        foreach ($name in $Presets.Keys) { if ($name -ne 'wsl') { Write-Note ("  {0,-15} {1}" -f $name, $Presets[$name].Summary) } }
        $answer = Read-InstallAnswer 'Which presets? (names separated by spaces, Enter for none; omawin preset add <name> later)'
        $picked = @($answer -split '[\s,]+' | Where-Object { $_ } | ForEach-Object {
                if ($Presets.Contains($_)) { $_ } else { Write-Warn "no preset '$_'" } })
        $picked = @($picked | Where-Object { $_ -ne 'wsl' })
        # Linux in WSL (the wsl preset): Arch unless this account already has one of the others.
        $choices = @(Get-LinuxChoices)
        $have = @(Get-WslDistros)
        $default = $choices | Where-Object { $have -contains $_.Distro } | Select-Object -First 1
        if (-not $default) { $default = $choices[0] }
        $names = @($choices | ForEach-Object Name) + 'none'
        $answer = (Read-InstallAnswer "Linux in WSL: $($names -join ', ')? (Enter for $($default.Name))").Trim().ToLower()
        if (-not $answer) { $answer = $default.Name }
        $linux = $choices | Where-Object Name -eq $answer | Select-Object -First 1
        if ($linux) {
            $picked += 'wsl'
            New-Item -ItemType Directory -Force -Path (Split-Path $LinuxPickFile) | Out-Null
            Set-Content $LinuxPickFile $linux.Name -NoNewline
            Write-Note "Linux: $($linux.Label)$(if (-not (Test-WslInstalled)) { ' (WSL goes in with the admin prompt; then a restart)' })"
        } elseif ($answer -ne 'none') { Write-Warn "no Linux '$answer': none for now (omawin preset add wsl later)" }
    }
    New-Item -ItemType Directory -Force -Path (Split-Path $PresetsFile) | Out-Null
    Set-Content $PresetsFile (@('# omawin presets on this PC (install/packages.ps1); omawin preset add|remove <name>') + $picked) -Encoding utf8
    Write-Note "presets: $(if ($picked) { $picked -join ', ' } else { 'none (omawin preset list)' })"
}

function Install-NerdFont {
    Write-Step "$NerdFont Nerd Font"
    $fontKey = 'HKCU:\Software\Microsoft\Windows NT\CurrentVersion\Fonts'
    $installed = (Get-ItemProperty $fontKey -ErrorAction SilentlyContinue).PSObject.Properties.Name |
        Where-Object { $_ -like "$NerdFont*Nerd Font*" -or $_ -like "$NerdFont NF*" }
    $machine = Get-ChildItem "$env:WINDIR\Fonts" -Filter "$($NerdFont)NerdFont*" -ErrorAction SilentlyContinue
    $font = "$NerdFont Nerd Font"
    if ($installed -or $machine) { Write-Note 'ok  already installed'; Add-InstallItem 'Tools' 'font' $font 'have'; return }
    Add-InstallItem 'Tools' 'font' $font 'run'

    $tmp = Join-Path $env:TEMP "nerdfont-$Stamp"
    New-Item -ItemType Directory -Force -Path $tmp | Out-Null
    $zip = Join-Path $tmp "$NerdFont.zip"
    # The tools bundle brought it (install/tools.txt, font): its files, no download.
    $fromBundle = Join-Path $env:LOCALAPPDATA "omawin\fonts\$NerdFont"
    if (Test-Path (Join-Path $fromBundle '.omawin-version')) { $tmp = $fromBundle }
    else {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor 3072
    Save-Download "https://github.com/ryanoasis/nerd-fonts/releases/latest/download/$NerdFont.zip" $zip
    & "$env:WINDIR\System32\tar.exe" -xf $zip -C $tmp
    if ($LASTEXITCODE -ne 0) { Expand-Archive $zip -DestinationPath $tmp -Force }
    }

    # Per-user install: no admin needed. A new account has no per-user font key until it's made.
    $fontDir = "$env:LOCALAPPDATA\Microsoft\Windows\Fonts"
    New-Item -ItemType Directory -Force -Path $fontDir | Out-Null
    if (-not (Test-Path $fontKey)) { New-Item -Path $fontKey -Force | Out-Null }
    Add-Type -AssemblyName System.Drawing
    foreach ($ttf in Get-ChildItem $tmp -Filter '*.ttf') {
        $dest = Join-Path $fontDir $ttf.Name
        Copy-Item $ttf.FullName $dest -Force
        $fc = New-Object System.Drawing.Text.PrivateFontCollection
        $fc.AddFontFile($dest)
        $face = $fc.Families[0].Name
        $style = [IO.Path]::GetFileNameWithoutExtension($ttf.Name) -replace '^.*-', ''
        New-ItemProperty -Path $fontKey -Name "$face $style (TrueType)" -Value $dest -PropertyType String -Force | Out-Null
        $fc.Dispose()
    }
    if ($tmp -ne $fromBundle) { Remove-Item $tmp -Recurse -Force }   # the bundle's copy stays: it's what omawin pkg update checks
    Write-Note 'installed (restart Windows Terminal to pick it up)'
    Add-InstallItem 'Tools' 'font' $font 'done'
}

if (-not $SoftwareFunctionsOnly) {
    if (-not $LinkOnly) {
        Start-EarlyExtras @($CoreExtras)
        Start-ToolBundle (Get-WingetInstalled)   # a no-op when install.ps1 already started it
        Write-Step 'winget packages'
        Initialize-Presets
        $chosen = @(Get-ChosenPresets)
        Install-WingetPackages @(Get-WantedPackages)
        Write-Step 'other tools'
        $extras = @($CoreExtras) + @(foreach ($name in $chosen) { $Presets[$name].Extras })
        Install-Extras @($extras | Where-Object { $_ } | Select-Object -Unique)
        # The bundle keeps unpacking through the next steps: the shell, desktop and helpers steps
        # wait for the tools they need (Wait-BundleTools), the tools step collects the rest and
        # puts in the font (install/tools.ps1). Run alone (-Step software), it's all collected here.
        if ($Step) { Complete-ToolBundle; Install-NerdFont }
    } else {
        # An update installs nothing, but a tool a package moved off the PATH still goes back on.
        Add-ToolPaths @(Get-WantedTools)
    }

    Write-Step 'Neovim and Neovide'
    Set-Junction "$env:LOCALAPPDATA\nvim" "$Repo\config\nvim"
    # Neovide uses the same Neovim config; this is only its own settings (config.toml).
    Set-Junction "$env:APPDATA\neovide" "$Repo\config\neovide"

    Write-Step 'R / RStudio'
    # A one-line ~/.Rprofile (R's ~ is Documents) sourcing config/r/rprofile.R: RStudio follows the
    # desktop theme. An .Rprofile of your own (more than that one line) is left alone.
    $rprofile = Join-Path ([Environment]::GetFolderPath('MyDocuments')) '.Rprofile'
    $source = "source('$("$Repo\config\r\rprofile.R" -replace '\\', '/')')"
    $current = if (Test-Path $rprofile) { (Get-Content $rprofile -Raw).Trim() }
    if ($current -eq $source) { Write-Note "ok  $rprofile" }
    elseif (-not $current -or $current -match '^source\(.*rprofile\.R.*\)$') { Set-Content $rprofile $source -Encoding utf8; Write-Note "wrote $rprofile" }
    else { Write-Note "$rprofile is yours; add this line to it for themes: $source" }
}
