<#
.SYNOPSIS
    One command for a brand-new Windows PC: get omawin and run install.ps1.

.DESCRIPTION
    Meant to be run straight from the web, like `curl ... | bash` on Linux. From Windows
    PowerShell:

        irm https://raw.githubusercontent.com/mjveilleux/omawin/main/bootstrap.ps1 | iex

    It needs nothing installed first:

      1. installs git with winget
      2. clones omawin to ~/omawin (or pulls it if it's already there)
      3. your own settings (docs/work.md): the PC's shared folder (C:\ProgramData\omawin\shared,
         yours in every account; found if another account made it, else started empty or from
         a private backup repo) and this account's (~/omawin-account, from a private repo or
         started fresh). A private repo opens Git's sign-in window.
      4. PowerShell 7, from the release's tools bundle (downloaded while git clones)
      5. runs install.ps1 in PowerShell 7, which sets up everything else

    Safe to run again. Runs under Windows PowerShell 5.1, which every Windows PC has: the only part
    of omawin that does. It gets PowerShell 7 and hands everything after it to that.
    OMAWIN_REPO=<owner>/<name> installs a fork; OMAWIN_LOCAL=1 skips the clone and uses the copy
    already at ~/omawin (testing in a VM).
#>
$ErrorActionPreference = 'Stop'

