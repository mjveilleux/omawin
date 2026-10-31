using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SystemMenu;

// What runs in the background while the menu app is up (it always is), so the launcher's lists
// are ready when you open them:
//   - the clipboard history (Clipboard page, Win+Shift+V): every text and picture you copy
//   - the installed apps (apps.tsv), every 10 minutes, and seconds after an install or removal
//   - the projects (the Linux ones cached in wsl-projects.txt), every 10 minutes, and seconds
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
    // home) and the Linux list from last time (wsl-projects.txt, used while WSL isn't running)
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

// The clipboard history: each text or picture you copy, as one file in %LOCALAPPDATA%\launcher\
// clipboard (named by when, newest first; the newest 200 kept; copying something again moves it to
// the top), and the ones you pin in clipboard\pinned, kept for good. Copies that password managers
// mark as private are skipped, as Windows' own clipboard history does
// (ExcludeClipboardContentFromMonitorProcessing, CanIncludeInClipboardHistory = 0). Windows tells
// it when the clipboard changes (AddClipboardFormatListener): nothing polls.
public static unsafe partial class ClipboardHistory
{
    public static readonly string Dir = Path.Combine(MenuModel.StateDir, "clipboard");
    public static string PinnedDir => Path.Combine(Dir, ClipEntries.PinnedFolder);
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
    [DllImport("kernel32.dll")] static extern UIntPtr GlobalSize(IntPtr mem);
    const uint CF_UNICODETEXT = 13, CF_DIB = 8, CF_DIBV5 = 17, WM_CLIPBOARDUPDATE = 0x031D;

    [StructLayout(LayoutKind.Sequential)]
    struct WNDCLASSEXW
    {
        public uint cbSize, style;
        public nint lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public nint hInstance, hIcon, hCursor, hbrBackground, lpszMenuName, lpszClassName, hIconSm;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct MSG { public nint hwnd; public uint message; public nuint wParam; public nint lParam; public uint time; public int x, y; }

    [LibraryImport("user32.dll", SetLastError = true)] private static partial ushort RegisterClassExW(WNDCLASSEXW* wc);
    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowExW(uint exStyle, string cls, string? name, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);
    [LibraryImport("user32.dll")] private static partial nint DefWindowProcW(nint hwnd, uint msg, nuint w, nint l);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool AddClipboardFormatListener(nint hwnd);
    [LibraryImport("user32.dll")] private static partial int GetMessageW(MSG* msg, nint hwnd, uint min, uint max);
    [LibraryImport("user32.dll")] private static partial nint DispatchMessageW(MSG* msg);
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial nint GetModuleHandleW(string? name);

    public static void Start() => new Thread(Listen) { IsBackground = true, Name = "clipboard" }.Start();

    static uint seen;

    // A message-only window that Windows tells about each clipboard change. Should it fail (it
    // shouldn't), the old way: a look every half second.
    static void Listen()
    {
        seen = GetClipboardSequenceNumber();
        const string name = "omawin-clipboard-listener";
        var wc = new WNDCLASSEXW
        {
            cbSize = (uint)sizeof(WNDCLASSEXW),
            lpfnWndProc = (nint)(delegate* unmanaged<nint, uint, nuint, nint, nint>)&WndProc,
            hInstance = GetModuleHandleW(null),
        };
        nint hwnd = 0;
        fixed (char* cls = name)
        {
            wc.lpszClassName = (nint)cls;
            if (RegisterClassExW(&wc) != 0)
                hwnd = CreateWindowExW(0, name, null, 0, 0, 0, 0, 0, -3 /* HWND_MESSAGE */, 0, wc.hInstance, 0);
        }
        if (hwnd != 0 && AddClipboardFormatListener(hwnd))
        {
            MSG msg;
            while (GetMessageW(&msg, 0, 0, 0) > 0) DispatchMessageW(&msg);
            return;
        }
        App.Log(new Exception($"clipboard: no listener ({Marshal.GetLastPInvokeError()}); looking every 500 ms"));
        while (true)
        {
            Thread.Sleep(500);
            Changed();
        }
    }

