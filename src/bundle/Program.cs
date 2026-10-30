using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Omawin.Bundle;

// dotnet run --project build/bundle -- <out dir> [tools.txt]
// Downloads each tool's latest upstream build, repacks it (src/bundle/Tools.cs says how) and
// writes omawin-tools.txt, the manifest the install reads. GITHUB_TOKEN, when set, lifts
// GitHub's rate limit for anonymous API calls.
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
        var stage = Path.Combine(dir, "stage");
        Directory.CreateDirectory(stage);
        Unpack(t, download, stage);
        var zip = Path.Combine(outDir, Tools.FileName(t.Name));
        File.Delete(zip);
        ZipFile.CreateFromDirectory(stage, zip, CompressionLevel.Optimal, includeBaseDirectory: false);
        await using var z = File.OpenRead(zip);
        var sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(z, ct));
        built[i] = new Built(t.Name, version, Path.GetFileName(zip), sha, t.Bin, t.Provides, t.Home, t.Later);
        Console.WriteLine($"  {t.Name,-12} {version,-16} {new FileInfo(zip).Length / 1048576.0,7:0.0} MB  {name}");
    });
}
finally { Directory.Delete(work, recursive: true); }

var manifest = Path.Combine(outDir, Tools.Manifest);
File.WriteAllLines(manifest, built.Select(b => b!.Line));
Console.WriteLine($"  {Path.GetFileName(manifest)}: {tools.Count} tools, {built.Sum(b => new FileInfo(Path.Combine(outDir, b!.File)).Length) / 1048576.0:0} MB");
return 0;

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

// The download into stage/, in the tool's shape: a zip without its one top folder, a 7-Zip
// self-extractor's contents, a single file (.exe, or .gz holding one) under its new name.
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
    if (download.EndsWith(".7z.exe", StringComparison.OrdinalIgnoreCase))
    {
        // A 7-Zip self-extractor (Git for Windows' PortableGit): 7-Zip opens it like an archive.
        // The CI runner has 7z; the PC gets a plain zip like every other tool.
        var psi = new System.Diagnostics.ProcessStartInfo("7z") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "x", "-y", "-o" + stage, download }) psi.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException($"{t.Name}: 7z didn't start");
        var err = p.StandardError.ReadToEndAsync();
        p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"{t.Name}: 7z exited {p.ExitCode}: {err.Result}");
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

static string FindRepo()
{
    for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        if (File.Exists(Path.Combine(d.FullName, "install", "tools.txt"))) return d.FullName;
    return Directory.GetCurrentDirectory();
}
