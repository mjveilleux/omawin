using System.Text.Json;
using SystemMenu.Plugins;

namespace Omawin.PluginGallery;

// One plugin of Omarchy's marketplace worth scanning: a bar widget its maintainers verified, at
// the commit they checked.
public sealed record CatalogPlugin(string CatalogId, string Name, string Author, string Description, string Category,
    int Stars, string License, string Version, string Repo, string Commit);

public static class Catalog
{
    public const string Url = "https://plugins.omarchy.org/catalog.json";

    // The marketplace's plugins the gallery can offer, most starred first, at most `limit`: bar
    // widgets (the host runs a plugin from its bar widget), available and installable, verified,
    // a plugin at the top of its repository (not a suite), on GitHub at a full commit. Omarchy
    // lists a repository once per plugin; one entry each here.
    public static List<CatalogPlugin> Pick(JsonElement catalog, int limit)
    {
        var picked = new List<CatalogPlugin>();
        if (!catalog.TryGetProperty("plugins", out var plugins) || plugins.ValueKind != JsonValueKind.Array) return picked;
        var repos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in plugins.EnumerateArray())
        {
            if (p.ValueKind != JsonValueKind.Object) continue;
            if (!S(p, "kind").Contains("Bar widget", StringComparison.Ordinal)) continue;
            if (S(p, "status") != "Available" || S(p, "verificationStatus") != "verified" || S(p, "repositoryLayout") != "root-plugin") continue;
            if (!p.TryGetProperty("installAvailable", out var install) || install.ValueKind != JsonValueKind.True) continue;
            var repo = S(p, "repo").TrimEnd('/');
            if (repo.EndsWith(".git", StringComparison.Ordinal)) repo = repo[..^4];
            if (!GalleryIndex.IsGitHubRepo(repo) || !repos.Add(repo)) continue;
            // The newest commit Omarchy validated: upstream's (re-checked as it moves), else the
            // one it was listed at, else the one it was verified at.
            var commit = new[] { S(p, "upstreamValidatedCommit"), S(p, "listingValidatedCommit"), S(p, "verificationCommit") }.FirstOrDefault(GalleryIndex.IsCommit);
            if (commit == null) { repos.Remove(repo); continue; }
            int stars = p.TryGetProperty("stars", out var st) && st.TryGetInt32(out var n) ? n : 0;
            picked.Add(new CatalogPlugin(S(p, "id"), S(p, "name"), S(p, "author"), S(p, "description"), S(p, "category"),
                stars, S(p, "license"), S(p, "version"), repo, commit));
        }
        return [.. picked.OrderByDescending(p => p.Stars).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Take(limit)];
    }

    static string S(JsonElement o, string name) => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    // The index entry for a scanned plugin: its manifest's id (what Omarchy and omawin install it
    // as) and the catalog's description of it. Null when its manifest has no bar widget or no
    // usable id.
    public static GalleryEntry? Entry(CatalogPlugin plugin, string manifestJson, ScanResult scan)
    {
        var manifest = Manifest.Parse(manifestJson, out _);
        if (manifest == null || !GalleryIndex.IsSafeId(manifest.Id)) return null;
        if (manifest.EntryPoints == null || !manifest.EntryPoints.ContainsKey("barWidget")) return null;
        return new GalleryEntry
        {
            Id = manifest.Id,
            Name = plugin.Name.Length > 0 ? plugin.Name : manifest.Name,
            Author = plugin.Author.Length > 0 ? plugin.Author : manifest.Author ?? "",
            Description = plugin.Description.Length > 0 ? plugin.Description : manifest.Description ?? "",
            Category = plugin.Category,
            Stars = plugin.Stars,
            License = plugin.License.Length > 0 ? plugin.License : manifest.License ?? "",
            Version = manifest.Version.Length > 0 ? manifest.Version : plugin.Version,
            Repo = plugin.Repo,
            Commit = plugin.Commit,
            Verdict = scan.Verdict,
            Needs = scan.Needs,
            Commands = scan.Commands,
        };
    }
}
