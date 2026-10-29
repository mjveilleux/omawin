using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SystemMenu;

// What runs in the background while the menu app is up (it always is), so the launcher's lists
// are ready when you open them:
//   - the clipboard history (Clipboard page, Win+Shift+V): every text you copy
//   - the installed apps (apps.tsv), every 10 minutes, and seconds after an install or removal
//   - the projects (the Ubuntu ones cached in wsl-projects.txt), every 10 minutes, and seconds
//     after a git repo appears or goes in your home folder
//   - the reports (reports.tsv), every 2 hours, where ~/omawin-account has a reports script
public static class Background
{
    public static void Start()
    {
        ClipboardHistory.Start();
        // Shell.Application is an apartment-threaded COM object: an STA thread uses it directly,
        // not through a proxy.
        var apps = new Thread(IndexApps) { IsBackground = true, Name = "apps" };
        apps.SetApartmentState(ApartmentState.STA);
        apps.Start();
        new Thread(IndexProjects) { IsBackground = true, Name = "projects" }.Start();
        new Thread(IndexReports) { IsBackground = true, Name = "reports" }.Start();
    }

    // --- installed apps -------------------------------------------------------------------
    // Start's All apps (desktop programs and Store apps, shell:AppsFolder), as "name<TAB>app id"
    // lines in %LOCALAPPDATA%\launcher\apps.tsv; Start menu clutter left out.
    static readonly System.Text.RegularExpressions.Regex Noise = new(
        "uninstall|readme|read me|release notes|documentation|license|website|help$|manual|what's new",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    // Set when a Start menu shortcut appears, changes or goes (an app installed or removed), so
    // the list is redone within seconds instead of at the next 10 minutes.
    static readonly AutoResetEvent appsChanged = new(false);
    static readonly List<FileSystemWatcher> startMenuWatchers = [];

    static void WatchStartMenu()
    {
        string[] folders =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
        ];
        foreach (var folder in folders.Where(Directory.Exists))
        {
            try
            {
                var w = new FileSystemWatcher(folder) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName };
                w.Created += (_, _) => appsChanged.Set();
                w.Deleted += (_, _) => appsChanged.Set();
                w.Renamed += (_, _) => appsChanged.Set();
                w.EnableRaisingEvents = true;
                startMenuWatchers.Add(w);
            }
            catch (Exception ex) { App.Log(ex); }
        }
    }

    static void IndexApps()
    {
        var file = Path.Combine(MenuModel.StateDir, "apps.tsv");
        WatchStartMenu();
        while (true)
        {
            try
            {
                var rows = new SortedSet<string>(StringComparer.Ordinal);
                foreach (var (name, id) in AppsFolder.Items())
                    if (!string.IsNullOrWhiteSpace(name) && !Noise.IsMatch(name)) rows.Add($"{name}\t{id}");
                if (rows.Count > 0)
                {
                    Directory.CreateDirectory(MenuModel.StateDir);
                    File.WriteAllLines(file + ".part", rows);
                    File.Move(file + ".part", file, overwrite: true);
                }
            }
            catch (Exception ex) { App.Log(ex); }
            if (appsChanged.WaitOne(TimeSpan.FromMinutes(10)))
            {
                // An installer makes several shortcuts in a row: wait for it to finish.
                Thread.Sleep(TimeSpan.FromSeconds(3));
                appsChanged.Reset();
            }
        }
    }

    // --- projects ---------------------------------------------------------------------------
    // The Projects page searches each time it opens; this keeps Projects.Latest (the launcher's
    // home) and the Ubuntu list from last time (wsl-projects.txt, used while WSL isn't running)
    // current: every 10 minutes, and seconds after a repo appears or goes anywhere in your home
    // folder (a clone, a git init, a deleted repo, a repo folder moved in).
    static readonly AutoResetEvent reposChanged = new(false);
    static FileSystemWatcher? repoWatcher;

    // A .git folder where the Projects search looks: not in AppData or a dot folder (Neovim's
    // plugins, cargo's checkouts are repos too), and not deep in a tree (node_modules).
    static bool IsRepoChange(string path)
    {
        var parts = Path.GetRelativePath(Projects.Home, path).Split(Path.DirectorySeparatorChar);
        if (parts.Length > 5 || parts[0].StartsWith('.') || parts[0].Equals("AppData", StringComparison.OrdinalIgnoreCase)) return false;
        return parts[^1].Equals(".git", StringComparison.OrdinalIgnoreCase);
    }

    static void WatchRepos()
    {
        try
        {
            repoWatcher = new FileSystemWatcher(Projects.Home) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.DirectoryName, InternalBufferSize = 64 * 1024 };
            repoWatcher.Created += (_, e) => { if (IsRepoChange(e.FullPath)) reposChanged.Set(); };
            repoWatcher.Deleted += (_, e) => { if (IsRepoChange(e.FullPath)) reposChanged.Set(); };
            repoWatcher.Renamed += (_, e) =>
            {
                if (IsRepoChange(e.FullPath) || IsRepoChange(e.OldFullPath) || IsRepoChange(Path.Combine(e.FullPath, ".git")) && Directory.Exists(Path.Combine(e.FullPath, ".git")))
                    reposChanged.Set();
            };
            // Too many changes at once (a big unzip): something may have been missed, so look.
            repoWatcher.Error += (_, _) => reposChanged.Set();
            repoWatcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) { App.Log(ex); }
    }

    static void IndexProjects()
    {
        WatchRepos();
        while (true)
        {
            try { Projects.Find().Wait(); } catch (Exception ex) { App.Log(ex); }
            if (reposChanged.WaitOne(TimeSpan.FromMinutes(10)))
            {
                // A clone makes .git first and fills it for a while: let it finish.
                Thread.Sleep(TimeSpan.FromSeconds(2));
                reposChanged.Reset();
            }
        }
    }

    // --- reports ---------------------------------------------------------------------------
    // The reports script's -Index writes reports.tsv (Pages.cs, Reports); it's run here, hidden,
    // only where there is one (~/omawin-account).
    static void IndexReports()
    {
        while (true)
        {
            if (Reports.Available)
            {
                try
                {
                    using var p = Process.Start(new ProcessStartInfo("pwsh", $"-NoLogo -NoProfile -WindowStyle Hidden -File \"{Reports.Script}\" -Index")
                    { UseShellExecute = false, CreateNoWindow = true });
                    p?.WaitForExit(TimeSpan.FromMinutes(20));
                }
                catch (Exception ex) { App.Log(ex); }
            }
            Thread.Sleep(TimeSpan.FromHours(2));
        }
    }
}

