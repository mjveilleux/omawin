# install.ps1 step: the desktop, now (roadmap 2.2): GlazeWM, which starts the bar and the menu
# app, so you're tiling windows the moment the install ends instead of signing out and back in.
# Dot-sourced by install.ps1, so its functions (install/lib.ps1) and $Repo are in scope.
#
# It runs the sign-in task the desktop step registered (install/login-start.ps1) rather than
# GlazeWM itself: the task starts exactly what a sign-in starts (omawin.exe start glazewm: the
# menu app, then GlazeWM once Explorer's taskbar is up), and it runs on its own, not as a child of
# this console, so closing the install's window doesn't take the desktop with it.

Write-Step 'the desktop'
$session = (Get-Process -Id $PID).SessionId
function Get-InSession([string]$Name) { @(Get-Process $Name -ErrorAction SilentlyContinue | Where-Object SessionId -eq $session) }
$UI.DesktopUp = $false

if ($LinkOnly) {
    # omawin update: the desktop is yours as it is; its parts restart themselves where they need to.
    Write-Note 'skipped: an update leaves the running desktop as it is'
} elseif (Get-InSession glazewm) {
    Write-Note 'ok  GlazeWM is already running'
    Add-InstallItem 'Desktop' 'desktop-up' 'the desktop' 'have'
    $UI.DesktopUp = $true
} elseif (-not (Get-InSession explorer)) {
    # No Windows desktop in this session (a CI runner, a remote shell, a scheduled run): nothing to
    # start the desktop on. It starts at the next sign-in, from the task.
    Write-Note 'no Windows desktop in this session: it starts at your next sign-in'
} else {
    $task = "omawin GlazeWM ($([Environment]::UserName))"
    try {
        if (-not (Get-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue)) { throw "no sign-in task '$task' (install.ps1 -Step desktop makes it)" }
        Add-InstallItem 'Desktop' 'desktop-up' 'the desktop' 'run'
        # The install's own window floats over the desktop as it comes up, centred, like the
        # system menu's jobs (config/glazewm/config.yaml's rule for 'menu-task'), rather than being
        # tiled full screen as GlazeWM's first window.
        # Its own title comes back when the install ends (Close-InstallUI); the window stays floating.
        try { $UI.Title = [Console]::Title; [Console]::Title = 'menu-task' } catch { }
        Start-ScheduledTask -TaskName $task
        # GlazeWM, then the menu app (which draws the bar): seconds at most on a PC whose
        # Explorer is already up. Not waited on for ever: the task keeps trying on its own.
        $clock = [Diagnostics.Stopwatch]::StartNew()
        while ($clock.Elapsed.TotalSeconds -lt 20 -and -not ((Get-InSession glazewm) -and (Get-InSession SystemMenu))) { Start-Sleep -Milliseconds 200 }
        $wm = [bool](Get-InSession glazewm); $menu = [bool](Get-InSession SystemMenu)
        Write-Log ("    desktop: GlazeWM {0}, the menu app {1}, after {2:0.0} s" -f $wm, $menu, $clock.Elapsed.TotalSeconds)
        if ($wm) {
            $UI.DesktopUp = $true
            Write-Note ("GlazeWM is running{0} ({1:0.0} s)" -f $(if ($menu) { ', the bar and the launcher too' } else { '; the bar follows in a moment' }), $clock.Elapsed.TotalSeconds)
            Add-InstallItem 'Desktop' 'desktop-up' 'the desktop' 'done'
        } else {
            Write-Warn "GlazeWM didn't start within 20 s: it starts at your next sign-in (or: omawin start glazewm)"
            Add-InstallItem 'Desktop' 'desktop-up' 'the desktop' 'fail'
        }
    } catch {
        Write-Warn "the desktop didn't start: $($_.Exception.Message); it starts at your next sign-in"
        Add-InstallItem 'Desktop' 'desktop-up' 'the desktop' 'fail'
    }
}
