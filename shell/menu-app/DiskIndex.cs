using System.IO.Enumeration;
using System.Runtime.InteropServices;

namespace SystemMenu;

// Whole-disk search (roadmap 8.2), the no-admin way: every file and folder name on the fixed
// drives, found by a directory walk at idle and kept current from Windows' change notifications
// (Everywhere.cs feeds Created / Deleted / Renamed in). The NTFS journal would be faster still but
// needs a volume handle, so admin (open-questions/disk-index-usn.md).
//
// Built to be small and quick over a million names: one char pool for every name, and per entry
// its parent, where its name is, a flag byte and its children's block (a walk adds a folder's
// children one after another, so finding a child is a scan of its siblings, not of the disk).
// Children added later by a change go in a small map per folder. Nothing is ever moved: a removed
// entry is flagged gone, and the next walk (each start, at idle) starts clean.
// Pure (files and spans, no Windows calls), so it's tested anywhere (DiskIndexTests).
public sealed class DiskIndex
{
    const byte IsDir = 1, IsGone = 2;
    static readonly char Sep = Path.DirectorySeparatorChar;

    int count;
    int[] parent = new int[1024];
    int[] nameAt = new int[1024];
    ushort[] nameLength = new ushort[1024];
    byte[] flags = new byte[1024];
    int[] firstChild = new int[1024];
    int[] childCount = new int[1024];
    char[] pool = new char[16 * 1024];
    int poolUsed;
    // Children added after the walk, by name, per folder.
    readonly Dictionary<int, Dictionary<string, int>> later = [];
    readonly List<int> roots = [];

    // When the walk that made it finished (UTC).
    public DateTime Built { get; private set; }
    // Entries, the gone ones included; Live counts what's there now (roots aside).
    public int Count => count;
    public int Live
    {
        get
        {
            int live = 0;
            for (int i = 0; i < count; i++) if (parent[i] >= 0 && (flags[i] & IsGone) == 0) live++;
            return live;
        }
    }
    public IReadOnlyList<string> Roots => [.. roots.Select(r => RootPath(r))];

    // What the walk leaves out: folder or file names (any depth, any case) and full paths.
    public sealed class Skip
    {
        readonly HashSet<string> names;
        readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> byName;
        readonly string[] paths;

        public Skip(IEnumerable<string> names, IEnumerable<string>? paths = null)
        {
            this.names = new HashSet<string>(names.Where(n => n.Length > 0), StringComparer.OrdinalIgnoreCase);
            byName = this.names.GetAlternateLookup<ReadOnlySpan<char>>();
            this.paths = [.. (paths ?? []).Select(p => p.TrimEnd('\\', '/')).Where(p => p.Length > 0)];
        }

        public bool Name(ReadOnlySpan<char> name) => byName.Contains(name);
        public bool Path(string path) => paths.Any(p => path.Equals(p, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(p + Sep, StringComparison.OrdinalIgnoreCase));
        // A changed path inside something skipped (a node_modules anywhere above it).
        public bool Under(string path)
        {
            if (Path(path)) return true;
            foreach (var part in path.Split('\\', '/')) if (part.Length > 0 && names.Contains(part)) return true;
            return false;
        }
    }

    // --- building ----------------------------------------------------------------------------

    // Every name under the roots (C:\, D:\), folder by folder. Junctions and symbolic links are
    // listed but not followed (they'd loop, or count a folder twice); cloud files (OneDrive's
    // placeholders, also reparse points) are folders like any other. Folders it may not read are
    // left out quietly. Cancelled: what's walked so far is thrown away by the caller.
    public static DiskIndex Walk(IEnumerable<string> rootPaths, Skip skip, CancellationToken cancel = default)
    {
        var index = new DiskIndex();
        foreach (var root in rootPaths)
        {
            var full = Path.GetFullPath(root);
            var name = full.Length > 1 ? full.TrimEnd('\\', '/') : full;
            if (name.Length == 0 || !Directory.Exists(full)) continue;
            int r = index.Add(-1, name, dir: true);
            index.roots.Add(r);
            index.Descend(r, full, skip, cancel);
        }
        index.Built = DateTime.UtcNow;
        return index;
    }