// The clipboard history: each text you copy, as one file in %LOCALAPPDATA%\launcher\clipboard
// (named by when, newest first; the newest 200 kept; copying something again moves it to the
// top). Copies that password managers mark as private are skipped, as Windows' own clipboard
// history does (ExcludeClipboardContentFromMonitorProcessing, CanIncludeInClipboardHistory = 0).
public static class ClipboardHistory
{
    public static readonly string Dir = Path.Combine(MenuModel.StateDir, "clipboard");
    const int Keep = 200;

    [DllImport("user32.dll")] static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll", SetLastError = true)] static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] static extern bool CloseClipboard();
    [DllImport("user32.dll")] static extern bool EmptyClipboard();
    [DllImport("user32.dll")] static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll")] static extern IntPtr SetClipboardData(uint format, IntPtr data);
    [DllImport("user32.dll")] static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterClipboardFormat(string name);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr mem);
    [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr mem);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    const uint CF_UNICODETEXT = 13;

    public static void Start() => new Thread(Watch) { IsBackground = true, Name = "clipboard" }.Start();

    static void Watch()
    {
        uint seen = GetClipboardSequenceNumber();
        while (true)
        {
            Thread.Sleep(500);
            uint now = GetClipboardSequenceNumber();
            if (now == seen) continue;
            try
            {
                var text = ReadText();
                if (text == null) continue; // someone has the clipboard open: next time
                seen = now;
                Save(text);
            }
            catch { seen = now; }
        }
    }

    // The text, "" when there's nothing to keep (no text, or marked private), null when busy.
    static string? ReadText()
    {
        if (!OpenClipboard(IntPtr.Zero)) return null;
        try
        {
            if (IsClipboardFormatAvailable(RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing"))) return "";
            uint history = RegisterClipboardFormat("CanIncludeInClipboardHistory");
            if (IsClipboardFormatAvailable(history))
            {
                var h = GetClipboardData(history);
                var p = GlobalLock(h);
                if (p != IntPtr.Zero)
                {
                    int allowed = Marshal.ReadInt32(p);
                    GlobalUnlock(h);
                    if (allowed == 0) return "";
                }
            }
            if (!IsClipboardFormatAvailable(CF_UNICODETEXT)) return "";
            var data = GetClipboardData(CF_UNICODETEXT);
            var text = GlobalLock(data);
            if (text == IntPtr.Zero) return "";
            try { return Marshal.PtrToStringUni(text) ?? ""; } finally { GlobalUnlock(data); }
        }
        finally { CloseClipboard(); }
    }

    static void Save(string text)
    {
        if (text.Trim().Length == 0 || text.Length > 200_000) return;
        Directory.CreateDirectory(Dir);
        foreach (var f in Directory.GetFiles(Dir, "*.txt"))
            try { if (File.ReadAllText(f) == text) File.Delete(f); } catch { }
        File.WriteAllText(Path.Combine(Dir, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".txt"), text);
        foreach (var old in Entries().Skip(Keep)) try { File.Delete(old); } catch { }
    }

    // Newest first.
    public static List<string> Entries() =>
        Directory.Exists(Dir) ? [.. Directory.GetFiles(Dir, "*.txt").OrderByDescending(Path.GetFileName, StringComparer.Ordinal)] : [];

    public static DateTime When(string file) =>
        DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file), "yyyyMMdd-HHmmss-fff", null, System.Globalization.DateTimeStyles.None, out var t) ? t : File.GetLastWriteTime(file);

    // Puts text on the clipboard (it's then also saved to the history as the newest entry).
    public static bool SetText(string text)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    EmptyClipboard();
                    var bytes = (text.Length + 1) * 2;
                    var mem = GlobalAlloc(0x0002 /* GMEM_MOVEABLE */, (UIntPtr)bytes);
                    var p = GlobalLock(mem);
                    Marshal.Copy(text.ToCharArray(), 0, p, text.Length);
                    Marshal.WriteInt16(p, text.Length * 2, 0);
                    GlobalUnlock(mem);
                    SetClipboardData(CF_UNICODETEXT, mem);
                    return true;
                }
                finally { CloseClipboard(); }
            }
            Thread.Sleep(30);
        }
        return false;
    }
}
