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
      3. your layers, if you have them (docs/work.md), each asked for, Enter skips:
         this Windows account's user layer to ~/omawin-work, your shared one to ~/omawin-me.
         A private one opens Git's sign-in window.
      4. runs install.ps1, which sets up everything else

    Safe to run again. Runs under Windows PowerShell 5.1, which every Windows PC has.
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
    Set-ExecutionPolicy -Scope CurrentUser RemoteSigned -Force
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
    [Console]::Out.WriteLine("  $($Boot.Dim)winget$($d)git$($d)omawin$($d)your layers$($d)install.ps1$($Boot.Reset)")
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

Write-Step 'git'
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    winget install --id Git.Git --exact --silent --accept-source-agreements --accept-package-agreements
    Update-SessionPath
} else { Write-Note 'ok  git' }

if ($Local) {
    Write-Step "using the copy at $Target (OMAWIN_LOCAL=1)"
} else {
    Write-Step "get $Repo"
    if (Test-Path "$Target\.git") { git -C $Target pull --ff-only }
    else { git clone "https://github.com/$Repo.git" $Target }
    if ($LASTEXITCODE -ne 0) { throw "couldn't get $Repo" }

    # The layers (docs/work.md), before install so it uses them. Optional: Enter skips.
    function Get-Layer([string]$Path, [string]$Question) {
        if (Test-Path "$Path\.git") { git -C $Path pull --ff-only; return }
        if (Test-Path $Path) { return }
        $answer = (Read-Host "    $Question (owner/name or a git URL; Enter: none)").Trim()
        if (-not $answer) { return }
        $url = if ($answer -match '^(https?|git@|ssh)') { $answer } else { "https://github.com/$answer.git" }
        Write-Step "get $answer"
        git clone $url $Path
        if ($LASTEXITCODE -ne 0) { Write-Host "    couldn't clone $answer; carrying on without it" -ForegroundColor Yellow }
    }
    Get-Layer "$HOME\omawin-work" "This account's user layer repo, if you have one"
    Get-Layer "$HOME\omawin-me" 'Your shared layer repo, if you have one'
}

# install.ps1 shows the same look; it skips the logo, shown a moment ago.
if ($Fancy) { Complete-Step; $env:OMAWIN_BANNER = 'shown' } else { Write-Step 'install.ps1' }
try { & "$Target\install.ps1" }
finally {
    $env:OMAWIN_BANNER = $null
    if ($Fancy) { [Console]::OutputEncoding = $Boot.Encoding }
}