    [UnmanagedCallersOnly]
    static nint WndProc(nint hwnd, uint msg, nuint w, nint l)
    {
        if (msg == WM_CLIPBOARDUPDATE)
        {
            try { Changed(); } catch (Exception ex) { App.Log(ex); }
            return 0;
        }
        return DefWindowProcW(hwnd, msg, w, l);
    }

    // The program that copied may still hold the clipboard open: a few short tries.
    static void Changed()
    {
        uint now = GetClipboardSequenceNumber();
        if (now == seen) return;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            Copied? copied;
            try { copied = Read(); }
            catch (Exception ex) { App.Log(ex); seen = now; return; }
            if (copied == null) { Thread.Sleep(40); continue; }
            seen = now;
            if (copied.Text is { } text) Save(text);
            else if (copied.Png is { } png) SavePicture(png);
            return;
        }
    }

    sealed record Copied(string? Text, byte[]? Png);

    // What was copied: its text, else its picture as PNG; neither when it's marked private or is
    // something else (files, rich data only). Null when the clipboard is busy.
    static Copied? Read()
    {
        if (!OpenClipboard(IntPtr.Zero)) return null;
        try
        {
            if (IsClipboardFormatAvailable(RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing"))) return new(null, null);
            uint history = RegisterClipboardFormat("CanIncludeInClipboardHistory");
            if (IsClipboardFormatAvailable(history) && Bytes(history) is { Length: >= 4 } allowed && BitConverter.ToInt32(allowed) == 0)
                return new(null, null);
            if (IsClipboardFormatAvailable(CF_UNICODETEXT))
            {
                var data = GetClipboardData(CF_UNICODETEXT);
                var text = GlobalLock(data);
                if (text == IntPtr.Zero) return new(null, null);
                try { return new(Marshal.PtrToStringUni(text) ?? "", null); } finally { GlobalUnlock(data); }
            }
            // A picture: browsers, Snip and most apps also put a PNG (alpha and all); else the bitmap.
            uint png = RegisterClipboardFormat("PNG");
            if (IsClipboardFormatAvailable(png) && Bytes(png) is { } p && ClipEntries.PngSize(p) != null) return new(null, p);
            foreach (var format in new[] { CF_DIBV5, CF_DIB })
                if (IsClipboardFormatAvailable(format) && Bytes(format) is { } dib && ClipEntries.DibToBmp(dib) is { } bmp)
                    return new(null, BmpToPng(bmp));
            return new(null, null);
        }
        finally { CloseClipboard(); }
    }

    // A clipboard format's bytes (the clipboard open), or null; none past the size kept.
    static byte[]? Bytes(uint format)
    {
        var h = GetClipboardData(format);
        if (h == IntPtr.Zero) return null;
        var size = (long)GlobalSize(h);
        if (size <= 0 || size > ClipEntries.MaxImageBytes) return null;
        var p = GlobalLock(h);
        if (p == IntPtr.Zero) return null;
        try
        {
            var bytes = new byte[size];
            Marshal.Copy(p, bytes, 0, bytes.Length);
            return bytes;
        }
        finally { GlobalUnlock(h); }
    }

    static byte[]? BmpToPng(byte[] bmp)
    {
        try
        {
            using var input = new MemoryStream(bmp);
            using var image = System.Drawing.Image.FromStream(input);
            using var output = new MemoryStream();
            image.Save(output, System.Drawing.Imaging.ImageFormat.Png);
            return output.ToArray();
        }
        catch (Exception ex) when (ex is ArgumentException or System.Runtime.InteropServices.ExternalException) { return null; }
    }

    static void Save(string text)
    {
        if (text.Trim().Length == 0 || text.Length > 200_000) return;
        Directory.CreateDirectory(Dir);
        // Copied again: moves to the top (a pinned one stays pinned, and isn't added twice).
        foreach (var f in Directory.GetFiles(Dir, "*.txt"))
            try { if (File.ReadAllText(f) == text) File.Delete(f); } catch { }
        if (Directory.Exists(PinnedDir) && Directory.GetFiles(PinnedDir, "*.txt").Any(f => { try { return File.ReadAllText(f) == text; } catch { return false; } })) return;
        File.WriteAllText(Path.Combine(Dir, DateTime.Now.ToString(ClipEntries.Stamp) + ".txt"), text);
        TrimOld();
    }

    static void SavePicture(byte[] png)
    {
        Directory.CreateDirectory(Dir);
        static bool Same(string f, byte[] png) { try { return new FileInfo(f).Length == png.Length && File.ReadAllBytes(f).AsSpan().SequenceEqual(png); } catch { return false; } }
        foreach (var f in Directory.GetFiles(Dir, "*.png"))
            if (Same(f, png)) try { File.Delete(f); } catch { }
        if (Directory.Exists(PinnedDir) && Directory.GetFiles(PinnedDir, "*.png").Any(f => Same(f, png))) return; // pinned already
        File.WriteAllBytes(Path.Combine(Dir, DateTime.Now.ToString(ClipEntries.Stamp) + ".png"), png);
        TrimOld();
    }

    static void TrimOld()
    {
        foreach (var old in ClipEntries.Trim(Directory.GetFiles(Dir), Keep)) try { File.Delete(old); } catch { }
    }

    // Pinned first, then newest first.
    public static List<string> Entries() =>
        Directory.Exists(Dir) ? ClipEntries.Order(Directory.Exists(PinnedDir) ? Directory.GetFiles(PinnedDir) : [], Directory.GetFiles(Dir)) : [];

    public static DateTime When(string file) =>
        DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file), ClipEntries.Stamp, null, System.Globalization.DateTimeStyles.None, out var t) ? t : File.GetLastWriteTime(file);