# Never as administrator: in a standard account, "Run as administrator" with another account's
# password runs everything as that other account (its home folder, its settings), and the
# desktop it starts can't work in this one. The steps that need admin ask for it themselves.
if (([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host ''
    Write-Host "  This PowerShell runs as administrator ($env:USERNAME). Close it, open PowerShell" -ForegroundColor Yellow
    Write-Host '  normally (Start, type PowerShell, Enter), and run the command again. The steps' -ForegroundColor Yellow
    Write-Host '  that need admin ask for it on their own.' -ForegroundColor Yellow
    return
}

$Repo = if ($env:OMAWIN_REPO) { $env:OMAWIN_REPO } else { 'mjveilleux/omawin' }
$Target = "$HOME\omawin"
$Local = $env:OMAWIN_LOCAL -eq '1' -or $env:DOTFILES_LOCAL -eq '1'

# A new PC only runs signed scripts; install.ps1 and the PowerShell profile are local files.
Set-ExecutionPolicy -Scope Process Bypass -Force
if ((Get-ExecutionPolicy -Scope CurrentUser) -notin 'RemoteSigned', 'Unrestricted', 'Bypass') {
    # The Process scope set just above is more specific, so PowerShell saves this one and then
    # reports "overridden by a policy defined at a more specific scope" as an error, which under
    # Stop would end the install. The setting is saved either way: that error is expected.
    try { Set-ExecutionPolicy -Scope CurrentUser RemoteSigned -Force }
    catch { if ((Get-ExecutionPolicy -Scope CurrentUser) -ne 'RemoteSigned') { throw } }
}

# How it looks: install.ps1's banner and step glyphs (install/ui.ps1) in a small copy, since this
# runs before there's a clone to load them from. Plain "==> step" lines when the output isn't a
# console, or OMAWIN_PLAIN=1 or NO_COLOR is set (OMAWIN_PLAIN=0 forces this look). ASCII only, glyphs from code points: Windows
# PowerShell may read this file in the ANSI code page.
$Fancy = $false
if ($env:OMAWIN_PLAIN -eq '0' -or ($env:OMAWIN_PLAIN -ne '1' -and -not $env:NO_COLOR -and -not [Console]::IsOutputRedirected -and $Host.Name -eq 'ConsoleHost')) {
    try {
        if (-not ('Omawin.Vt' -as [type])) {
            Add-Type -Namespace Omawin -Name Vt -MemberDefinition @'
[DllImport("kernel32.dll")] public static extern IntPtr GetStdHandle(int n);
[DllImport("kernel32.dll")] public static extern bool GetConsoleMode(IntPtr h, out uint m);
[DllImport("kernel32.dll")] public static extern bool SetConsoleMode(IntPtr h, uint m);
public static bool Enable() {
    IntPtr h = GetStdHandle(-11);
    uint m;
    if (!GetConsoleMode(h, out m)) return false;
    return (m & 4) != 0 || SetConsoleMode(h, m | 4);
}
'@
        }
        $Fancy = [Omawin.Vt]::Enable() -or $env:OMAWIN_PLAIN -eq '0'
    } catch { }
}
$e = [char]27
$Boot = @{ Accent = "$e[38;2;255;159;10m"; Green = "$e[38;2;48;209;88m"; Dim = "$e[38;2;140;140;150m"; Bold = "$e[1m"; Reset = "$e[0m" }
$rich = [bool]($env:WT_SESSION -or $env:TERM_PROGRAM -or $env:TERM)  # fonts with fallback (ui.ps1)
$Boot.Running = [string][char]0x25CF
$Boot.Done = if ($rich) { [string][char]0x2713 } else { [string][char]0x221A }
$Boot.Dot = [string][char]0x00B7
$Boot.Step = $null
if ($Fancy) {
    $Boot.Encoding = [Console]::OutputEncoding
    [Console]::OutputEncoding = New-Object Text.UTF8Encoding $false
    # "omawin" in figlet's ANSI Shadow, as in install/ui.ps1.
    $logo = @(
        @(' ######7 ', '##r===##7', '##|   ##|', '##|   ##|', 'L######rJ', ' L=====J '),
        @('###7   ###7', '####7 ####|', '##r####r##|', '##|L##rJ##|', '##| L=J ##|', 'L=J     L=J'),
        @(' #####7 ', '##r==##7', '#######|', '##r==##|', '##|  ##|', 'L=J  L=J'),
        @('##7    ##7', '##|    ##|', '##| #7 ##|', '##|###7##|', 'L###r###rJ', ' L==JL==J '),
        @('##7', '##|', '##|', '##|', '##|', 'L=J'),
        @('###7   ##7', '####7  ##|', '##r##7 ##|', '##|L##7##|', '##| L####|', 'L=J  L===J')
    )
    $map = @{ '#' = 0x2588; 'r' = 0x2554; '7' = 0x2557; 'L' = 0x255A; 'J' = 0x255D; '=' = 0x2550; '|' = 0x2551 }
    $shades = @('255;196;0', '255;168;10', '255;140;30', '255;112;50', '255;84;70', '255;58;92')
    [Console]::Out.WriteLine('')
    for ($row = 0; $row -lt 6; $row++) {
        $line = -join ($logo | ForEach-Object { $_[$row] })
        $line = -join ($line.ToCharArray() | ForEach-Object { if ($map.ContainsKey([string]$_)) { [string][char]$map[[string]$_] } else { [string]$_ } })
        [Console]::Out.WriteLine("  $e[38;2;$($shades[$row])m$line$($Boot.Reset)")
    }
    [Console]::Out.WriteLine('')
    $d = " $($Boot.Dot) "
    [Console]::Out.WriteLine("  $($Boot.Bold)Omakase Windows$($Boot.Reset)$($Boot.Dim)$($d)keyboard-driven developer desktop$($d)bootstrap$($Boot.Reset)")
    [Console]::Out.WriteLine("  $($Boot.Dim)winget$($d)git$($d)omawin$($d)your settings$($d)install.ps1$($Boot.Reset)")
}

# Each step's line; the next one (or the end) marks the one before done, with its time.
function Complete-Step {
    if (-not $Boot.Step) { return }
    $took = [int]((Get-Date) - $Boot.Started).TotalSeconds
    [Console]::Out.WriteLine("  $($Boot.Green)$($Boot.Done)$($Boot.Reset) $($Boot.Step.PadRight(28)) $($Boot.Dim)$($took)s$($Boot.Reset)")
    $Boot.Step = $null
}
function Write-Step([string]$Text) {
    if (-not $Fancy) { Write-Host "`n==> $Text" -ForegroundColor Cyan; return }
    Complete-Step
    $Boot.Step = $Text
    $Boot.Started = Get-Date
    [Console]::Out.WriteLine("`n  $($Boot.Accent)$($Boot.Running)$($Boot.Reset) $($Boot.Bold)$Text$($Boot.Reset)")
}
function Write-Note([string]$Text) {
    if (-not $Fancy) { Write-Host "    $Text"; return }
    [Console]::Out.WriteLine("      $($Boot.Dim)$Text$($Boot.Reset)")
}
function Update-SessionPath {
    $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' +
        [Environment]::GetEnvironmentVariable('Path', 'User')
}

Write-Step 'winget'
if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
    # New PCs get winget (App Installer) from the Store shortly after first sign-in; nudge it.
    Write-Note 'winget is not ready yet; asking Windows to register App Installer ...'
    try { Add-AppxPackage -RegisterByFamilyName -MainPackage Microsoft.DesktopAppInstaller_8wekyb3d8bbwe } catch { }
    Update-SessionPath
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        throw 'winget is missing. Update "App Installer" in the Microsoft Store, then run this again.'
    }
}

