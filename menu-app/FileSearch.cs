using System.Runtime.InteropServices;

namespace SystemMenu;

// Files in the launcher: fff (github.com/dmtrKovalenko/fff, MIT), a fast fuzzy file finder,
// through its C library (fff_c.dll, downloaded by build.ps1, pinned). One index per folder you
// keep files in (Desktop, Documents, Downloads, and the project folders), each kept current by
// fff's file watcher, honoring .gitignore, and ranking files you open higher (frecency, in
// %LOCALAPPDATA%\launcher\fff). Started in the background when the menu starts; until an
// index is ready its folder just isn't searched.
public static class FileSearch
{
    // A file or folder found: where it is (its root folder's name, e.g. Documents, and the
    // folders under that), and what fff knows about it.
    public sealed record Hit(string Path, string Name, string RootLabel, string RelativeDir,
        bool IsDir, long Size, DateTime? Modified, string? Git, bool Binary, int Score);

    sealed class Index(string root, IntPtr handle)
    {
        public string Root = root;
        public IntPtr Handle = handle;
    }

    static readonly List<Index> indexes = [];
    static readonly object gate = new();

    // Folders left out (launcher/finder-skip.txt): names (at any depth) or full paths.
    static readonly Lazy<string[]> skip = new(() =>
    {
        try
        {
            return [.. File.ReadAllLines(Path.Combine(MenuModel.Repo, "launcher", "finder-skip.txt"))
                .Select(l => l.Trim().TrimEnd('\\', '/')).Where(l => l.Length > 0 && !l.StartsWith('#'))];
        }
        catch { return []; }
    });

    static bool Skipped(string path) => skip.Value.Any(s =>
        s.Contains('\\') || s.Contains(':')
            ? path.Equals(s, StringComparison.OrdinalIgnoreCase) || path.StartsWith(s + "\\", StringComparison.OrdinalIgnoreCase)
            : path.Split('\\').Any(part => part.Equals(s, StringComparison.OrdinalIgnoreCase)));

