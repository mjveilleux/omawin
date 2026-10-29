using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SystemMenu;

// The data behind the launcher's Packages page (MainWindow.Packages.cs): three package managers'
// catalogs, searched with the launcher's fuzzy matching, like Omarchy's package picker.
//   winget       its whole catalog is on the PC (bin/pkg-index.ps1 reads it: ~16,000 packages)
//   Scoop        the buckets it has cloned (~/scoop/buckets/<bucket>/bucket/*.json), read here
//   Chocolatey   no catalog on the PC: its community feed is asked as you type
// What's installed: installed.txt (winget and Chocolatey, kept by launcher/menu.ps1) and Scoop's
// apps folder. Installing goes through launcher/menu.ps1 -Install, in a terminal you can watch.
public sealed record Package(string Source, string Id, string Name, string Version, string Detail)
{
    // What the fuzzy matching reads, and a lowercased copy for the quick check before it.
    public string Haystack => Name == Id ? Name : $"{Name} {Id}";
    public readonly string Lower = (Name == Id ? Name : $"{Name} {Id}").ToLowerInvariant();

    // The key installed.txt and menu.ps1 -Install use: winget:<id>, choco:<id>, scoop:<name>.
    public string Key => $"{Source}:{(Source == "scoop" ? Id.Split('/')[^1] : Id)}".ToLowerInvariant();
}

public static class Packages
{
    public static readonly string[] Sources = ["winget", "choco", "scoop"];

    public static string Label(string source) => source switch { "winget" => "winget", "choco" => "Chocolatey", _ => "Scoop" };

    // Ids handed to menu.ps1 -Install: what the three managers' ids look like, nothing that
    // could break out of a command line (each source checks the same again).
    static readonly Regex SafeId = new(@"^[A-Za-z0-9][A-Za-z0-9._+-]*(/[A-Za-z0-9][A-Za-z0-9._+-]*)?$");
    public static bool IsSafe(Package p) => SafeId.IsMatch(p.Id) && p.Id.Length < 200;

    static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    static string ScoopRoot => Environment.GetEnvironmentVariable("SCOOP") is { Length: > 0 } s ? s : Path.Combine(Home, "scoop");

    // Whether the manager is on this PC (Chocolatey and Scoop may well not be).
    public static bool Available(string source) => source switch
    {
        "winget" => OnPath("winget.exe"),
        "choco" => OnPath("choco.exe") || File.Exists(Path.Combine(Environment.GetEnvironmentVariable("ChocolateyInstall") ?? @"C:\ProgramData\chocolatey", "bin", "choco.exe")),
        _ => File.Exists(Path.Combine(ScoopRoot, "shims", "scoop.ps1")) || File.Exists(Path.Combine(ScoopRoot, "shims", "scoop.cmd")),
    };

    static bool OnPath(string exe) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Any(dir => { try { return File.Exists(Path.Combine(dir.Trim('"'), exe)); } catch { return false; } });

    // --- catalogs ------------------------------------------------------------------------
    static readonly Dictionary<string, Task<List<Package>>> loading = [];
    static readonly Dictionary<string, (string Stamp, List<Package> List)> loaded = [];
    static readonly Dictionary<string, DateTime> lastRead = [];
    static readonly object gate = new();

    // The catalog as last read (null while the first read runs), read again in the background
    // when it has changed; done is called on the caller's thread's queue when a read lands.
    public static List<Package>? Catalog(string source, Action done)
    {
        if (source == "choco") return null; // asked as you type: Search
        lock (gate)
        {
            loaded.TryGetValue(source, out var have);
            // Asked on every drawing (each key typed): looked at again at most once a minute.
            bool due = !lastRead.TryGetValue(source, out var at) || DateTime.Now - at > TimeSpan.FromMinutes(1);
            if (due && (!loading.TryGetValue(source, out var task) || task.IsCompleted))
            {
                lastRead[source] = DateTime.Now;
                var ui = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
                loading[source] = task = Task.Run(() => Read(source, have.Stamp));
                task.ContinueWith(t =>
                {
                    if (t.IsCompletedSuccessfully && t.Result is { } list && !ReferenceEquals(list, have.List)) ui?.TryEnqueue(() => done());
                });
            }
            return have.List;
        }
    }

    static List<Package> Read(string source, string? stamp)
    {
        try
        {
            var (now, list) = source == "winget" ? ReadWinget(stamp) : ReadScoop(stamp);
            if (list == null) lock (gate) return loaded.TryGetValue(source, out var same) ? same.List : [];
            lock (gate) loaded[source] = (now, list);
            return list;
        }
        catch (Exception ex) { App.Log(ex); return []; }
    }

