using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace SystemMenu;

// The data behind the launcher's own pages (Projects, Localhost, Reports, Shortcuts); the
// window draws them (MainWindow.Pages.cs). The clipboard's is in Background.cs.

// --- Projects ------------------------------------------------------------------------------
// Git repos on Windows (two levels under the folders below, and in your home folder or one
// folder down: ~/omawin, ~/src/glazewm) and in WSL Ubuntu (three levels under ~). Repos zoxide
// knows (visited often or recently) come first, then the rest by their latest git activity. WSL
// is only asked when it's already running (booting it takes ~8 s); otherwise its list from last
// time is used (wsl-projects.txt). The background index (Background.cs) searches again seconds
// after a repo appears or goes, and keeps the result in Latest.
public sealed record Project(bool Wsl, string Path, double Time, bool Visited)
{
    public string Name => Wsl ? Path.TrimEnd('/').Split('/')[^1] : System.IO.Path.GetFileName(Path.TrimEnd('\\'));
    public string Shown => Wsl ? Regex.Replace(Path, "^/home/[^/]+", "~") : Path.Replace(Projects.Home, "~");
}

public static class Projects
{
    public static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public const string Distro = "Ubuntu";
    static readonly string[] Roots = [Path.Combine(Home, "Documents"), Path.Combine(Home, "OneDrive", "Documents"), Path.Combine(Home, "source", "repos"), Path.Combine(Home, "repos")];
    static string WslCache => Path.Combine(MenuModel.StateDir, "wsl-projects.txt");

    // A program's output lines. wsl.exe prints its own messages in UTF-16, but what it runs
    // (bash) prints plain UTF-8.
    static string[] Run(string exe, string args, int timeoutMs = 10_000, bool utf16 = false)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                StandardOutputEncoding = utf16 ? System.Text.Encoding.Unicode : System.Text.Encoding.UTF8,
            })!;
            var output = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } return []; }
            return output.Result.Split('\n').Select(l => l.Trim('\r', '\0', ' ')).Where(l => l.Length > 0).ToArray();
        }
        catch { return []; }
    }

    // One search at a time: a caller that comes while one runs (the background index, the
    // launcher's home, the Projects page) gets that one, instead of all of them searching (and
    // racing to write wsl-projects.txt).
    static Task<List<Project>>? finding;
    static readonly object findLock = new();

    public static Task<List<Project>> Find()
    {
        lock (findLock) return finding is { IsCompleted: false } ? finding : finding = Task.Run(Search);
    }

    // The last search's result, for lists that shouldn't wait for a new one (the launcher's home).
    public static List<Project>? Latest { get; private set; }

    static List<Project> Search()
    {
        var visited = new HashSet<string>(Run("zoxide", "query -l"), StringComparer.OrdinalIgnoreCase);
        var list = new List<Project>();
        void Add(string dir)
        {
            if (list.Any(p => p.Path.Equals(dir, StringComparison.OrdinalIgnoreCase))) return;
            var git = Path.Combine(dir, ".git");
            list.Add(new Project(false, dir, new DateTimeOffset(Directory.GetLastWriteTimeUtc(git)).ToUnixTimeSeconds(), visited.Contains(dir)));
        }
        foreach (var root in Roots.Where(Directory.Exists))
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2, IgnoreInaccessible = true, AttributesToSkip = 0 };
            try
            {
                foreach (var git in Directory.EnumerateDirectories(root, ".git", options)) Add(Path.GetDirectoryName(git)!);
            }
            catch { }
        }
        foreach (var dir in Folders(Home))
        {
            if (IsRepo(dir)) { Add(dir); continue; }
            foreach (var sub in Folders(dir)) if (IsRepo(sub)) Add(sub);
        }
        list.AddRange(FindWsl());
        var found = list.OrderByDescending(p => p.Visited).ThenByDescending(p => p.Time).Take(40).ToList();
        Latest = found;
        return found;
    }

    // A worktree's .git is a file, so only real repos count (agents' worktrees stay out).
    static bool IsRepo(string dir) => Directory.Exists(Path.Combine(dir, ".git"));

    // A folder's subfolders, without hidden ones (AppData), dot folders (.cargo, .herdr) and
    // junctions (My Documents, Local Settings: old names for folders searched anyway).
    static List<string> Folders(string dir)
    {
        try
        {
            var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint };
            return [.. new DirectoryInfo(dir).EnumerateDirectories("*", options).Where(d => !d.Name.StartsWith('.')).Select(d => d.FullName)];
        }
        catch { return []; }
    }

    static IEnumerable<Project> FindWsl()
    {
        string[] lines;
        if (Run("wsl.exe", "-l --running -q", utf16: true).Contains(Distro))
        {
            const string script = "zoxide query -l 2>/dev/null; echo ---; find ~ -maxdepth 3 -name .git -type d -not -path '*/.cache/*' -not -path '*/.local/*' -printf '%T@ %h\\n' 2>/dev/null | sort -rn";
            lines = Run("wsl.exe", $"-d {Distro} -e bash -lc \"{script}\"", 15_000);
            if (lines.Contains("---")) { try { File.WriteAllLines(WslCache, lines); } catch { } }
        }
        else if (File.Exists(WslCache)) lines = File.ReadAllLines(WslCache);
        else yield break;
        int split = Array.IndexOf(lines, "---");
        var visited = split > 0 ? lines[..split].ToHashSet() : [];
        foreach (var line in lines.Skip(split + 1))
        {
            var parts = line.Split(' ', 2);
            if (parts.Length == 2 && double.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out var time))
                yield return new Project(true, parts[1], time, visited.Contains(parts[1]));
        }
    }

    // A project gets a terminal window (noctty: a herdr session) with a tab per tool, in the
    // project folder: launcher/project-tabs.txt. omawin opens it in the default terminal
    // (omawin launch project).
    public static void Open(Project project)
    {
        if (project.Wsl) MenuModel.Omawin("launch", "project", project.Path, "--wsl");
        else MenuModel.Omawin("launch", "project", project.Path);
    }
}

