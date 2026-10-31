// omawin-view.exe (roadmap 8.25): what Windows starts when you open a file of a type you gave
// omawin (`omawin open-with`): a picture, Markdown, JSON, a log. It hands the file to the menu app,
// which is always running, and exits: the menu app's viewer (shell/menu-app/ViewerWindow.cs) shows
// it, so opening a file costs this tiny exe and nothing else (docs/shell-apps.md, decision 6).
//
// The hand-off is the one every request to the menu app takes (bin/omawin.cs Send): the request
// written to %LOCALAPPDATA%\launcher\system-menu-request, then its event set. A request is words
// split on spaces, so the path goes escaped (%20), as one word. With the menu app not running, it's
// started with the request; with no menu app at all, Windows' "Open with" chooser opens instead
// (opening the file the usual way would come straight back here).
//
// A windowless exe compiled by the C# compiler that ships with Windows (C# 5), by build/helpers.ps1.
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

static class View
{
    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);

    static readonly string StateDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "launcher");

    static int Main(string[] args)
    {
        if (args.Length == 0) return 2;
        string file;
        try { file = Path.GetFullPath(args[0]); }
        catch (Exception) { return 2; }
        string request = "--view " + Uri.EscapeDataString(file);

        EventWaitHandle signal;
        if (EventWaitHandle.TryOpenExisting(@"Local\dotfiles-system-menu-signal", out signal))
        {
            using (signal)
            {
                string requestFile = Path.Combine(StateDir, "system-menu-request");
                // Several files opened at once each start one of these: wait (briefly) for the one
                // before to be picked up, so no request overwrites another.
                for (int i = 0; i < 40 && File.Exists(requestFile); i++) Thread.Sleep(25);
                Directory.CreateDirectory(StateDir);
                File.WriteAllText(requestFile, request);
                AllowSetForegroundWindow(-1); // the viewer comes to the front
                signal.Set();
            }
            return 0;
        }
        string exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "system-menu", "SystemMenu.exe");
        try
        {
            if (File.Exists(exe)) Process.Start(new ProcessStartInfo(exe, request) { UseShellExecute = false });
            else Process.Start(new ProcessStartInfo("rundll32.exe", "shell32.dll,OpenAs_RunDLL " + file) { UseShellExecute = false });
            return 0;
        }
        catch (Exception) { return 1; }
    }
}
