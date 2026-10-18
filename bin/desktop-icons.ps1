<#
.SYNOPSIS
    omawin toggle desktop-icons: the desktop's icons shown or hidden. Hidden is omawin's default:
    the desktop is just the wallpaper (files belong in Documents; apps are in the launcher).

.DESCRIPTION
    Windows' own setting (right-click the desktop > View > Show desktop icons), so nothing is
    moved or deleted: files saved to the desktop are still there, in File Explorer. It applies at
    once, through the same command that menu sends Explorer, and is saved in HideIcons (HKCU,
    Explorer\Advanced) for the next sign-in.

        omawin toggle desktop-icons           shown <-> hidden
        omawin toggle desktop-icons hide|show
        omawin toggle desktop-icons status
        .\desktop-icons.ps1 -Default          install's: hidden, unless you chose to show them

    Your choice is kept (%LOCALAPPDATA%\launcher\desktop-icons), so an update doesn't hide them again.
    Runs under Windows PowerShell 5.1 too.
#>
param([ValidateSet('toggle', 'hide', 'show', 'status')][string]$Action = 'toggle', [switch]$Default)
$ErrorActionPreference = 'Stop'

$advanced = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'
$choice = Join-Path $env:LOCALAPPDATA 'launcher\desktop-icons'

if (-not ('Omawin.DesktopIcons' -as [type])) {
    Add-Type -Namespace Omawin -Name DesktopIcons -MemberDefinition @'
[DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
[DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
[DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
// The desktop's view: under Progman, or under a WorkerW once the wallpaper has been changed.
public static IntPtr View() {
    IntPtr view = FindWindowEx(FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Progman", null), IntPtr.Zero, "SHELLDLL_DefView", null);
    IntPtr worker = IntPtr.Zero;
    while (view == IntPtr.Zero && (worker = FindWindowEx(IntPtr.Zero, worker, "WorkerW", null)) != IntPtr.Zero)
        view = FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null);
    return view;
}
// -1: no desktop (Explorer isn't running); 1 shown; 0 hidden.
public static int Shown() {
    IntPtr view = View();
    if (view == IntPtr.Zero) return -1;
    IntPtr list = FindWindowEx(view, IntPtr.Zero, "SysListView32", null);
    return list != IntPtr.Zero && IsWindowVisible(list) ? 1 : 0;
}
// Explorer's own View > Show desktop icons (WM_COMMAND 0x7402): flips them, and saves HideIcons.
public static void Toggle() { SendMessage(View(), 0x0111, (IntPtr)0x7402, IntPtr.Zero); }
'@
}

function Get-Hidden {
    $live = [Omawin.DesktopIcons]::Shown()
    if ($live -ge 0) { return ($live -eq 0) }
    return ((Get-ItemProperty $advanced -Name HideIcons -ErrorAction SilentlyContinue).HideIcons -eq 1)
}

function Set-Hidden([bool]$Hide) {
    if ((Get-Hidden) -ne $Hide -and [Omawin.DesktopIcons]::Shown() -ge 0) { [Omawin.DesktopIcons]::Toggle() }
    # Saved either way: with no Explorer running, it applies at its next start.
    Set-ItemProperty $advanced -Name HideIcons -Value ([int]$Hide) -Type DWord
}

if ($Default) {
    $chose = if (Test-Path $choice) { (Get-Content $choice -Raw).Trim() }
    if ($chose -eq 'shown') { return 'ok  desktop icons shown (your choice: omawin toggle desktop-icons hide)' }
    if (Get-Hidden) { Set-Hidden $true; return 'ok  desktop icons hidden' }
    Set-Hidden $true
    return 'desktop icons hidden (omawin toggle desktop-icons show brings them back)'
}

if ($Action -eq 'status') { if (Get-Hidden) { 'hidden' } else { 'shown' }; return }
$hide = switch ($Action) { 'hide' { $true } 'show' { $false } default { -not (Get-Hidden) } }
Set-Hidden $hide
New-Item -ItemType Directory -Force (Split-Path $choice) | Out-Null
Set-Content -LiteralPath $choice -Value $(if ($hide) { 'hidden' } else { 'shown' }) -NoNewline
if ($hide) { 'desktop icons hidden' } else { 'desktop icons shown' }