    // Where files are looked for.
    static IEnumerable<string> Roots()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        // Windows' Desktop and Documents, and the ones in your home folder too: with OneDrive
        // backing them up, Windows' are OneDrive's, and work kept in ~\Documents (a work folder, ...)
        // would never be found.
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.Combine(home, "Desktop"),
            Path.Combine(home, "Documents"),
            Path.Combine(home, "Downloads"),
            Path.Combine(home, "repos"),
            Path.Combine(home, "source", "repos"),
        };
        // And git repos sitting right in your home folder (~\omawin).
        try { roots = [.. roots, .. Directory.GetDirectories(home).Where(d => Directory.Exists(Path.Combine(d, ".git")))]; } catch { }
        var list = roots.Where(Directory.Exists).Select(r => Path.GetFullPath(r).TrimEnd('\\'))
            .Distinct(StringComparer.OrdinalIgnoreCase).Where(r => !Skipped(r)).ToList();
        // One inside another (Documents\repos) is already covered by the outer one.
        return list.Where(r => !list.Any(o => o != r && r.StartsWith(o + "\\", StringComparison.OrdinalIgnoreCase)));
    }

    public static void Start()
    {
        new Thread(() =>
        {
            try
            {
                string db = Path.Combine(MenuModel.StateDir, "fff");
                Directory.CreateDirectory(db);
                foreach (var root in Roots())
                {
                    string name = new string(root.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
                    var handle = Create(root, Path.Combine(db, name + "-frecency"), Path.Combine(db, name + "-history"));
                    if (handle != IntPtr.Zero) lock (gate) indexes.Add(new Index(root, handle));
                }
            }
            catch (Exception ex) { App.Log(ex); }
        }) { IsBackground = true }.Start();
    }

    // The folders searched, for the finder's scope chips: (path, name).
    public static List<(string Root, string Label)> Scopes()
    {
        lock (gate) return [.. indexes.Select(i => (i.Root, Label(i.Root)))];
    }

    static string Label(string root) => Path.GetFileName(root) switch
    {
        "repos" when root.Contains(@"\source\", StringComparison.OrdinalIgnoreCase) => @"source\repos",
        var name when root.Contains(@"\OneDrive", StringComparison.OrdinalIgnoreCase) => "OneDrive " + name,
        var name => name,
    };

    // Files and folders matching a query, best first, across every folder or just one (scope);
    // or just folders. An empty query lists what you use most (fff's frecency).
    public static List<Hit> Search(string query, string? scope = null, int limit = 60, bool foldersOnly = false)
    {
        var hits = new List<Hit>();
        Index[] all;
        lock (gate) all = [.. indexes.Where(i => scope == null || i.Root == scope)];
        foreach (var index in all)
        {
            IntPtr envelope = IntPtr.Zero, result = IntPtr.Zero;
            try
            {
                // More than shown, so the re-ranking below has good names to choose from.
                // Folders only: far more, since most of what fff finds is files.
                envelope = fff_search_mixed(index.Handle, query, null, 0, 0, (uint)((query.Length == 0 ? limit : 4 * limit) * (foldersOnly ? 10 : 1)), 0, 0);
                if (envelope == IntPtr.Zero || !fff_result_get_success(envelope)) continue;
                result = fff_result_get_handle(envelope);
                if (result == IntPtr.Zero) continue;
                uint count = (uint)Marshal.ReadInt32(result, 2 * IntPtr.Size); // FffMixedSearchResult.count
                for (uint i = 0; i < count; i++)
                {
                    // FffMixedItem: item_type, relative_path, display_name, git_status, size,
                    // modified, three frecency scores, is_binary.
                    var item = fff_mixed_search_result_get_item(result, i);
                    var score = fff_mixed_search_result_get_score(result, i);
                    if (item == IntPtr.Zero) continue;
                    bool isDir = Marshal.ReadByte(item, 0) == 1;
                    if (foldersOnly && !isDir) continue;
                    string? relative = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(item, 8));
                    if (string.IsNullOrEmpty(relative)) continue;
                    relative = relative.Replace('/', '\\').TrimEnd('\\');
                    string? git = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(item, 24));
                    long size = Marshal.ReadInt64(item, 32);
                    long modified = Marshal.ReadInt64(item, 40);
                    bool binary = Marshal.ReadByte(item, 72) != 0;
                    string full = Path.Combine(index.Root, relative);
                    if (Skipped(full)) continue; // a skipped folder inside a searched one
                    hits.Add(new Hit(full, Path.GetFileName(full), Label(index.Root),
                        Path.GetDirectoryName(relative) ?? "", isDir, size,
                        modified > 0 ? DateTimeOffset.FromUnixTimeSeconds(modified).LocalDateTime : null,
                        string.IsNullOrEmpty(git) || git == "clean" ? null : git, binary,
                        score == IntPtr.Zero ? 0 : Marshal.ReadInt32(score)));
                }
            }
            catch { }
            finally
            {
                if (result != IntPtr.Zero) fff_free_mixed_search_result(result);
                if (envelope != IntPtr.Zero) fff_free_result(envelope);
            }
        }
        if (query.Length == 0)
            return [.. hits.OrderByDescending(h => h.Score).ThenByDescending(h => h.Modified).Take(limit)];
        // fff matches anywhere in the path, so a big repo's deep files can crowd out what you
        // meant. Those whose own name matches come first, folders before files (scored like
        // the launcher's apps), then the rest in fff's order.
        return [.. hits
            .Select(h => (h, name: Fuzzy.Match(h.Name, query)))
            .OrderByDescending(x => x.name == Fuzzy.None ? 0 : 1)
            .ThenByDescending(x => x.name != Fuzzy.None && x.h.IsDir)
            .ThenByDescending(x => x.name == Fuzzy.None ? 0 : x.name - 0.02 * x.h.Name.Length)
            .ThenByDescending(x => x.h.Score)
            .Select(x => x.h).Take(limit)];
    }

    // Opened from the launcher: fff ranks it higher next time, most of all for this query.
    public static void Opened(string path, string query)
    {
        Index? index;
        lock (gate) index = indexes.FirstOrDefault(i => path.StartsWith(i.Root + "\\", StringComparison.OrdinalIgnoreCase));
        if (index == null) return;
        try
        {
            var envelope = fff_track_query(index.Handle, query, path);
            if (envelope != IntPtr.Zero) fff_free_result(envelope);
        }
        catch { }
    }

    static IntPtr Create(string root, string frecency, string history)
    {
        var strings = new List<IntPtr>();
        IntPtr Utf8(string s) { var p = Marshal.StringToCoTaskMemUTF8(s); strings.Add(p); return p; }
        try
        {
            var options = new CreateOptions
            {
                Version = 2,
                BasePath = Utf8(root),
                FrecencyDbPath = Utf8(frecency),
                HistoryDbPath = Utf8(history),
                Watch = true,
            };
            var envelope = fff_create_instance_with(ref options);
            if (envelope == IntPtr.Zero) return IntPtr.Zero;
            try
            {
                if (!fff_result_get_success(envelope))
                {
                    App.Log(new Exception($"fff {root}: {Marshal.PtrToStringUTF8(fff_result_get_error(envelope))}"));
                    return IntPtr.Zero;
                }
                return fff_result_get_handle(envelope);
            }
            finally { fff_free_result(envelope); }
        }
        finally { foreach (var p in strings) Marshal.FreeCoTaskMem(p); }
    }

    // FffCreateOptions (fff.h), version 2.
    [StructLayout(LayoutKind.Sequential)]
    struct CreateOptions
    {
        public uint Version;
        public IntPtr BasePath, FrecencyDbPath, HistoryDbPath;
        [MarshalAs(UnmanagedType.U1)] public bool EnableMmapCache;
        [MarshalAs(UnmanagedType.U1)] public bool EnableContentIndexing;
        [MarshalAs(UnmanagedType.U1)] public bool Watch;
        [MarshalAs(UnmanagedType.U1)] public bool AiMode;
        public IntPtr LogFilePath, LogLevel;
        public ulong CacheMaxFiles, CacheMaxBytes, CacheMaxFileSize;
        [MarshalAs(UnmanagedType.U1)] public bool EnableFsRootScanning;
        [MarshalAs(UnmanagedType.U1)] public bool EnableHomeDirScanning;
        [MarshalAs(UnmanagedType.U1)] public bool FollowSymlinks;
    }

    const string Lib = "fff_c";
    [DllImport(Lib)] static extern IntPtr fff_create_instance_with(ref CreateOptions options);
    [DllImport(Lib)] static extern IntPtr fff_search_mixed(IntPtr fff, [MarshalAs(UnmanagedType.LPUTF8Str)] string query,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? currentFile, uint maxThreads, uint pageIndex, uint pageSize, int comboBoost, uint minCombo);
    [DllImport(Lib)] static extern void fff_free_mixed_search_result(IntPtr result);
    [DllImport(Lib)] static extern IntPtr fff_mixed_search_result_get_item(IntPtr result, uint index);
    [DllImport(Lib)] static extern IntPtr fff_mixed_search_result_get_score(IntPtr result, uint index);
    [DllImport(Lib)] static extern IntPtr fff_track_query(IntPtr fff, [MarshalAs(UnmanagedType.LPUTF8Str)] string query, [MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [DllImport(Lib)] [return: MarshalAs(UnmanagedType.U1)] static extern bool fff_result_get_success(IntPtr result);
    [DllImport(Lib)] static extern IntPtr fff_result_get_error(IntPtr result);
    [DllImport(Lib)] static extern IntPtr fff_result_get_handle(IntPtr result);
    [DllImport(Lib)] static extern void fff_free_result(IntPtr result);
}
