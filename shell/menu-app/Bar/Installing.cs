using System.Diagnostics;

namespace SystemMenu;

// Whether an install or update is running now, for the bar's glyph, and the toast when it ends
// (roadmap 2.2). install.ps1 writes InstallMark's two files; a watcher on the state folder sees
// them come and go (no polling), and the install's own process is waited on, so one that was
// closed or crashed (its file left behind) stops showing at once.
public static class Installing
{
    public static event Action? Changed;
    public static InstallMark.Running? Now { get; private set; }

    static FileSystemWatcher? watcher;
    static Process? waiting;
    static readonly object gate = new();

    public static void Start()
    {
        if (watcher != null) return;
        try
        {
            Directory.CreateDirectory(MenuModel.StateDir);
            watcher = new FileSystemWatcher(MenuModel.StateDir, "install-*.txt")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            watcher.Created += (_, e) => Seen(e.Name);
            watcher.Changed += (_, e) => Seen(e.Name);
            watcher.Deleted += (_, e) => Seen(e.Name);
            watcher.Renamed += (_, e) => Seen(e.Name);
        }
        catch (Exception ex) { App.Log(ex); }
        ReadRunning(); // one already running when the menu app starts (the install's start step starts it)
    }

    static void Seen(string? name)
    {
        if (name == InstallMark.RunningFile) ReadRunning();
        else if (name == InstallMark.DoneFile) ReadDone();
    }

    static void ReadRunning()
    {
        InstallMark.Running? now = null;
        var file = Path.Combine(MenuModel.StateDir, InstallMark.RunningFile);
        try { if (File.Exists(file)) now = InstallMark.ParseRunning(File.ReadAllLines(file)); }
        catch (IOException) { return; } // being written: its Changed comes next
        lock (gate)
        {
            if (now != null && !Alive(now.Pid)) now = null; // left behind by one that ended badly
            if (Equals(now, Now)) return;
            Now = now;
        }
        Changed?.Invoke();
    }

    // Waits on the install's process: when it goes, so does the glyph, whatever its file says.
    static bool Alive(int pid)
    {
        try
        {
            waiting?.Dispose();
            waiting = Process.GetProcessById(pid);
            if (waiting.HasExited) return false;
            waiting.EnableRaisingEvents = true;
            int watched = pid;
            waiting.Exited += (_, _) =>
            {
                lock (gate) { if (Now?.Pid != watched) return; Now = null; }
                Changed?.Invoke();
            };
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }

    static void ReadDone()
    {
        var file = Path.Combine(MenuModel.StateDir, InstallMark.DoneFile);
        try
        {
            if (!File.Exists(file) || !InstallMark.Fresh(File.GetLastWriteTime(file), DateTime.Now)) return;
            if (InstallMark.ParseDone(File.ReadAllLines(file)) is not { } done) return;
            // Said once: the file's Changed can come twice for one write.
            var key = $"{File.GetLastWriteTimeUtc(file).Ticks}";
            lock (gate)
            {
                if (key == said) return;
                said = key;
            }
            Snip.Say(InstallMark.Toast(done));
        }
        catch (IOException) { } // being written: its next Changed reads it
    }
    static string said = "";
}