// --- Localhost -------------------------------------------------------------------------------
// The web servers running on this PC (and in WSL, which forwards its ports as "wslrelay"):
// ports something listens on (netstat), minus Windows' own and the bar / window manager, each
// asked for its home page at once (HTTP, then HTTPS; self-signed certificates are fine); only
// those that answer are listed, named by the page's <title>. Then the favorite sites
// (launcher/sites.txt, then your account folder's). Opened in Chrome.
public sealed record Server(int Port, string Program, int Pid, string Url, string Title);
public sealed record Site(string Icon, string Name, string Url);

public static class Localhost
{
    static readonly string[] Ignored = ["System", "svchost", "lsass", "wininit", "services", "spoolsv", "tailscaled", "glazewm", "zebar", "SystemMenu"];

    sealed record Port(int Number, string Program, int Pid, string Host);

    static List<Port> Listening()
    {
        var seen = new Dictionary<int, Port>();
        string text;
        try
        {
            using var p = Process.Start(new ProcessStartInfo("netstat", "-ano -p TCP") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
            text = p.StandardOutput.ReadToEnd();
            using var p6 = Process.Start(new ProcessStartInfo("netstat", "-ano -p TCPv6") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
            text += p6.StandardOutput.ReadToEnd();
        }
        catch { return []; }
        foreach (var line in text.Split('\n'))
        {
            var f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (f.Length < 5 || f[3] != "LISTENING") continue;
            var m = Regex.Match(f[1], @"^\[?(.*?)\]?:(\d+)$");
            if (!m.Success) continue;
            string address = m.Groups[1].Value;
            int port = int.Parse(m.Groups[2].Value);
            if (address is not ("0.0.0.0" or "127.0.0.1" or "::" or "::1") || seen.ContainsKey(port)) continue;
            if (!int.TryParse(f[4], out int pid)) continue;
            string program;
            try { program = Process.GetProcessById(pid).ProcessName; } catch { continue; }
            if (Ignored.Contains(program, StringComparer.OrdinalIgnoreCase)) continue;
            // Ask the address it really listens on ("localhost" tries IPv6 first, and a refused
            // connection on Windows takes ~2 s).
            seen[port] = new Port(port, program, pid, address is "0.0.0.0" or "127.0.0.1" ? "127.0.0.1" : "[::1]");
        }
        return [.. seen.Values.OrderBy(p => p.Number)];
    }

    // Run it off the UI thread (Task.Run): netstat runs before the first await.
    public static async Task<List<Server>> FindAsync()
    {
        var ports = Listening();
        using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator, AllowAutoRedirect = true };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(1500) };
        var found = new Dictionary<int, Server>();
        foreach (var scheme in new[] { "http", "https" })
        {
            var tries = ports.Where(p => !found.ContainsKey(p.Number)).Select(async p =>
            {
                try
                {
                    using var response = await client.GetAsync($"{scheme}://{p.Host}:{p.Number}/");
                    string? title = null;
                    try
                    {
                        var body = await response.Content.ReadAsStringAsync().WaitAsync(TimeSpan.FromMilliseconds(800));
                        var m = Regex.Match(body, @"<title[^>]*>\s*(.*?)\s*</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                        if (m.Success) title = Regex.Replace(System.Net.WebUtility.HtmlDecode(m.Groups[1].Value), @"\s+", " ");
                    }
                    catch { }
                    title ??= response.Headers.Server.FirstOrDefault()?.ToString();
                    if (string.IsNullOrWhiteSpace(title)) title = p.Program;
                    lock (found) found[p.Number] = new Server(p.Number, p.Program, p.Pid, $"{scheme}://localhost:{p.Number}/", title);
                }
                catch { }
            });
            await Task.WhenAll(tries);
        }
        return [.. found.Values.OrderBy(s => s.Port)];
    }

    public static List<Site> Sites()
    {
        try
        {
            // omawin's, then the shared folder's and this account's (MenuModel.Layered).
            return [.. MenuModel.LayeredLines("sites.txt")
                .Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith('#'))
                .Select(l => l.Split('|', 3).Select(p => p.Trim()).ToArray())
                .Select(p => p.Length == 3 ? new Site(p[0], p[1], p[2]) : new Site(char.ConvertFromUtf32(0xF059F), p[0], p.Length > 1 ? p[1] : p[0]))];
        }
        catch { return []; }
    }