# PowerShell 7 runs everything after this script (install.ps1 needs it). Taken from the release's
# tools bundle (bundle/tools.txt): per-user, no admin, checked against the release's SHA256SUMS.
# Its download starts now and runs while git gets the repo.
Write-Step 'PowerShell 7'
$Pwsh = $null
$pwshFound = Get-Command pwsh -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
if ($pwshFound -and $pwshFound.Version.Major -ge 7) { $Pwsh = $pwshFound.Source; Write-Note "ok  $Pwsh" }
$PwshFetch = $null
if (-not $Pwsh) {
    $releaseBase = if ($env:OMAWIN_RELEASE_URL) { $env:OMAWIN_RELEASE_URL.TrimEnd('/', '\') }
                   elseif ($env:OMAWIN_RELEASE -and $env:OMAWIN_RELEASE -ne 'latest') { "https://github.com/$Repo/releases/download/$env:OMAWIN_RELEASE" }
                   else { "https://github.com/$Repo/releases/latest/download" }
    $curl = "$env:WINDIR\System32\curl.exe"
    $work = Join-Path $env:TEMP "omawin-bootstrap-$PID"
    New-Item -ItemType Directory -Force $work | Out-Null
    function Get-ReleaseFile([string]$Name, [switch]$Background) {
        if ($releaseBase -notmatch '^[a-z]+://') { return @{ Path = Join-Path $releaseBase $Name; Process = $null } }
        $out = Join-Path $work $Name
        $p = Start-Process $curl -ArgumentList "-fsSL --retry 3 -o `"$out`" `"$releaseBase/$Name`"" -NoNewWindow -PassThru
        $null = $p.Handle
        if (-not $Background) { $p.WaitForExit() }
        @{ Path = $out; Process = $p }
    }
    $sums = Get-ReleaseFile 'SHA256SUMS.txt'
    $list = Get-ReleaseFile 'omawin-tools.txt'
    $row = $null
    if ((Test-Path $sums.Path) -and (Test-Path $list.Path)) {
        $want = (Get-Content $sums.Path | Where-Object { $_ -match '^([0-9a-fA-F]{64})\s+\*?omawin-tools\.txt$' } | ForEach-Object { $Matches[1] }) | Select-Object -First 1
        if ($want -and (Get-FileHash $list.Path -Algorithm SHA256).Hash -eq $want.ToUpper()) {
            foreach ($line in Get-Content $list.Path) { $f = $line -split "`t"; if ($f[0] -eq 'pwsh' -and $f.Count -ge 4) { $row = $f; break } }
        }
    }
    if ($row) { $PwshFetch = @{ Row = $row; File = Get-ReleaseFile $row[2] -Background } }
    else { Write-Note 'this release has no tools bundle: PowerShell 7 comes through winget' }
}

Write-Step 'git'
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    winget install --id Git.Git --exact --silent --accept-source-agreements --accept-package-agreements
    Update-SessionPath
} else { Write-Note 'ok  git' }

if ($Local) {
    Write-Step "using the copy at $Target (OMAWIN_LOCAL=1)"
} else {
    Write-Step "get $Repo"
    if (Test-Path "$Target\.git") {
        # omawin follows main: a checkout left on a branch that's all in main goes back to it
        # (bin/update.ps1 does the same), or the pull would only ever get that branch.
        git -C $Target fetch --quiet origin main
        $branch = git -C $Target symbolic-ref --quiet --short HEAD
        if ($branch -ne 'main' -and -not (git -C $Target cherry origin/main HEAD | Where-Object { $_ -like '+*' })) {
            git -C $Target switch --quiet main
        }
        git -C $Target pull --ff-only
    }
    else { git clone "https://github.com/$Repo.git" $Target }
    if ($LASTEXITCODE -ne 0) { throw "couldn't get $Repo" }

    # Your own settings (docs/work.md), before install so it uses them. Two folders: the PC's
    # shared one (yours in every account) and this account's. Both optional: Enter starts empty.
    Write-Step 'your settings'
    function Get-Answer([string]$Question) {
        $answer = (Read-Host "    $Question").Trim()
        if (-not $answer) { return $null }
        if ($answer -match '^(https?|git@|ssh)') { $answer } else { "https://github.com/$answer.git" }
    }
    $shared = & "$Target\bin\user-layer.ps1" -Shared
    if ($shared -eq "$HOME\omawin-me") {
        Write-Note "shared: your old $shared; install moves it to the PC's shared folder"
    } elseif (Test-Path $shared) {
        Write-Note "shared: $shared, already on this PC; this account uses it too"
    } else {
        Write-Note 'Shared settings are what''s yours in every Windows account on this PC (Claude rules and'
        Write-Note 'skills, launcher items). None here yet. If you backed them up to a private repo, they come from there.'
        $url = Get-Answer 'Shared settings backup (owner/name or a git URL; Enter: start empty)'
        try { if ($url) { & "$Target\bin\shared.ps1" -From $url } else { & "$Target\bin\shared.ps1" -Init } }
        catch { Write-Host "    $($_.Exception.Message); carrying on with an empty one" -ForegroundColor Yellow; & "$Target\bin\shared.ps1" -Init }
    }
    $account = & "$Target\bin\user-layer.ps1"
    if (Test-Path "$account\.git") {
        # (Windows PowerShell turns a native program's stderr into an error under 'Stop'.)
        try { git -C $account pull --ff-only -q 2>$null } catch { }
        Write-Note "this account: $account"
    } elseif (Test-Path $account) {
        Write-Note "this account: $account"
    } else {
        Write-Note 'This account''s own settings are who you are here: git name and email, the employer''s'
        Write-Note 'or client''s launcher items, shares. Keep them in a private repo? Install asks your git name otherwise.'
        $url = Get-Answer "This account's settings repo (owner/name or a git URL; Enter: start fresh)"
        if ($url) {
            git clone -q $url $account
            if ($LASTEXITCODE -ne 0) { Write-Host "    couldn't clone $url; carrying on without it" -ForegroundColor Yellow }
        }
    }
}

# PowerShell 7, now that its download has had the clone's time: checked, unpacked where the
# install's tools go (install/software.ps1 then sees it's there), and put on the PATH.
if (-not $Pwsh -and $PwshFetch) {
    $row = $PwshFetch.Row
    if ($PwshFetch.File.Process) { $PwshFetch.File.Process.WaitForExit() }
    $zip = $PwshFetch.File.Path
    if (-not (Test-Path $zip) -or (Get-FileHash $zip -Algorithm SHA256).Hash -ne $row[3].ToUpper()) { throw "PowerShell 7 from the release doesn't match its published SHA-256" }
    $dir = Join-Path $env:LOCALAPPDATA 'omawin\tools\pwsh'
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    New-Item -ItemType Directory -Force $dir | Out-Null
    & "$env:WINDIR\System32\tar.exe" -xf $zip -C $dir
    if ($LASTEXITCODE -ne 0) { throw "couldn't unpack PowerShell 7 ($LASTEXITCODE)" }
    Set-Content (Join-Path $dir '.omawin-version') "$($row[1]) $($row[3])"
    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    if (($userPath -split ';') -notcontains $dir) { [Environment]::SetEnvironmentVariable('Path', (@($userPath.TrimEnd(';'), $dir) | Where-Object { $_ }) -join ';', 'User') }
    $Pwsh = Join-Path $dir 'pwsh.exe'
    Write-Note "PowerShell $($row[1]) -> $dir"
}
if ($work) { Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue }
if (-not $Pwsh) {
    winget install --id Microsoft.PowerShell --exact --silent --accept-source-agreements --accept-package-agreements
    Update-SessionPath
    $pwshFound = Get-Command pwsh -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $pwshFound) { throw 'PowerShell 7 is missing and winget could not install it: install it (aka.ms/powershell), then run this again.' }
    $Pwsh = $pwshFound.Source
}

# install.ps1 shows the same look; it skips the logo, shown a moment ago. It runs in PowerShell 7.
if ($Fancy) { Complete-Step; $env:OMAWIN_BANNER = 'shown' } else { Write-Step 'install.ps1' }
try {
    & $Pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File "$Target\install.ps1"
    # A throw, not exit: run as `irm | iex`, exit would close your PowerShell window.
    if ($LASTEXITCODE) { throw "install.ps1 stopped (exit $LASTEXITCODE): its log is in $env:LOCALAPPDATA\launcher\install.log" }
}
finally {
    $env:OMAWIN_BANNER = $null
    if ($Fancy) { [Console]::OutputEncoding = $Boot.Encoding }
}
