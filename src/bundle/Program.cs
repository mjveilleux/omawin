using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Omawin.Bundle;

// dotnet run --project build/bundle -- <out dir> [tools.txt]
// Downloads each tool's latest upstream build, repacks it (src/bundle/Tools.cs says how) and
// writes omawin-tools.txt, the manifest the install reads. GITHUB_TOKEN, when set, lifts
// GitHub's rate limit for anonymous API calls.
// --pack <folder> <out dir> <n>: just the parts of one folder, as a tool with parts:<n> gets them
// (to check them by hand: tar -xf on each, side by side).
if (args is ["--pack", var packDir, var packOut, var packN])
{
    var root = Path.GetFullPath(packDir);
    Directory.CreateDirectory(packOut);
    var all = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'));
    var folders = Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).Select(d => Path.GetRelativePath(root, d).Replace('\\', '/')).Order(StringComparer.Ordinal).ToList();
    var groups = Tools.Split(all, int.Parse(packN));
    for (int k = 0; k < groups.Count; k++)
    {
        var part = Path.Combine(packOut, Tools.PartName(Path.GetFileName(root), k + 1));
        await Pack(root, k == 0 ? folders : [], groups[k], part, CancellationToken.None);
        Console.WriteLine($"  {Path.GetFileName(part)}  {groups[k].Count} files  {new FileInfo(part).Length / 1048576.0:0.0} MB");
    }
    return 0;
}
if (args.Length < 1) { Console.Error.WriteLine("usage: <out dir> [install/tools.txt]"); return 2; }
var outDir = Path.GetFullPath(args[0]);
var listFile = args.Length > 1 ? args[1] : Path.Combine(FindRepo(), "install", "tools.txt");
Directory.CreateDirectory(outDir);
var tools = Tools.Parse(File.ReadAllLines(listFile));

using var http = new HttpClient(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All }) { Timeout = TimeSpan.FromMinutes(10) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("omawin-bundle");
var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? Environment.GetEnvironmentVariable("GH_TOKEN");

var work = Directory.CreateTempSubdirectory("omawin-bundle-").FullName;
var built = new Built?[tools.Count];
try
{
    // Several at once: each is a download and an unzip, mostly waiting on the network.
    await Parallel.ForEachAsync(Enumerable.Range(0, tools.Count), new ParallelOptions { MaxDegreeOfParallelism = 6 }, async (i, ct) =>
    {
        // Up to three tries, 5 then 15 seconds apart: a server's passing 500 or a dropped
        // connection shouldn't fail the release (it did, once, on a GitHub 500).
        for (int attempt = 1; ; attempt++)
        {
            try { await Build(i, ct); return; }
            catch (Exception ex) when (attempt < 3 && ex is HttpRequestException or IOException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                Console.WriteLine($"  {tools[i].Name}: {ex.Message} (try {attempt} of 3; again in {(attempt == 1 ? 5 : 15)} s)");
                await Task.Delay(TimeSpan.FromSeconds(attempt == 1 ? 5 : 15), ct);
            }
        }
    });
}
finally { Directory.Delete(work, recursive: true); }

var manifest = Path.Combine(outDir, Tools.Manifest);
File.WriteAllLines(manifest, built.Select(b => b!.Line));
Console.WriteLine($"  {Path.GetFileName(manifest)}: {tools.Count} tools, {built.Sum(b => new FileInfo(Path.Combine(outDir, b!.File)).Length) / 1048576.0:0} MB");
return 0;

// One tool: found, downloaded, repacked into the output folder, its manifest row noted.
async Task Build(int i, CancellationToken ct)
{
    var t = tools[i];
    var (url, version, name) = await Resolve(t, ct);
    var dir = Path.Combine(work, t.Name);
    Directory.CreateDirectory(dir);
    var download = Path.Combine(dir, name);
    using (var res = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
    {
        res.EnsureSuccessStatusCode();
        await using var f = File.Create(download);
        await res.Content.CopyToAsync(f, ct);
    }
    var zip = Path.Combine(outDir, Tools.FileName(t.Name, t.Sfx));
    File.Delete(zip);
    var stage = Path.Combine(dir, "stage");
    if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
    Directory.CreateDirectory(stage);
    if (t.Sfx)
    {
        // A self-extractor goes out as it is: its LZMA is far smaller than a zip of what's
        // inside (Git for Windows: ~60 MB against ~160), and the PC runs it to unpack. With
        // parts, it's the fallback, so it's opened here too (7-Zip opens it like an archive).
        File.Copy(download, zip);
        if (t.Parts > 0) Extract7z(t, download, stage);
    }
    else
    {
        Unpack(t, download, stage);
        ZipFile.CreateFromDirectory(stage, zip, CompressionLevel.Optimal, includeBaseDirectory: false);
    }
    var parts = new List<(string, string)>();
    if (t.Parts > 0)
    {
        var files = Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(stage, f).Replace('\\', '/'));
        var dirs = Directory.EnumerateDirectories(stage, "*", SearchOption.AllDirectories).Select(d => Path.GetRelativePath(stage, d).Replace('\\', '/')).Order(StringComparer.Ordinal).ToList();
        var split = Tools.Split(files, t.Parts);
        for (int k = 0; k < split.Count; k++)
        {
            var part = Path.Combine(outDir, Tools.PartName(t.Name, k + 1));
            await Pack(stage, k == 0 ? dirs : [], split[k], part, ct);
            await using var pf = File.OpenRead(part);
            parts.Add((Path.GetFileName(part), Convert.ToHexStringLower(await SHA256.HashDataAsync(pf, ct))));
        }
    }
    await using var z = File.OpenRead(zip);
    var sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(z, ct));
    built[i] = new Built(t.Name, version, Path.GetFileName(zip), sha, t.Bin, t.Provides, t.Home, t.Later, t.Sfx, parts);
    var partsMb = parts.Sum(p => new FileInfo(Path.Combine(outDir, p.Item1)).Length) / 1048576.0;
    Console.WriteLine($"  {t.Name,-12} {version,-16} {new FileInfo(zip).Length / 1048576.0,7:0.0} MB  {name}" + (parts.Count > 0 ? $"  (+ {parts.Count} tar.zst parts, {partsMb:0.0} MB)" : ""));
}