    static readonly EnumerationOptions OneFolder = new()
    {
        RecurseSubdirectories = false, IgnoreInaccessible = true, AttributesToSkip = 0,
        ReturnSpecialDirectories = false, BufferSize = 64 * 1024,
    };

    // A folder and everything under it, depth first with a stack (no recursion: a deep tree can't
    // overflow it).
    void Descend(int start, string startPath, Skip skip, CancellationToken cancel)
    {
        var stack = new Stack<(int Slot, string Path)>();
        stack.Push((start, startPath));
        var folders = new List<(int Slot, string Name, bool Link)>();
        while (stack.Count > 0)
        {
            cancel.ThrowIfCancellationRequested();
            var (dir, path) = stack.Pop();
            int first = count;
            folders.Clear();
            try
            {
                var entries = new FileSystemEnumerable<int>(path, (ref FileSystemEntry e) =>
                {
                    var name = e.FileName;
                    if (skip.Name(name)) return -1;
                    int slot = Add(dir, name, e.IsDirectory);
                    if (e.IsDirectory) folders.Add((slot, name.ToString(), (e.Attributes & FileAttributes.ReparsePoint) != 0));
                    return slot;
                }, OneFolder);
                foreach (var _ in entries) { }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
            if (count > first) { firstChild[dir] = first; childCount[dir] = count - first; }
            // Pushed in reverse, so they're walked in the order listed.
            for (int k = folders.Count - 1; k >= 0; k--)
            {
                var (slot, name, link) = folders[k];
                string child = path.EndsWith(Sep) ? path + name : path + Sep + name;
                if (skip.Path(child)) { flags[slot] |= IsGone; continue; }
                if (link && IsLink(child)) continue;
                stack.Push((slot, child));
            }
        }
    }

    static bool IsLink(string folder)
    {
        try { return new DirectoryInfo(folder).LinkTarget != null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return true; }
    }

    int Add(int parentSlot, ReadOnlySpan<char> name, bool dir)
    {
        if (count == parent.Length)
        {
            int size = count * 2;
            Array.Resize(ref parent, size);
            Array.Resize(ref nameAt, size);
            Array.Resize(ref nameLength, size);
            Array.Resize(ref flags, size);
            Array.Resize(ref firstChild, size);
            Array.Resize(ref childCount, size);
        }
        int slot = count++;
        parent[slot] = parentSlot;
        nameAt[slot] = Store(name);
        nameLength[slot] = (ushort)Math.Min(name.Length, ushort.MaxValue);
        flags[slot] = dir ? IsDir : (byte)0;
        firstChild[slot] = 0;
        childCount[slot] = 0;
        return slot;
    }

    int Store(ReadOnlySpan<char> name)
    {
        if (poolUsed + name.Length > pool.Length) Array.Resize(ref pool, Math.Max(pool.Length * 2, poolUsed + name.Length));
        int at = poolUsed;
        name.CopyTo(pool.AsSpan(at));
        poolUsed += name.Length;
        return at;
    }

    ReadOnlySpan<char> Name(int slot) => pool.AsSpan(nameAt[slot], nameLength[slot]);

    // --- paths -------------------------------------------------------------------------------

    string RootPath(int root)
    {
        var name = Name(root).ToString();
        // "C:" is the drive's root as C:\; a plain folder root is itself.
        return name.Length == 2 && name[1] == ':' ? name + Sep : name;
    }

    // The full path of an entry: its names up to its root.
    public string PathOf(int slot)
    {
        var parts = new List<int>();
        for (int s = slot; s >= 0; s = parent[s]) parts.Add(s);
        var text = new System.Text.StringBuilder(260);
        for (int k = parts.Count - 1; k >= 0; k--)
        {
            if (text.Length > 0 && text[^1] != Sep) text.Append(Sep);
            text.Append(Name(parts[k]));
        }
        if (parts.Count == 1) return RootPath(slot);
        return text.ToString();
    }

    // There now: neither it nor a folder above it was removed.
    public bool Alive(int slot)
    {
        for (int s = slot; s >= 0; s = parent[s]) if ((flags[s] & IsGone) != 0) return false;
        return true;
    }

    public bool Folder(int slot) => (flags[slot] & IsDir) != 0;

    // The entry at a path, or -1.
    public int Find(string path)
    {
        path = path.TrimEnd('\\', '/');
        foreach (var root in roots)
        {
            var rootName = Name(root);
            if (!path.AsSpan().StartsWith(rootName, StringComparison.OrdinalIgnoreCase)) continue;
            var rest = path.AsSpan(rootName.Length);
            if (rest.Length > 0 && rest[0] is not ('\\' or '/')) continue;
            int at = root;
            foreach (var range in rest.Split(Sep))
            {
                var part = rest[range];
                if (part.Length == 0) continue;
                at = Child(at, part);
                if (at < 0) return -1;
            }
            return at;
        }
        return -1;
    }

    int Child(int dir, ReadOnlySpan<char> name)
    {
        if (later.TryGetValue(dir, out var added) && added.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(name, out var slot)
            && (flags[slot] & IsGone) == 0) return slot;
        int end = firstChild[dir] + childCount[dir];
        for (int i = firstChild[dir]; i < end; i++)
            if ((flags[i] & IsGone) == 0 && Name(i).Equals(name, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    // --- changes -----------------------------------------------------------------------------

    // A file or folder appeared. A folder is walked too: one moved in arrives as a single event.
    // Returns false when its folder isn't in the index (outside the roots, or skipped).
    public bool Created(string path, bool dir, Skip skip)
    {
        path = path.TrimEnd('\\', '/');
        var name = Path.GetFileName(path);
        var folder = Path.GetDirectoryName(path);
        if (name.Length == 0 || folder == null || skip.Name(name) || skip.Under(path)) return false;
        int p = Find(folder);
        if (p < 0 || !Folder(p)) return false;
        if (Child(p, name) >= 0) return true;
        int slot = Add(p, name, dir);
        if (!later.TryGetValue(p, out var added)) later[p] = added = new(StringComparer.OrdinalIgnoreCase);
        added[name] = slot;
        if (dir) Descend(slot, path, skip, default);
        return true;
    }

    public bool Deleted(string path)
    {
        int slot = Find(path);
        if (slot < 0 || parent[slot] < 0) return false;
        flags[slot] |= IsGone;
        return true;
    }

    // Renamed in place (Windows reports a rename only within one folder); anything else is a
    // removal and an arrival.
    public void Renamed(string from, string to, bool dir, Skip skip)
    {
        int slot = Find(from);
        string? fromFolder = Path.GetDirectoryName(from.TrimEnd('\\', '/')), toFolder = Path.GetDirectoryName(to.TrimEnd('\\', '/'));
        var name = Path.GetFileName(to.TrimEnd('\\', '/'));
        if (slot >= 0 && parent[slot] >= 0 && fromFolder != null && toFolder != null
            && fromFolder.Equals(toFolder, StringComparison.OrdinalIgnoreCase) && name.Length > 0
            && !skip.Name(name) && !skip.Under(to))
        {
            int p = parent[slot];
            bool inBlock = slot >= firstChild[p] && slot < firstChild[p] + childCount[p];
            if (!inBlock && later.TryGetValue(p, out var added)) added.Remove(Name(slot).ToString());
            nameAt[slot] = Store(name);
            nameLength[slot] = (ushort)Math.Min(name.Length, ushort.MaxValue);
            // Findable under its new name: the walk's block is scanned by name anyway; a child
            // added later is filed again.
            if (!inBlock)
            {
                if (!later.TryGetValue(p, out var map)) later[p] = map = new(StringComparer.OrdinalIgnoreCase);
                map[name] = slot;
            }
            return;
        }
        if (slot >= 0) Deleted(from);
        Created(to, dir, skip);
    }

    // --- searching ---------------------------------------------------------------------------

    public sealed record Found(int Slot, string Path, string Name, bool IsDir, double Score);

    // The names matching every word of the query, best first. A word is found anywhere in the
    // name, any case ("rep" finds Report.docx); *.png keeps only those types (any of them); a word
    // with a \ also has to be in the path (src\main). Ranked as the finder ranks fff's files
    // (FileSearch): by fzy's score on the name, folders first, short names before long; then
    // what's under home (preferred) before the rest of the disk, and shallow before deep.
    public List<Found> Search(string query, int limit, bool foldersOnly = false, string? preferred = null, CancellationToken cancel = default)
    {
        var words = new List<string>();
        var types = new List<string>();
        var pathWords = new List<string>();
        foreach (var raw in query.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (raw.StartsWith("*.") && raw.Length > 2) types.Add(raw[1..]);
            else if (raw.Contains('\\') || raw.Contains('/'))
            {
                var w = raw.Replace('/', '\\');
                pathWords.Add(w);
                // Its last piece is still a name word: src\ma finds main.rs in src.
                var last = w[(w.LastIndexOf('\\') + 1)..];
                if (last.Length > 0) words.Add(last);
            }
            else words.Add(raw);
        }
        if (words.Count == 0 && types.Count == 0 && pathWords.Count == 0) return [];

        // Scanned in chunks on every core: a million names take a few tens of milliseconds.
        const int Chunk = 1 << 16;
        int chunks = (count + Chunk - 1) / Chunk;
        var parts = new List<int>[chunks];
        Parallel.For(0, chunks, new ParallelOptions { CancellationToken = cancel }, c =>
        {
            var hits = new List<int>();
            int end = Math.Min(count, (c + 1) * Chunk);
            for (int i = c * Chunk; i < end; i++)
            {
                byte f = flags[i];
                if ((f & IsGone) != 0 || parent[i] < 0 || foldersOnly && (f & IsDir) == 0) continue;
                var name = Name(i);
                if (types.Count > 0)
                {
                    if ((f & IsDir) != 0) continue;
                    bool typed = false;
                    foreach (var t in types) if (name.EndsWith(t, StringComparison.OrdinalIgnoreCase)) { typed = true; break; }
                    if (!typed) continue;
                }
                bool all = true;
                foreach (var w in words) if (!name.Contains(w, StringComparison.OrdinalIgnoreCase)) { all = false; break; }
                if (all) hits.Add(i);
            }
            parts[c] = hits;
        });

        // Too many to score them all ("a"): the shortest names first, which is where good matches are.
        var candidates = parts.SelectMany(p => p).ToList();
        if (candidates.Count > 20000) candidates = [.. candidates.OrderBy(i => nameLength[i]).Take(20000)];

        string fuzzy = string.Join(' ', words);
        int home = preferred != null ? Find(preferred) : -1;
        var ranked = new List<(Found Hit, bool Home, int Depth)>();
        foreach (var i in candidates)
        {
            cancel.ThrowIfCancellationRequested();
            if (!Alive(i)) continue;
            string path = PathOf(i);
            if (pathWords.Any(w => !path.Replace('/', '\\').Contains(w, StringComparison.OrdinalIgnoreCase))) continue;
            string name = Name(i).ToString();
            double score = fuzzy.Length == 0 ? 0 : Fuzzy.Match(name, fuzzy);
            if (score == Fuzzy.None) score = 0;
            int depth = 0;
            bool under = false;
            for (int s = parent[i]; s >= 0; s = parent[s]) { depth++; if (s == home) under = true; }
            ranked.Add((new Found(i, path, name, Folder(i), score), under, depth));
        }
        return [.. ranked
            .OrderByDescending(x => x.Hit.IsDir)
            .ThenByDescending(x => x.Hit.Score - 0.02 * x.Hit.Name.Length)
            .ThenByDescending(x => x.Home)
            .ThenBy(x => x.Depth)
            .ThenBy(x => x.Hit.Path, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Hit).Take(limit)];
    }

    // --- the file ----------------------------------------------------------------------------
    // %LOCALAPPDATA%\launcher\disk-index\index.bin: a header, then the arrays as they are in
    // memory (a million names load in well under a second), then the later children.

    const string Magic = "omawin-disk-index";
    const int Version = 1;

    public void Save(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        string temp = file + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
        using (var w = new BinaryWriter(stream))
        {
            w.Write(Magic);
            w.Write(Version);
            w.Write(Built.Ticks);
            w.Write(count);
            w.Write(poolUsed);
            w.Write(MemoryMarshal.AsBytes(parent.AsSpan(0, count)));
            w.Write(MemoryMarshal.AsBytes(nameAt.AsSpan(0, count)));
            w.Write(MemoryMarshal.AsBytes(nameLength.AsSpan(0, count)));
            w.Write(flags.AsSpan(0, count));
            w.Write(MemoryMarshal.AsBytes(firstChild.AsSpan(0, count)));
            w.Write(MemoryMarshal.AsBytes(childCount.AsSpan(0, count)));
            w.Write(MemoryMarshal.AsBytes(pool.AsSpan(0, poolUsed)));
            w.Write(roots.Count);
            foreach (var r in roots) w.Write(r);
            w.Write(later.Count);
            foreach (var (dir, added) in later)
            {
                w.Write(dir);
                w.Write(added.Count);
                foreach (var slot in added.Values) w.Write(slot);
            }
        }
        File.Move(temp, file, overwrite: true);
    }

    // The saved index, or null when there's none or it isn't one this version reads.
    public static DiskIndex? Load(string file)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
            using var r = new BinaryReader(stream);
            if (r.ReadString() != Magic || r.ReadInt32() != Version) return null;
            var index = new DiskIndex { Built = new DateTime(r.ReadInt64(), DateTimeKind.Utc) };
            int n = r.ReadInt32(), chars = r.ReadInt32();
            if (n < 0 || chars < 0) return null;
            int size = Math.Max(1024, n);
            index.parent = Read<int>(r, n, size);
            index.nameAt = Read<int>(r, n, size);
            index.nameLength = Read<ushort>(r, n, size);
            index.flags = Read<byte>(r, n, size);
            index.firstChild = Read<int>(r, n, size);
            index.childCount = Read<int>(r, n, size);
            index.pool = Read<char>(r, chars, Math.Max(16 * 1024, chars));
            index.count = n;
            index.poolUsed = chars;
            int rootCount = r.ReadInt32();
            for (int k = 0; k < rootCount; k++) index.roots.Add(r.ReadInt32());
            int laterCount = r.ReadInt32();
            for (int k = 0; k < laterCount; k++)
            {
                int dir = r.ReadInt32(), m = r.ReadInt32();
                var added = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int j = 0; j < m; j++)
                {
                    int slot = r.ReadInt32();
                    if ((uint)slot < (uint)n) added[index.Name(slot).ToString()] = slot;
                }
                index.later[dir] = added;
            }
            // A damaged file is no index: every reference has to land inside it.
            for (int i = 0; i < n; i++)
                if (index.parent[i] >= n || index.parent[i] < -1 || index.nameAt[i] < 0 || index.nameAt[i] + index.nameLength[i] > chars
                    || index.childCount[i] < 0 || index.firstChild[i] < 0 || index.firstChild[i] + index.childCount[i] > n)
                    return null;
            if (index.roots.Any(x => (uint)x >= (uint)n)) return null;
            return index;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EndOfStreamException or FormatException) { return null; }
    }

    static T[] Read<T>(BinaryReader r, int n, int size) where T : struct
    {
        var array = new T[size];
        var bytes = MemoryMarshal.AsBytes(array.AsSpan(0, n));
        // A stream may hand back less than asked: read until it's full.
        for (int got = 0; got < bytes.Length;)
        {
            int k = r.Read(bytes[got..]);
            if (k == 0) throw new EndOfStreamException();
            got += k;
        }
        return array;
    }
}