    // Ctrl+P on the clipboard page: pinned entries stay at the top and are never trimmed; again
    // to unpin. The file moves; its name (its time) stays. Returns the new path.
    public static string TogglePin(string file)
    {
        var to = ClipEntries.IsPinned(file) ? Path.Combine(Dir, Path.GetFileName(file)) : Path.Combine(PinnedDir, Path.GetFileName(file));
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.Move(file, to, overwrite: true);
        return to;
    }

    // An entry back on the clipboard: its text, or its picture (PNG and a bitmap, so every app can paste it).
    public static bool SetEntry(string file)
    {
        if (!ClipEntries.IsImage(file)) return SetText(File.ReadAllText(file));
        var png = File.ReadAllBytes(file);
        byte[]? dib = null;
        try
        {
            using var input = new MemoryStream(png);
            using var image = System.Drawing.Image.FromStream(input);
            using var output = new MemoryStream();
            image.Save(output, System.Drawing.Imaging.ImageFormat.Bmp);
            dib = output.ToArray()[14..]; // a .bmp is the clipboard's bitmap with a file header in front
        }
        catch (Exception ex) when (ex is ArgumentException or System.Runtime.InteropServices.ExternalException) { }
        var formats = new List<(uint, byte[])> { (RegisterClipboardFormat("PNG"), png) };
        if (dib != null) formats.Add((CF_DIB, dib));
        return Set([.. formats]);
    }

    // Puts text on the clipboard (it's then also saved to the history as the newest entry).
    public static bool SetText(string text)
    {
        var bytes = new byte[(text.Length + 1) * 2];
        System.Text.Encoding.Unicode.GetBytes(text, bytes);
        return Set([(CF_UNICODETEXT, bytes)]);
    }

    static bool Set((uint Format, byte[] Data)[] formats)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    EmptyClipboard();
                    foreach (var (format, data) in formats)
                    {
                        var mem = GlobalAlloc(0x0002 /* GMEM_MOVEABLE */, (UIntPtr)data.Length);
                        var p = GlobalLock(mem);
                        Marshal.Copy(data, 0, p, data.Length);
                        GlobalUnlock(mem);
                        SetClipboardData(format, mem); // the clipboard owns it from here
                    }
                    return true;
                }
                finally { CloseClipboard(); }
            }
            Thread.Sleep(30);
        }
        return false;
    }
}