    // winget: bin/pkg-index.ps1 writes winget-index.tsv (id, name, moniker, version, tags) when
    // winget's catalog changed, and prints its path. null: unchanged since stamp.
    static (string, List<Package>?) ReadWinget(string? stamp)
    {
        var file = Path.Combine(MenuModel.StateDir, "winget-index.tsv");
        try
        {
            using var p = Process.Start(new ProcessStartInfo("pwsh", $"-NoLogo -NoProfile -File \"{Path.Combine(MenuModel.Repo, "bin", "pkg-index.ps1")}\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
            p.WaitForExit(60_000);
        }
        catch (Exception ex) { App.Log(ex); }
        if (!File.Exists(file)) return ("", []);
        var now = File.GetLastWriteTimeUtc(file).Ticks.ToString();
        if (now == stamp) return (now, null);
        var list = new List<Package>();
        foreach (var line in File.ReadLines(file))
        {
            var f = line.Split('\t');
            if (f.Length < 2) continue;
            // Detail: the short name and the first tags, shown under the name.
            var tags = f.Length > 4 ? string.Join(", ", f[4].Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).Take(5)) : "";
            var detail = string.Join(" · ", new[] { f.Length > 2 ? f[2] : "", tags }.Where(s => s.Length > 0));
            list.Add(new Package("winget", f[0], f[1], f.Length > 3 ? f[3] : "", detail));
        }
        return (now, list);
    }

    // Scoop: every manifest in every bucket it has (bucket/<name>.json, or at the bucket's top
    // for old-style buckets). null: no bucket changed since stamp.
    static (string, List<Package>?) ReadScoop(string? stamp)
    {
        var buckets = Path.Combine(ScoopRoot, "buckets");
        if (!Directory.Exists(buckets)) return ("", []);
        var dirs = Directory.GetDirectories(buckets);
        // A bucket changes when Scoop pulls it: its git HEAD / FETCH_HEAD, or the folder itself.
        var now = string.Join(",", dirs.Select(d => new[] { Path.Combine(d, ".git", "FETCH_HEAD"), Path.Combine(d, ".git", "HEAD"), d }
            .Where(Path.Exists).Max(f => File.GetLastWriteTimeUtc(f).Ticks)));
        if (now == stamp) return (now, null);
        var list = new List<Package>();
        foreach (var dir in dirs)
        {
            var bucket = Path.GetFileName(dir);
            var manifests = Path.Combine(dir, "bucket");
            if (!Directory.Exists(manifests)) manifests = dir;
            foreach (var file in Directory.EnumerateFiles(manifests, "*.json"))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllBytes(file));
                    var root = doc.RootElement;
                    string Get(string key) => root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                    var name = Path.GetFileNameWithoutExtension(file);
                    list.Add(new Package("scoop", $"{bucket}/{name}", name, Get("version"), $"{bucket} · {Get("description")}"));
                }
                catch { } // a broken manifest: left out
            }
        }
        return (now, list);
    }

    // --- Chocolatey: its community feed, asked as you type ---------------------------------
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(10) };
    static readonly Dictionary<string, List<Package>> chocoCache = new(StringComparer.OrdinalIgnoreCase);

    public static async Task<List<Package>> SearchChoco(string query, CancellationToken cancel)
    {
        lock (chocoCache) if (chocoCache.TryGetValue(query, out var known)) return known;
        var term = Uri.EscapeDataString(query.Replace("'", "''"));
        var url = "https://community.chocolatey.org/api/v2/Search()?$filter=IsLatestVersion&$top=40" +
                  $"&searchTerm='{term}'&targetFramework=''&includePrerelease=false";
        var xml = await http.GetStringAsync(url, cancel);
        XNamespace atom = "http://www.w3.org/2005/Atom";
        XNamespace m = "http://schemas.microsoft.com/ado/2007/08/dataservices/metadata";
        XNamespace d = "http://schemas.microsoft.com/ado/2007/08/dataservices";
        var list = new List<Package>();
        foreach (var entry in XDocument.Parse(xml).Root?.Elements(atom + "entry") ?? [])
        {
            var props = entry.Element(m + "properties");
            string Get(string key) => props?.Element(d + key)?.Value ?? "";
            var id = Get("Id");
            if (id.Length == 0) id = entry.Element(atom + "title")?.Value ?? "";
            if (id.Length == 0) continue;
            var title = Get("Title");
            var summary = Get("Summary");
            if (summary.Length == 0) summary = Get("Description");
            list.Add(new Package("choco", id, title.Length > 0 ? title : id, Get("Version"), OneLine(summary, 160)));
        }
        lock (chocoCache) chocoCache[query] = list;
        return list;
    }

    static string OneLine(string text, int max)
    {
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text.Length > max ? text[..(max - 1)] + "…" : text;
    }

    // --- what's installed ----------------------------------------------------------------
    // installed.txt's winget: and choco: lines, and scoop:<app> for each app Scoop has.
    public static HashSet<string> Installed()
    {
        var set = new HashSet<string>(MenuModel.Installed(), StringComparer.OrdinalIgnoreCase);
        try
        {
            var apps = Path.Combine(ScoopRoot, "apps");
            if (Directory.Exists(apps))
                foreach (var dir in Directory.GetDirectories(apps))
                    if (Path.GetFileName(dir) is var name && name != "scoop") set.Add("scoop:" + name.ToLowerInvariant());
        }
        catch { }
        return set;
    }

    // --- details, for the preview --------------------------------------------------------
    static readonly Dictionary<string, Task<string>> details = [];

    // What the manager says about the package (publisher, description, homepage, license):
    // winget's `winget show` takes a second or two; Scoop's manifest and Chocolatey's feed
    // entry are at hand.
    public static Task<string> Details(Package p)
    {
        lock (details)
        {
            if (details.TryGetValue(p.Key, out var known)) return known;
            var task = Task.Run(() => p.Source switch
            {
                "winget" => WingetShow(p.Id),
                "scoop" => ScoopManifest(p.Id),
                _ => p.Detail,
            });
            details[p.Key] = task;
            return task;
        }
    }

    static string WingetShow(string id)
    {
        if (!SafeId.IsMatch(id)) return "";
        try
        {
            using var proc = Process.Start(new ProcessStartInfo("winget", $"show --id {id} --exact --accept-source-agreements --disable-interactivity")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, StandardOutputEncoding = System.Text.Encoding.UTF8 })!;
            var output = proc.StandardOutput.ReadToEndAsync();
            if (!proc.WaitForExit(20_000)) { try { proc.Kill(); } catch { } return ""; }
            // The lines worth reading: who makes it, what it is, where it lives, its license.
            var keep = new Regex(@"^(Publisher|Author|Description|Homepage|License|Release Notes Url):");
            var lines = new List<string>();
            bool on = false;
            foreach (var raw in output.Result.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (Regex.IsMatch(line, @"^\w[\w ]+:")) on = keep.IsMatch(line);
                else if (line.Length > 0 && !char.IsWhiteSpace(line[0])) on = false;
                if (on) lines.Add(line.Trim());
            }
            return string.Join("\n", lines);
        }
        catch (Exception ex) { App.Log(ex); return ""; }
    }

    static string ScoopManifest(string id)
    {
        var parts = id.Split('/');
        if (parts.Length != 2) return "";
        var dir = Path.Combine(ScoopRoot, "buckets", parts[0]);
        var file = Path.Combine(dir, "bucket", parts[1] + ".json");
        if (!File.Exists(file)) file = Path.Combine(dir, parts[1] + ".json");
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(file));
            var root = doc.RootElement;
            string Get(string key)
            {
                if (!root.TryGetProperty(key, out var v)) return "";
                return v.ValueKind switch
                {
                    JsonValueKind.String => v.GetString() ?? "",
                    // license: { "identifier": "MIT", "url": ... }
                    JsonValueKind.Object when v.TryGetProperty("identifier", out var i) => i.GetString() ?? "",
                    _ => "",
                };
            }
            return string.Join("\n", new[] { ("Description", Get("description")), ("Homepage", Get("homepage")), ("License", Get("license")), ("Bucket", parts[0]) }
                .Where(x => x.Item2.Length > 0).Select(x => $"{x.Item1}: {x.Item2}"));
        }
        catch { return ""; }
    }

    // --- installing ----------------------------------------------------------------------
    // In a terminal you can watch (launcher/menu.ps1 -Install), one after another; Chocolatey's
    // ask for admin first. installed.txt is updated as each finishes.
    public static void Install(IEnumerable<Package> packages)
    {
        var keys = packages.Where(IsSafe).Select(p => $"{p.Source}:{p.Id}").ToList();
        if (keys.Count == 0) return;
        RunMenu($"-Install \"{string.Join(";", keys)}\"");
    }

    // The manager itself, when it isn't on this PC: Chocolatey from winget (it asks for admin),
    // Scoop with its own installer (get.scoop.sh).
    public static void InstallManager(string source) => RunMenu($"-InstallManager {source}");

    static void RunMenu(string args) =>
        Process.Start(new ProcessStartInfo("pwsh", $"-NoLogo -NoProfile -WindowStyle Hidden -File \"{Path.Combine(MenuModel.Repo, "launcher", "menu.ps1")}\" {args}")
        { UseShellExecute = false, CreateNoWindow = true });
}
