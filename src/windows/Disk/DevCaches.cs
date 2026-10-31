using System.Text.RegularExpressions;

namespace Omawin.Windows;

// Where a developer's disk goes (roadmap 8.3): the package managers' caches, build output left in
// old projects, and Linux's virtual disks, which never shrink on their own. Pure: what's a cache,
// what's build output, which folders may be emptied, and the wording. Commands/DiskCommand.cs
// measures and cleans.
public sealed record DevCache(string Name, string Folder, string? Clean, bool OldFilesOnly = false);

public static partial class DevCaches
{
    // Every cache that's re-downloaded or rebuilt when needed: nothing of yours lives in them. Clean
    // is the tool's own command when it has one (it knows its layout and locks); without it, or
    // without the tool on PATH, the folder's contents are deleted. Temp keeps the last week's files
    // (something running may be using them).
    public static IReadOnlyList<DevCache> All(Func<string, string?> env, string localAppData, string home, string temp)
    {
        // Joined as Windows paths whatever runs this (the tests run on Linux).
        static string J(string a, string b) => a.TrimEnd('\\') + "\\" + b;
        string L(string p) => J(localAppData, p);
        string H(string p) => J(home, p);
        string E(string name, string fallback) => env(name) is { Length: > 0 } v ? v : fallback;
        var cargo = E("CARGO_HOME", H(".cargo"));
        return
        [
            new("npm", E("npm_config_cache", L("npm-cache")), "npm cache clean --force"),
            new("pnpm", E("PNPM_STORE_DIR", L(@"pnpm\store")), "pnpm store prune"),
            new("Yarn", E("YARN_CACHE_FOLDER", L(@"Yarn\Cache")), "yarn cache clean"),
            new("Bun", E("BUN_INSTALL_CACHE_DIR", H(@".bun\install\cache")), "bun pm cache rm"),
            new("NuGet", E("NUGET_PACKAGES", H(@".nuget\packages")), "dotnet nuget locals global-packages --clear"),
            new("NuGet downloads", L(@"NuGet\v3-cache"), "dotnet nuget locals http-cache --clear"),
            new("pip", E("PIP_CACHE_DIR", L(@"pip\Cache")), "pip cache purge"),
            new("uv", E("UV_CACHE_DIR", L(@"uv\cache")), "uv cache clean"),
            new("cargo registry", J(cargo, "registry"), null),
            new("cargo git", J(cargo, "git"), null),
            new("Go build", E("GOCACHE", L("go-build")), "go clean -cache"),
            new("Go modules", E("GOMODCACHE", H(@"go\pkg\mod")), "go clean -modcache"),
            new("Gradle", J(E("GRADLE_USER_HOME", H(".gradle")), "caches"), null),
            new("Maven", H(@".m2\repository"), null),
            new("mise downloads", E("MISE_CACHE_DIR", L(@"mise\cache")), "mise cache clear"),
            new("Temp (older than a week)", temp, null, OldFilesOnly: true),
        ];
    }

    public static DevCache? Find(IEnumerable<DevCache> caches, string name)
    {
        var all = caches.ToList();
        return all.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? (all.Where(c => c.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase)).ToList() is [var only] ? only : null);
    }

    // A folder omawin may empty: it exists by a cache's name, deep enough that it can't be a drive,
    // the home folder, or a folder that holds them (AppData, Local). A cache set by an environment
    // variable to somewhere odd is measured but not emptied by hand.
    // Windows paths as text (tested on Linux too, where '\' isn't a separator).
    public static bool SafeToEmpty(string folder, string home, string localAppData)
    {
        static string Norm(string p) => p.Replace('/', '\\').TrimEnd('\\');
        var full = Norm(folder);
        int cut = full.LastIndexOf('\\');
        // A drive (C:), a top-level folder (C:\Users) or a share's root are never emptied.
        if (cut < 0 || full.Count(c => c == '\\') < 2 || full.StartsWith(@"\\", StringComparison.Ordinal) && full.Count(c => c == '\\') < 5) return false;
        var appData = Norm(localAppData);
        string[] never = [Norm(home), appData, appData.LastIndexOf('\\') is > 0 and var i ? appData[..i] : "", Norm(home) + @"\.cargo", Norm(home) + @"\go"];
        if (never.Any(n => n.Length > 0 && n.Equals(full, StringComparison.OrdinalIgnoreCase))) return false;
        var leaf = full[(cut + 1)..].ToLowerInvariant();
        return leaf is "npm-cache" or "store" or "cache" or "packages" or "v3-cache" or "registry" or "git" or "go-build" or "mod"
            or "caches" or "repository" or "temp" or "tmp" || leaf.Contains("cache");
    }

    // --- build output in projects ---------------------------------------------------------------

    // A folder is build output only beside the file that makes it (or, for .venv, with its own
    // marker inside), so a folder that merely has the name is never touched.
    public sealed record Junk(string Folder, string Marker, bool MarkerInside = false);

    public static readonly Junk[] Kinds =
    [
        new("node_modules", "package.json"),
        new("target", "Cargo.toml"),
        new(".venv", "pyvenv.cfg", MarkerInside: true),
        new("venv", "pyvenv.cfg", MarkerInside: true),
        new("bin", "*.csproj"), new("obj", "*.csproj"),
        new("bin", "*.fsproj"), new("obj", "*.fsproj"),
        new(".next", "next.config.*"),
        new(".turbo", "turbo.json"),
        new(".gradle", "settings.gradle*"),
        new("build", "build.gradle*"),
        new(".zig-cache", "build.zig"), new("zig-cache", "build.zig"),
        new("__pycache__", "*.py"),
    ];

    // Which kind of build output `folder` is, given the names in the folder that holds it and in
    // the folder itself; null when it isn't.
    public static Junk? Match(string folder, IReadOnlyCollection<string> besideIt, IReadOnlyCollection<string> insideIt) =>
        Kinds.FirstOrDefault(k => k.Folder.Equals(folder, StringComparison.OrdinalIgnoreCase)
            && (k.MarkerInside ? insideIt : besideIt).Any(n => Glob(k.Marker, n)));

    // Folders a walk never enters: build output itself (no node_modules inside node_modules),
    // version control, and the places where nothing is a project of yours.
    public static bool Skip(string folder) =>
        folder.StartsWith('.') || folder.StartsWith('$') || folder is "node_modules" or "AppData" or "Windows" or "Program Files" or "Program Files (x86)";

    public static bool Glob(string pattern, string name) =>
        Regex.IsMatch(name, "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$", RegexOptions.IgnoreCase);

    // --- wording --------------------------------------------------------------------------------

    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.0} GB",
    };

    // "today", "3 days ago", "5 weeks ago", "4 months ago", "2 years ago".
    public static string Ago(DateTime then, DateTime now)
    {
        var days = (int)(now - then).TotalDays;
        return days switch
        {
            < 1 => "today",
            1 => "yesterday",
            < 14 => $"{days} days ago",
            < 60 => $"{days / 7} weeks ago",
            < 730 => $"{days / 30} months ago",
            _ => $"{days / 365} years ago",
        };
    }

    // A WSL distro's disk from its registry entry (HKCU\…\Lxss\{guid}): BasePath, newer WSL with
    // the \\?\ prefix, and VhdFileName (ext4.vhdx when absent).
    public static string WslDisk(string basePath, string? vhdFileName) =>
        (basePath.StartsWith(@"\\?\", StringComparison.Ordinal) ? basePath[4..] : basePath).TrimEnd('\\') + @"\" + (vhdFileName is { Length: > 0 } f ? f : "ext4.vhdx");
}