    public static void Open(string url) =>
        Process.Start(new ProcessStartInfo("chrome", $"--profile-directory=Default \"{url}\"") { UseShellExecute = true });

    // Stops the program serving a port; for WSL servers (wslrelay on Windows), whatever listens
    // on that port inside WSL.
    public static void Stop(Server server)
    {
        try
        {
            if (server.Program == "wslrelay")
                Process.Start(new ProcessStartInfo("wsl.exe", $"-d {Projects.Distro} -e sh -c \"kill $(ss -tlnpH 'sport = :{server.Port}' | grep -o 'pid=[0-9]*' | cut -d= -f2 | sort -u) 2>/dev/null\"") { UseShellExecute = false, CreateNoWindow = true })?.WaitForExit(5000);
            else Process.GetProcessById(server.Pid).Kill();
        }
        catch { }
    }
}

// --- Reports ---------------------------------------------------------------------------------
// Files from your own folders and shares, newest first, for a reports script that isn't
// omawin's: ~/omawin-account/reports/reports.ps1 (MenuModel.Work). Without one, the page and its
// launcher item are hidden. The script keeps reports.tsv fresh (-Index: "<modified unix>
// TAB <size> TAB <tag> TAB <path>" lines; run by Background.cs) and handles a pick (-Import
// <path>, in a small terminal).
public sealed record Report(DateTime Modified, long Size, string Tag, string Path)
{
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
    public string Folder => System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(Path)) ?? "";
}

public static class Reports
{
    public static string Script => Path.Combine(MenuModel.Work, "reports", "reports.ps1");
    static string Cache => Path.Combine(MenuModel.StateDir, "reports.tsv");
    public static bool Available => File.Exists(Script);

