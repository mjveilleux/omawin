using System.Runtime.InteropServices;

namespace SystemMenu;

// Whole-disk search (roadmap 8.2): the finder's Everywhere chip, every file and folder name on
// the fixed drives. Off until you turn it on (`omawin everywhere on`, or Enter on the chip), since
// walking a disk costs a minute of background disk time. No admin and no service: the menu app
// keeps it, as it keeps fff's indexes.
//   - the walk (DiskIndex.Walk) runs once the PC has been idle for 2 minutes, at background
//     priority (Windows lowers its disk and CPU priority too), when there's no index yet, after
//     each start (what changed while the menu app wasn't running), after Windows said it lost
//     changes, and once a day
//   - between walks Windows' change notifications (FileSystemWatcher on each drive's root, which
//     is ReadDirectoryChangesW) keep it current: created, deleted, renamed
//   - the index lives in %LOCALAPPDATA%\launcher\disk-index\index.bin, and in memory only while
//     it's being searched (about 60 bytes a name; dropped after 3 minutes unused); changes that
//     come while it's not loaded wait in a queue and are applied when it is
// Names only, never contents; and only what this account can list (another account's folders
// can't be read, so they aren't in it).
public static class Everywhere
{
    static string Dir => Path.Combine(MenuModel.StateDir, "disk-index");
    static string IndexFile => Path.Combine(Dir, "index.bin");
    static string OnFile => Path.Combine(Dir, "on");
    // count<TAB>walked (UTC ticks)<TAB>drives: what `omawin everywhere` says without loading it.
    static string StatusFile => Path.Combine(Dir, "status.txt");

    public static bool On => File.Exists(OnFile);
    public enum State { Off, Walking, Ready }
    // Off, walking with nothing to search yet, or searchable.
    public static State Now => !On ? State.Off : File.Exists(IndexFile) ? State.Ready : State.Walking;

    static readonly object gate = new();
    static DiskIndex? index;
    static DateTime lastUse;
    static bool dirty;
    static volatile bool walking;
    static volatile bool stale;          // a change was missed: walk again
    static bool walkedThisRun;
    static DateTime startedAt;
    static CancellationTokenSource? cancel;
    static Timer? timer;
    static DiskIndex.Skip? skip;
    static readonly Queue<(WatcherChangeTypes Kind, string Path, string? From)> pending = new();
    const int MaxPending = 200_000;
    static readonly List<FileSystemWatcher> watchers = [];

    static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static void Start()
    {
        if (On) Begin();
    }

