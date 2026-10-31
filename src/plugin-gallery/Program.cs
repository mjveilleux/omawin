// The plugin gallery's index (roadmap 4.10), made in CI: Omarchy's marketplace (catalog.json),
// its most starred verified bar widgets downloaded at the reviewed commit and scanned for whether
// they run in omawin's QML host. Writes <out>/plugin-gallery.json (SystemMenu.Plugins.GalleryIndex).
//
//   dotnet run --project src/plugin-gallery -- <out> [--catalog <file or url>] [--limit <n>]
//                                                    [--omarchy <Omarchy's shell folder>]
//   dotnet run --project src/plugin-gallery -- --scan <plugin folder> [--omarchy <folder>]
//                                                    one plugin's verdict, for working on the host
//
// --omarchy: Omarchy's shell/ (a checkout's), so a plugin using one of its qs.Ui types the host
// lacks is caught; without it only imports and Quickshell's types are checked.
using System.IO.Compression;
using System.Text.Json;
using Omawin.PluginGallery;
using SystemMenu.Plugins;

string Option(string name, string fallback) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
var omarchy = Option("--omarchy", "");
var repo = FindRepo();
var host = HostCapabilities.Read(Path.Combine(repo, "src", "qs-host"), omarchy.Length > 0 ? omarchy : null);
if (Option("--scan", "") is { Length: > 0 } one)
{
    var found = Directory.EnumerateFiles(one, "*", SearchOption.AllDirectories)
        .Where(f => Path.GetExtension(f) is ".qml" or ".js" or ".mjs" or ".sh")
        .ToDictionary(f => Path.GetRelativePath(one, f).Replace('\\', '/'), File.ReadAllText);
    var scan = Scanner.Scan(found, host);
    Console.WriteLine($"{Path.GetFileName(Path.TrimEndingDirectorySeparator(one))}: {scan.Verdict}"
        + (scan.Needs.Count > 0 ? "; needs " + string.Join(", ", scan.Needs) : "")
        + (scan.Commands.Count > 0 ? "; runs " + string.Join(", ", scan.Commands) : ""));
    return 0;
}
if (args.Length < 1 || args[0].StartsWith('-'))
{
    Console.Error.WriteLine("usage: plugin-gallery <out> [--catalog <file or url>] [--limit <n>] [--omarchy <shell folder>] | --scan <plugin folder>");
    return 2;
}
var outDir = args[0];
var source = Option("--catalog", Catalog.Url);
var limit = int.Parse(Option("--limit", "300"));
Console.WriteLine($"host: {string.Join(", ", host.Modules.Order())}{(host.OmarchyTypes.Count > 0 ? $"; Omarchy's {host.OmarchyTypes.Count} qs types" : "; Omarchy's types not checked")}");

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("omawin-plugin-gallery");
var catalogText = source.StartsWith("https://") ? await http.GetStringAsync(source) : await File.ReadAllTextAsync(source);
using var catalog = JsonDocument.Parse(catalogText);
var picked = Catalog.Pick(catalog.RootElement, limit);
Console.WriteLine($"catalog: {picked.Count} verified bar widgets picked (of the most starred)");

// Eight at a time: GitHub's archive downloads are quick, and this is a few hundred small zips.
var results = new GalleryEntry?[picked.Count];
using var gate = new SemaphoreSlim(8);
await Task.WhenAll(picked.Select(async (plugin, i) =>
{
    await gate.WaitAsync();
    try
    {
        var zip = await http.GetByteArrayAsync($"{plugin.Repo.Replace("https://github.com/", "https://codeload.github.com/")}/zip/{plugin.Commit}");
        var (manifest, files) = Read(zip);
        if (manifest == null) { Console.WriteLine($"  skip {plugin.Repo}: no manifest.json at its top"); return; }
        results[i] = Catalog.Entry(plugin, manifest, Scanner.Scan(files, host));
        if (results[i] == null) Console.WriteLine($"  skip {plugin.Repo}: no bar widget, or an id omawin can't use");
    }
    catch (Exception ex) { Console.WriteLine($"  skip {plugin.Repo}: {ex.Message}"); }
    finally { gate.Release(); }
}));

// One per id (two plugins can share one): the more starred, which comes first.
var seen = new HashSet<string>(StringComparer.Ordinal);
var entries = results.Where(e => e != null && seen.Add(e.Id)).Select(e => e!).ToList();
var index = new GalleryIndex
{
    GeneratedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
    Source = source.StartsWith("https://") ? source : Catalog.Url,
    CatalogGeneratedAt = catalog.RootElement.TryGetProperty("generatedAt", out var g) ? g.GetString() ?? "" : "",
    Plugins = entries,
};
Directory.CreateDirectory(outDir);
await File.WriteAllTextAsync(Path.Combine(outDir, GalleryIndex.FileName), index.ToJson());
foreach (var verdict in Verdicts.All) Console.WriteLine($"  {verdict,-8} {entries.Count(e => e.Verdict == verdict)}");
// What's missing most: where the host's next modules would unlock the most plugins.
foreach (var need in entries.SelectMany(e => e.Needs).GroupBy(n => n).OrderByDescending(n => n.Count()).Take(12))
    Console.WriteLine($"  needs {need.Key}: {need.Count()}");
Console.WriteLine($"wrote {entries.Count} plugins to {Path.Combine(outDir, GalleryIndex.FileName)}");
return entries.Count > 0 ? 0 : 1;

// A GitHub archive: one top folder (<repo>-<sha>/) holding the plugin. Its manifest, and the
// files a scan reads (QML, JavaScript, shell scripts; each at most 1 MB).
static (string? Manifest, Dictionary<string, string> Files) Read(byte[] zip)
{
    using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
    string? manifest = null;
    var files = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var entry in archive.Entries)
    {
        var slash = entry.FullName.IndexOf('/');
        if (slash < 0 || entry.FullName.EndsWith('/') || entry.Length > 1 << 20) continue;
        var path = entry.FullName[(slash + 1)..];
        bool scanned = path.EndsWith(".qml") || path.EndsWith(".js") || path.EndsWith(".mjs") || path.EndsWith(".sh");
        if (path != "manifest.json" && !scanned) continue;
        using var reader = new StreamReader(entry.Open());
        var text = reader.ReadToEnd();
        if (path == "manifest.json") manifest = text;
        else files[path] = text;
    }
    return (manifest, files);
}

static string FindRepo()
{
    for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        if (Directory.Exists(Path.Combine(d.FullName, "src", "qs-host", "qml"))) return d.FullName;
    for (var d = new DirectoryInfo(Environment.CurrentDirectory); d != null; d = d.Parent)
        if (Directory.Exists(Path.Combine(d.FullName, "src", "qs-host", "qml"))) return d.FullName;
    throw new InvalidOperationException("src/qs-host not found: run this from omawin's repository");
}
