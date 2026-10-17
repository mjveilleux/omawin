// theme-watch: tells the Zebar bar and dock when the theme changed, so they recolor at once
// without a restart and without polling (zebar/tokyo-islands/theme.js).
//
// Prints "changed" on stdout each time theme.css (next to the bar's pages; themes/render.ps1
// writes it on every theme switch) is written. Waits on Windows' directory change
// notifications, so it costs nothing in between.
//
// Built by install.ps1 into ~/.local/bin with the C# compiler that ships with Windows.
using System;
using System.IO;
using System.Threading;

static class ThemeWatch
{
    static void Main()
    {
        try { Run(); }
        catch (IOException) { } // stdout closed: whoever was reading is gone
    }

    static void Run()
    {
        // Where Zebar reads its packs from (a link to omawin's zebar folder).
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @".glzr\zebar\tokyo-islands");
        var changed = new AutoResetEvent(false);
        var watcher = new FileSystemWatcher(dir);
        watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size;
        FileSystemEventHandler onChange = (s, e) => { if (IsTheme(e.Name)) changed.Set(); };
        watcher.Changed += onChange;
        watcher.Created += onChange;
        // render.ps1 writes theme.css.tmp, then renames it to theme.css.
        watcher.Renamed += (s, e) => { if (IsTheme(e.Name)) changed.Set(); };
        watcher.EnableRaisingEvents = true;

        var output = Console.Out;
        while (true)
        {
            // Every 5 s, check Zebar is still there: it can't stop the programs it started (on
            // Windows), so this quits by itself once Zebar is gone.
            if (!changed.WaitOne(5000))
            {
                if (System.Diagnostics.Process.GetProcessesByName("zebar").Length == 0) return;
                continue;
            }
            // One write raises several events: wait for them to settle, then tell once.
            while (changed.WaitOne(30)) { }
            output.WriteLine("changed");
            output.Flush();
        }
    }

    static bool IsTheme(string name)
    {
        return string.Equals(name, "theme.css", StringComparison.OrdinalIgnoreCase);
    }
}