// Where a tool's build is: (url, version, file name).
async Task<(string, string, string)> Resolve(Tool t, CancellationToken ct)
{
    if (t.Source.StartsWith("github:"))
    {
        var repo = t.Source["github:".Length..];
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases/latest");
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        if (!string.IsNullOrEmpty(token)) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var res = await http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var tagName = doc.RootElement.GetProperty("tag_name").GetString()!;
        foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
        {
            var n = a.GetProperty("name").GetString()!;
            if (t.Asset.IsMatch(n)) return (a.GetProperty("browser_download_url").GetString()!, tagName, n);
        }
        throw new InvalidOperationException($"{t.Name}: no asset matching {t.Asset} in {repo} {tagName}");
    }
    if (t.Source == "node-lts")
    {
        using var doc = JsonDocument.Parse(await http.GetStringAsync("https://nodejs.org/dist/index.json", ct));
        foreach (var r in doc.RootElement.EnumerateArray())
        {
            if (r.GetProperty("lts").ValueKind == JsonValueKind.False) continue;
            var v = r.GetProperty("version").GetString()!;
            var n = $"node-{v}-{t.Asset}.zip";
            return ($"https://nodejs.org/dist/{v}/{n}", v, n);
        }
        throw new InvalidOperationException("node: no LTS release");
    }
    throw new InvalidOperationException($"{t.Name}: unknown source {t.Source}");
}

// The download into stage/, in the tool's shape: a zip without its one top folder, a single file
// (.exe, or .gz holding one) under its new name.
static void Unpack(Tool t, string download, string stage)
{
    if (download.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
    {
        using var zip = ZipFile.OpenRead(download);
        var top = Tools.CommonTop(zip.Entries.Select(e => e.FullName));
        foreach (var e in zip.Entries)
        {
            var rel = e.FullName.Replace('\\', '/');
            if (top != null) rel = rel[(top.Length + 1)..];
            if (rel.Length == 0) continue;
            var dest = Path.GetFullPath(Path.Combine(stage, rel));
            if (!dest.StartsWith(stage + Path.DirectorySeparatorChar)) throw new InvalidOperationException($"{t.Name}: {e.FullName} leaves the folder");
            if (rel.EndsWith('/')) { Directory.CreateDirectory(dest); continue; }
            if (!Tools.Keeps(t, rel)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            e.ExtractToFile(dest, overwrite: true);
        }
        return;
    }
    var target = Path.Combine(stage, t.Rename ?? Path.GetFileName(download));
    if (download.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
    {
        using var input = new GZipStream(File.OpenRead(download), CompressionMode.Decompress);
        using var output = File.Create(target);
        input.CopyTo(output);
        return;
    }
    File.Copy(download, target, overwrite: true);
}

// One part: the folders (first part only), then its files, as a tar (PAX) through zstd. Level 19
// with a long window: built once in CI, unpacked on every PC, so smaller is worth the CPU here.
static async Task Pack(string stage, IReadOnlyList<string> dirs, IReadOnlyList<string> files, string output, CancellationToken ct)
{
    File.Delete(output);
    await using var outFile = File.Create(output);
    using var zstd = new ZstdSharp.CompressionStream(outFile, 19);
    zstd.SetParameter(ZstdSharp.Unsafe.ZSTD_cParameter.ZSTD_c_nbWorkers, Environment.ProcessorCount);
    zstd.SetParameter(ZstdSharp.Unsafe.ZSTD_cParameter.ZSTD_c_enableLongDistanceMatching, 1);
    zstd.SetParameter(ZstdSharp.Unsafe.ZSTD_cParameter.ZSTD_c_windowLog, 27);
    await using (var tar = new System.Formats.Tar.TarWriter(zstd, System.Formats.Tar.TarEntryFormat.Pax, leaveOpen: true))
    {
        foreach (var d in dirs) await tar.WriteEntryAsync(new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.Directory, d + "/"), ct);
        foreach (var f in files) await tar.WriteEntryAsync(Path.Combine(stage, f), f, ct);
    }
}

// A 7-Zip self-extractor's contents, with the runner's 7z (CI's Linux has it).
static void Extract7z(Tool t, string download, string stage)
{
    var psi = new System.Diagnostics.ProcessStartInfo("7z") { RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var a in new[] { "x", "-y", "-o" + stage, download }) psi.ArgumentList.Add(a);
    using var p = System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException($"{t.Name}: 7z didn't start");
    var err = p.StandardError.ReadToEndAsync();
    p.StandardOutput.ReadToEnd();
    p.WaitForExit();
    if (p.ExitCode != 0) throw new InvalidOperationException($"{t.Name}: 7z exited {p.ExitCode}: {err.Result}");
}

static string FindRepo()
{
    for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        if (File.Exists(Path.Combine(d.FullName, "install", "tools.txt"))) return d.FullName;
    return Directory.GetCurrentDirectory();
}