    static void Begin()
    {
        lock (gate)
        {
            startedAt = DateTime.UtcNow;
            skip = MakeSkip();
            if (watchers.Count == 0) Watch();
            timer ??= new Timer(_ => Tick(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }
    }

    // The fixed drives (internal disks, not USB sticks or network shares) that are ready.
    static List<string> Drives()
    {
        try { return [.. DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).Select(d => d.RootDirectory.FullName)]; }
        catch (IOException) { return []; }
    }

    // What's left out: the finder's own skip list (default/launcher/finder-skip.txt), Windows'
    // bookkeeping folders nobody opens a file from, and this index's own folder.
    static DiskIndex.Skip MakeSkip()
    {
        var (names, paths) = FileSearch.SkipList();
        return new DiskIndex.Skip(
            [.. names, "$Recycle.Bin", "System Volume Information", "$WinREAgent", "$SysReset", "$Windows.~BT", "$Windows.~WS", "WinSxS", "Config.Msi"],
            [.. paths, Dir]);
    }

    // --- watching ----------------------------------------------------------------------------

    static void Watch()
    {
        foreach (var root in Drives())
        {
            try
            {
                var w = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                    InternalBufferSize = 64 * 1024,
                };
                w.Created += (_, e) => Changed(WatcherChangeTypes.Created, e.FullPath, null);
                w.Deleted += (_, e) => Changed(WatcherChangeTypes.Deleted, e.FullPath, null);
                w.Renamed += (_, e) => Changed(WatcherChangeTypes.Renamed, e.FullPath, e.OldFullPath);
                // Windows' buffer overflowed (a huge copy): some changes are lost, so walk again.
                w.Error += (_, _) => stale = true;
                w.EnableRaisingEvents = true;
                watchers.Add(w);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { App.Log(ex); }
        }
    }

    static void Changed(WatcherChangeTypes kind, string path, string? from)
    {
        var s = skip;
        if (s == null || s.Under(path) && (from == null || s.Under(from))) return;
        lock (gate)
        {
            if (!walking && index != null) { Apply(index, kind, path, from); return; }
            if (pending.Count >= MaxPending) { pending.Clear(); stale = true; return; }
            if (!stale) pending.Enqueue((kind, path, from));
        }
    }

    static void Apply(DiskIndex to, WatcherChangeTypes kind, string path, string? from)
    {
        var s = skip ?? MakeSkip();
        try
        {
            switch (kind)
            {
                case WatcherChangeTypes.Created: to.Created(path, Directory.Exists(path), s); break;
                case WatcherChangeTypes.Deleted: to.Deleted(path); break;
                case WatcherChangeTypes.Renamed: to.Renamed(from!, path, Directory.Exists(path), s); break;
            }
            dirty = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
    }

    static void Drain(DiskIndex to)
    {
        while (pending.TryDequeue(out var c)) Apply(to, c.Kind, c.Path, c.From);
    }

    // --- walking -----------------------------------------------------------------------------

    // Each minute: the index out of memory when it's had no use for 3 minutes (saved first if it
    // changed), and a walk when one is due and nobody's at the PC.
    static void Tick()
    {
        try
        {
            lock (gate)
            {
                if (index != null && !walking && DateTime.UtcNow - lastUse > TimeSpan.FromMinutes(3))
                {
                    if (dirty) Save(index);
                    index = null;
                    dirty = false;
                }
            }
            if (!walking && Due() && IdleFor() >= TimeSpan.FromMinutes(2)) Walk();
        }
        catch (Exception ex) { App.Log(ex); }
    }

    static bool Due()
    {
        if (stale || !File.Exists(IndexFile)) return true;
        if (!walkedThisRun && DateTime.UtcNow - startedAt > TimeSpan.FromMinutes(3)) return true;
        return ReadStatus() is { } status && DateTime.UtcNow - status.Walked > TimeSpan.FromDays(1);
    }

    // A fresh walk of every fixed drive, in the background. Changes that come meanwhile are queued
    // and applied to the new index when it's done (the old one, if loaded, still answers).
    static void Walk()
    {
        CancellationToken token;
        lock (gate)
        {
            if (walking) return;
            walking = true;
            stale = false;
            pending.Clear();
            cancel = new CancellationTokenSource();
            token = cancel.Token;
            skip ??= MakeSkip();
        }
        new Thread(() =>
        {
            bool background = SetThreadPriority(GetCurrentThread(), THREAD_MODE_BACKGROUND_BEGIN);
            try
            {
                var drives = Drives();
                var fresh = DiskIndex.Walk(drives, skip!, token);
                lock (gate)
                {
                    if (token.IsCancellationRequested) return; // turned off meanwhile
                    Drain(fresh);
                    Save(fresh);
                    // Searched now: the fresh one. Not in use: nothing stays in memory.
                    index = index != null ? fresh : null;
                    dirty = false;
                    walkedThisRun = true;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { App.Log(ex); stale = true; }
            finally
            {
                if (background) SetThreadPriority(GetCurrentThread(), THREAD_MODE_BACKGROUND_END);
                walking = false;
            }
        }) { IsBackground = true, Name = "disk-index", Priority = ThreadPriority.Lowest }.Start();
    }

    static void Save(DiskIndex what)
    {
        try
        {
            what.Save(IndexFile);
            File.WriteAllText(StatusFile, $"{what.Live}\t{what.Built.Ticks}\t{string.Join(" ", what.Roots)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { App.Log(ex); }
    }

    static (int Names, DateTime Walked, string Drives)? ReadStatus()
    {
        try
        {
            var p = File.ReadAllText(StatusFile).Split('\t');
            if (p.Length == 3 && int.TryParse(p[0], out var n) && long.TryParse(p[1], out var ticks))
                return (n, new DateTime(ticks, DateTimeKind.Utc), p[2]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return null;
    }

    // --- searching ---------------------------------------------------------------------------

    // The best matches on every drive (DiskIndex.Search: every word in the name, *.png, src\x),
    // what's in your home folder first among equals. Loads the index if it isn't in memory.
    public static List<DiskIndex.Found> Search(string query, int limit, bool foldersOnly)
    {
        if (!On || query.Trim().Length == 0) return [];
        lock (gate)
        {
            lastUse = DateTime.UtcNow;
            if (index == null && File.Exists(IndexFile))
            {
                index = DiskIndex.Load(IndexFile);
                if (index == null) stale = true; // damaged or an older format: walk again
                else if (!walking) Drain(index);
            }
            return index?.Search(query, limit, foldersOnly, Home) ?? [];
        }
    }

    // --- omawin everywhere -------------------------------------------------------------------

    public static SettingCommands.Answer Answer(string[] args)
    {
        switch (args.FirstOrDefault()?.ToLowerInvariant() ?? "status")
        {
            case "on":
                Directory.CreateDirectory(Dir);
                if (!On) File.WriteAllText(OnFile, "");
                Begin();
                if (!File.Exists(IndexFile)) Walk();
                return new(0, $"Whole-disk search is on: {string.Join(", ", Drives().Select(d => d.TrimEnd('\\')))} {(walking ? "being walked in the background (a minute or two)" : "indexed")}. Search it with the finder's Everywhere chip (Win+F, Tab).");
            case "off":
                Stop();
                return new(0, "Whole-disk search is off, and its index is deleted.");
            case "rebuild":
                if (!On) return new(1, "Whole-disk search is off: omawin everywhere on");
                if (timer == null) Begin();
                Walk();
                return new(0, "Walking every fixed drive again, in the background.");
            case "status":
                if (!On) return new(0, "Whole-disk search is off. omawin everywhere on turns it on (no admin; a minute of background disk time, then it keeps itself current).");
                var status = ReadStatus();
                long bytes = File.Exists(IndexFile) ? new FileInfo(IndexFile).Length : 0;
                string walked = status is { } s ? $"{s.Names:N0} names on {s.Drives}, walked {Ago(s.Walked)}, {bytes / 1048576.0:F0} MB on disk" : "no index yet";
                return new(0, $"Whole-disk search is on: {walked}{(walking ? "; walking now" : "")}{(stale ? "; changes were missed, walked again at the next idle minute" : "")}.");
            default:
                return new(2, "omawin everywhere [on|off|rebuild|status]");
        }
    }

    static string Ago(DateTime utc)
    {
        var age = DateTime.UtcNow - utc;
        return age.TotalMinutes < 1 ? "just now" : age.TotalHours < 1 ? $"{(int)age.TotalMinutes} min ago"
            : age.TotalDays < 1 ? $"{(int)age.TotalHours} h ago" : $"{(int)age.TotalDays} days ago";
    }

    // Turned off: the watchers, the walk and the index go; the folder with it.
    static void Stop()
    {
        lock (gate)
        {
            cancel?.Cancel();
            foreach (var w in watchers) w.Dispose();
            watchers.Clear();
            timer?.Dispose();
            timer = null;
            index = null;
            pending.Clear();
            try { if (Directory.Exists(Dir)) Directory.Delete(Dir, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { App.Log(ex); }
        }
    }

    // --- Windows -----------------------------------------------------------------------------

    // Background mode: Windows lowers the thread's CPU, disk and memory priority, so a walk never
    // slows what you're doing.
    const int THREAD_MODE_BACKGROUND_BEGIN = 0x00010000, THREAD_MODE_BACKGROUND_END = 0x00020000;
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentThread();
    [DllImport("kernel32.dll")] static extern bool SetThreadPriority(IntPtr thread, int priority);

    [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint Size, Time; }
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

    // How long since the last key or mouse move.
    static TimeSpan IdleFor()
    {
        var info = new LASTINPUTINFO { Size = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        return GetLastInputInfo(ref info) ? TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.Time)) : TimeSpan.Zero;
    }
}