    public static List<Report> Load()
    {
        try
        {
            return [.. File.ReadAllLines(Cache).Select(l => l.Split('\t', 4)).Where(p => p.Length == 4)
                .Select(p => new Report(DateTimeOffset.FromUnixTimeSeconds(long.Parse(p[0])).LocalDateTime, long.Parse(p[1]), p[2], p[3]))
                .OrderByDescending(r => r.Modified)];
        }
        catch { return []; }
    }

    public static string Age()
    {
        if (!File.Exists(Cache)) return "building the list, try again in a minute";
        var minutes = (int)(DateTime.Now - File.GetLastWriteTime(Cache)).TotalMinutes;
        return minutes < 2 ? "just updated" : minutes < 120 ? $"updated {minutes} min ago" : $"updated {minutes / 60} h ago";
    }

    public static void Import(Report report)
    {
        MenuModel.Omawin("launch", "terminal", "--title", "Import report", "--", "pwsh", "-NoLogo", "-NoProfile", "-File", Script, "-Import", report.Path);
    }
}

// --- Shortcuts -------------------------------------------------------------------------------
// The cheat sheet, launcher/keys.txt: "<section> | <keys> | <what it does>".
public sealed record Shortcut(string Section, string Keys, string What);

public static class Shortcuts
{
    public static List<Shortcut> Load()
    {
        try
        {
            // omawin's, then the shared folder's and this account's (MenuModel.Layered).
            return [.. MenuModel.LayeredLines("keys.txt")
                .Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith('#'))
                .Select(l => l.Split('|', 3).Select(p => p.Trim()).ToArray()).Where(p => p.Length == 3)
                .Select(p => new Shortcut(p[0], p[1], p[2]))];
        }
        catch { return []; }
    }
}

// --- VPN -------------------------------------------------------------------------------------
// A Windows VPN connection: rasphone -d opens Windows' own connect box already dialing; rasdial
// can't dial this kind, but it tells whether it's up, and disconnects it.
public static class Vpn
{
    public static bool Connected(string name)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("rasdial") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
            return p.StandardOutput.ReadToEnd().Split('\n').Any(l => l.Trim() == name);
        }
        catch { return false; }
    }

    // The connection's own icon, when it comes as a Connection Manager profile (a company's
    // VPN installer): Cm\<profile>.cmp names its .cms, whose ServiceName is the connection's
    // name and Icon its icon. Null for a plain Windows VPN connection.
    static readonly Dictionary<string, string?> icons = new(StringComparer.OrdinalIgnoreCase);

    public static string? IconFile(string name)
    {
        if (name.Length == 0) return null;
        lock (icons)
        {
            if (icons.TryGetValue(name, out var known)) return known;
            string? found = null;
            foreach (var root in new[] { Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.CommonApplicationData })
            {
                var cm = Path.Combine(Environment.GetFolderPath(root), "Microsoft", "Network", "Connections", "Cm");
                if (!Directory.Exists(cm)) continue;
                foreach (var cmp in Directory.GetFiles(cm, "*.cmp"))
                {
                    try
                    {
                        var cms = Ini(cmp, "CMSFile");
                        if (cms == null) continue;
                        cms = Path.Combine(cm, cms);
                        if (!string.Equals(Ini(cms, "ServiceName"), name, StringComparison.OrdinalIgnoreCase)) continue;
                        found = new[] { Ini(cms, "Icon"), Ini(cms, "SmallIcon") }
                            .Where(i => !string.IsNullOrEmpty(i)).Select(i => Path.Combine(cm, i!)).FirstOrDefault(File.Exists);
                        if (found != null) break;
                    }
                    catch { }
                }
                if (found != null) break;
            }
            return icons[name] = found;
        }
    }

    static string? Ini(string file, string key) =>
        File.ReadLines(file).Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))?[(key.Length + 1)..].Trim();

    public static void Connect(string name) => Process.Start(new ProcessStartInfo("rasphone", $"-d \"{name}\"") { UseShellExecute = true });

    public static void Disconnect(string name) =>
        Process.Start(new ProcessStartInfo("rasdial", $"\"{name}\" /disconnect") { UseShellExecute = false, CreateNoWindow = true });
}
