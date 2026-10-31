using System.IO.Enumeration;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Omawin.Windows;

// Measuring and emptying folders for omawin disk (8.3). Sizes through FileSystemEnumerable: one
// directory stream per folder, no extra call per file, links (junctions, symlinks) never followed,
// so nothing is counted twice and nothing outside the folder is reached.
public static partial class DiskScan
{
    [LibraryImport("kernel32.dll", EntryPoint = "GetCompressedFileSizeW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint GetCompressedFileSize(string path, out uint high);

    // What a file really takes on the disk: less than its length when it's sparse or compressed
    // (a sparse WSL disk), else the same.
    public static long OnDisk(string file)
    {
        uint low = GetCompressedFileSize(file, out var high);
        if (low == uint.MaxValue && Marshal.GetLastPInvokeError() != 0) return new FileInfo(file).Length;
        return ((long)high << 32) | low;
    }

    static readonly EnumerationOptions Walk = new()
    {
        RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint,
        ReturnSpecialDirectories = false, BufferSize = 64 * 1024,
    };

    public static long SizeOf(string folder, DateTime? olderThan = null)
    {
        if (!Directory.Exists(folder)) return 0;
        var cutoff = olderThan?.ToUniversalTime();
        return new FileSystemEnumerable<long>(folder, (ref FileSystemEntry e) => e.Length, Walk)
        {
            ShouldIncludePredicate = (ref FileSystemEntry e) => !e.IsDirectory && (cutoff == null || e.LastWriteTimeUtc < cutoff),
        }.Sum();
    }

    // A folder's contents deleted (the folder itself kept, as the tool would leave it), files in
    // use skipped. Links are removed as links, never followed. Returns the bytes freed.
    public static long Empty(string folder, DateTime? olderThan = null)
    {
        long freed = 0;
        if (!Directory.Exists(folder)) return 0;
        var top = new DirectoryInfo(folder);
        foreach (var entry in top.EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 }))
            freed += Remove(entry, olderThan?.ToUniversalTime());
        return freed;
    }

    static long Remove(FileSystemInfo entry, DateTime? cutoff)
    {
        try
        {
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                if (cutoff == null) entry.Delete(); // a junction or symlink: the link goes, its target stays
                return 0;
            }
            if (entry is FileInfo f)
            {
                if (cutoff != null && f.LastWriteTimeUtc >= cutoff) return 0;
                long size = f.Length;
                if (f.IsReadOnly) f.IsReadOnly = false;
                f.Delete();
                return size;
            }
            var d = (DirectoryInfo)entry;
            long freed = 0;
            foreach (var child in d.EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 }))
                freed += Remove(child, cutoff);
            if (cutoff == null || !d.EnumerateFileSystemInfos().Any()) d.Delete();
            return freed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; } // in use: left for next time
    }

    // A whole folder of build output deleted (node_modules, target, …).
    public static long Delete(string folder)
    {
        long freed = Empty(folder);
        try { Directory.Delete(folder); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return freed;
    }

    public sealed record Found(string Project, string Folder, string Kind, long Size, DateTime Touched);

    // Build output under the roots: each folder that's a known kind beside its marker, with the
    // project's last change (its newest top-level file, or git's index). Six levels deep at most.
    public static List<Found> Projects(IEnumerable<string> roots, int maxDepth = 6)
    {
        var found = new List<Found>();
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        void Visit(string dir, int depth)
        {
            List<string> children, files;
            try
            {
                children = Directory.EnumerateDirectories(dir, "*", options).ToList();
                files = Directory.EnumerateFiles(dir, "*", options).Select(f => Path.GetFileName(f)).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
            var beside = files.Concat(children.Select(c => Path.GetFileName(c)!)).ToList();
            foreach (var child in children)
            {
                var name = Path.GetFileName(child);
                List<string> inside = [];
                if (DevCaches.Kinds.Any(k => k.MarkerInside && k.Folder.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    try { inside = Directory.EnumerateFiles(child, "*", options).Select(f => Path.GetFileName(f)).ToList(); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                if (DevCaches.Match(name, beside, inside) is { } kind)
                    found.Add(new Found(dir, child, kind.Folder, SizeOf(child), Touched(dir, files)));
                else if (depth < maxDepth && !DevCaches.Skip(name))
                    Visit(child, depth + 1);
            }
        }
        foreach (var root in roots.Where(Directory.Exists)) Visit(root, 0);
        return found;
    }

    static DateTime Touched(string project, List<string> files)
    {
        var times = files.Select(f => File.GetLastWriteTime(Path.Combine(project, f))).ToList();
        var index = Path.Combine(project, ".git", "index");
        if (File.Exists(index)) times.Add(File.GetLastWriteTime(index));
        return times.Count > 0 ? times.Max() : Directory.GetLastWriteTime(project);
    }

    public sealed record LinuxDisk(string Name, string Vhd, long Size, bool Sparse);

    // WSL's distros (this account's, from the registry: no admin) and Docker Desktop's data disk.
    public static List<LinuxDisk> LinuxDisks(string localAppData)
    {
        var list = new List<LinuxDisk>();
        using (var lxss = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Lxss"))
            foreach (var id in lxss?.GetSubKeyNames() ?? [])
            {
                using var d = lxss!.OpenSubKey(id);
                if (d?.GetValue("DistributionName") is not string name || d.GetValue("BasePath") is not string basePath) continue;
                Add(name, DevCaches.WslDisk(basePath, d.GetValue("VhdFileName") as string));
            }
        // Newer Docker Desktop keeps its data here, older in wsl\data (check on a PC with each).
        Add("Docker Desktop", Path.Combine(localAppData, @"Docker\wsl\disk\docker_data.vhdx"));
        Add("Docker Desktop", Path.Combine(localAppData, @"Docker\wsl\data\ext4.vhdx"));
        return list;

        void Add(string name, string vhd)
        {
            var info = new FileInfo(vhd);
            if (info.Exists) list.Add(new LinuxDisk(name, vhd, info.Length, info.Attributes.HasFlag(FileAttributes.SparseFile)));
        }
    }
}
