using System.Runtime.InteropServices;
using System.Text;

namespace Omawin.Cli.Pkg;

// winget's whole catalogue, read off the PC (omawin pkg index): winget keeps it as a small SQLite
// database in its source package (Microsoft.Winget.Source, Public\index.db, refreshed by winget
// about daily). Windows' own SQLite (winsqlite3.dll) reads its ~16,000 packages in milliseconds:
// no network, no winget call. The launcher's Packages page and the fzf pickers read the list.
public static unsafe partial class WingetCatalog
{
    const string Family = "Microsoft.Winget.Source_8wekyb3d8bbwe";

    // The newest installed copy of winget's source package: its index.db and version, or null
    // when winget hasn't fetched its catalogue on this PC yet.
    public static (string Db, string Version)? Find()
    {
        if (!OperatingSystem.IsWindows()) return null;
        uint count = 0, length = 0;
        // Asked once for the sizes (ERROR_INSUFFICIENT_BUFFER), then for the names.
        var rc = GetPackagesByPackageFamily(Family, ref count, null, ref length, null);
        if (rc != 122 || count == 0) return null;
        var names = new char*[count];
        var buffer = new char[length];
        fixed (char** n = names)
        fixed (char* b = buffer)
        {
            if (GetPackagesByPackageFamily(Family, ref count, n, ref length, b) != 0) return null;
            (string Db, string Version, Version Order)? best = null;
            for (int i = 0; i < count; i++)
            {
                var full = new string(names[i]);
                var version = full.Split('_') is [_, var v, ..] ? v : "";
                if (!System.Version.TryParse(version, out var order) || Path(full) is not { } dir) continue;
                var db = System.IO.Path.Combine(dir, "Public", "index.db");
                if (File.Exists(db) && (best == null || order > best.Value.Order)) best = (db, version, order);
            }
            return best is { } found ? (found.Db, found.Version) : null;
        }
    }

    static string? Path(string fullName)
    {
        uint length = 0;
        if (GetPackagePathByFullName(fullName, ref length, null) != 122) return null;
        var buffer = new char[length];
        fixed (char* b = buffer)
            return GetPackagePathByFullName(fullName, ref length, b) == 0 ? new string(b) : null;
    }

    // Every package: id, name, short name (moniker), latest version, tags (comma-separated).
    public static List<string> Read(string db)
    {
        // Read only, and immutable: the file sits in WindowsApps, where nothing may write.
        if (sqlite3_open_v2(Z("file:///" + db.Replace('\\', '/') + "?immutable=1"), out var h, 0x01 | 0x40, 0) != 0)
            throw new IOException("couldn't open " + db);
        try
        {
            var tags = new Dictionary<string, List<string>>();
            foreach (var row in Query(h, "select m.package, t.tag from tags2_map m join tags2 t on t.rowid = m.tag", 2, optional: true))
            {
                if (!tags.TryGetValue(row[0], out var list)) tags[row[0]] = list = [];
                list.Add(row[1]);
            }
            return Query(h, "select rowid, id, name, moniker, latest_version from packages order by name collate nocase", 5, optional: false)
                .Select(r => Line(r[1], r[2], r[3], r[4], tags.GetValueOrDefault(r[0]) ?? [])).ToList();
        }
        finally { sqlite3_close(h); }
    }

    // One line of winget-index.tsv. Pure, so the format is tested.
    public static string Line(string id, string name, string moniker, string version, IEnumerable<string> tags) =>
        string.Join('\t', new[] { id, name, moniker, version, string.Join(", ", tags) }.Select(Clean));

    static string Clean(string s) => s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    static List<string[]> Query(nint db, string sql, int columns, bool optional)
    {
        var rows = new List<string[]>();
        if (sqlite3_prepare_v2(db, Z(sql), -1, out var st, 0) != 0)
        {
            if (optional) return rows;
            throw new IOException("winget's catalogue has no packages table (a newer winget?)");
        }
        try
        {
            while (sqlite3_step(st) == 100) // SQLITE_ROW
            {
                var row = new string[columns];
                for (int i = 0; i < columns; i++)
                {
                    var p = sqlite3_column_text(st, i);
                    row[i] = p == 0 ? "" : Encoding.UTF8.GetString((byte*)p, sqlite3_column_bytes(st, i));
                }
                rows.Add(row);
            }
        }
        finally { sqlite3_finalize(st); }
        return rows;
    }

    static byte[] Z(string s) => Encoding.UTF8.GetBytes(s + "\0");

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetPackagesByPackageFamily(string family, ref uint count, char** fullNames, ref uint bufferLength, char* buffer);
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetPackagePathByFullName(string fullName, ref uint length, char* path);
    [LibraryImport("winsqlite3.dll")] private static partial int sqlite3_open_v2(byte[] file, out nint db, int flags, nint vfs);
    [LibraryImport("winsqlite3.dll")] private static partial int sqlite3_prepare_v2(nint db, byte[] sql, int n, out nint st, nint tail);
    [LibraryImport("winsqlite3.dll")] private static partial int sqlite3_step(nint st);
    [LibraryImport("winsqlite3.dll")] private static partial nint sqlite3_column_text(nint st, int i);
    [LibraryImport("winsqlite3.dll")] private static partial int sqlite3_column_bytes(nint st, int i);
    [LibraryImport("winsqlite3.dll")] private static partial int sqlite3_finalize(nint st);
    [LibraryImport("winsqlite3.dll")] private static partial int sqlite3_close(nint db);
}
