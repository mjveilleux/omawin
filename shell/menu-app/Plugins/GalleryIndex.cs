using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SystemMenu.Plugins;

// The plugin gallery's index (roadmap 4.10): the bar widgets of Omarchy's marketplace
// (plugins.omarchy.org/catalog.json, ~6,000 plugins, 15 MB) that its maintainers verified, cut
// down to what the gallery shows, each with a verdict on whether it runs in omawin's QML host
// (src/qs-host). Made in CI by src/plugin-gallery and shipped as a tools bundle entry, unpacked to
// %LOCALAPPDATA%\launcher\plugin-gallery\plugin-gallery.json; the gallery page reads it, and
// `omawin plugin add <id>` downloads a plugin from it at the commit that was reviewed.
//
// Pure: no WinUI, no Windows calls. The builder links this file, so the file it writes is the one
// the menu app reads.
public sealed class GalleryIndex
{
    public string GeneratedAt { get; set; } = "";
    public string Source { get; set; } = "";
    public string CatalogGeneratedAt { get; set; } = "";
    public List<GalleryEntry> Plugins { get; set; } = [];

    public const string FileName = "plugin-gallery.json";

    // Reads an index; null with the reason when it isn't one. Entries whose id or source can't be
    // trusted as a folder name or a download are dropped, so nothing downstream has to check again.
    public static GalleryIndex? Parse(string json, out string? error)
    {
        try
        {
            var index = JsonSerializer.Deserialize(json, GalleryJson.Default.GalleryIndex);
            if (index is null) { error = "empty index"; return null; }
            index.Plugins = [.. index.Plugins.Where(p => p.IsSound)];
            error = null;
            return index;
        }
        catch (JsonException ex)
        {
            error = $"not valid JSON: {ex.Message}";
            return null;
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, GalleryJson.Default.GalleryIndex);

    // A plugin id as a folder name: Omarchy's ids are dotted lowercase words (omawin.music,
    // b.omadoro); nothing that could climb out of the plugins folder or name a device.
    static readonly Regex SafeId = new(@"^[a-z0-9][a-z0-9_-]*(\.[a-z0-9][a-z0-9_-]*)*$", RegexOptions.CultureInvariant);
    public static bool IsSafeId(string? id) => id is { Length: > 0 and <= 80 } && SafeId.IsMatch(id);

    // Where a plugin comes from: a GitHub repository (the whole marketplace is on GitHub) at a
    // full commit, so what's downloaded is what was reviewed.
    static readonly Regex GitHubRepo = new(@"^https://github\.com/[A-Za-z0-9][A-Za-z0-9-]*/[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant);
    static readonly Regex FullCommit = new("^[0-9a-f]{40}$", RegexOptions.CultureInvariant);
    public static bool IsGitHubRepo(string? url) => url != null && GitHubRepo.IsMatch(url) && !url.EndsWith(".git") && !url.Contains("..");
    public static bool IsCommit(string? sha) => sha != null && FullCommit.IsMatch(sha);
}

public sealed class GalleryEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public int Stars { get; set; }
    public string License { get; set; } = "";
    public string Version { get; set; } = "";
    public string Repo { get; set; } = "";
    public string Commit { get; set; } = "";
    public string Verdict { get; set; } = Verdicts.NotYet;
    public List<string> Needs { get; set; } = [];     // what the host lacks: modules and types
    public List<string> Commands { get; set; } = [];  // Linux programs it runs

    // The zip of the plugin at its reviewed commit.
    [JsonIgnore] public string Download => $"{Repo.Replace("https://github.com/", "https://codeload.github.com/")}/zip/{Commit}";

    [JsonIgnore]
    public bool IsSound => GalleryIndex.IsSafeId(Id) && GalleryIndex.IsGitHubRepo(Repo) && GalleryIndex.IsCommit(Commit) && Verdicts.All.Contains(Verdict);

    // Whether `omawin plugin add` takes it: it loads in the host (all of it works, or all but its
    // Linux commands).
    [JsonIgnore] public bool CanTurnOn => Verdict is Verdicts.Runs or Verdicts.Partly;

    // What the gallery says about it, one line.
    [JsonIgnore]
    public string Why => Verdict switch
    {
        Verdicts.Runs => "runs in omawin",
        Verdicts.Partly => Commands.Count > 0 ? $"runs, but calls Linux programs: {string.Join(", ", Commands.Take(4))}" : "runs, but calls Linux programs",
        _ => Needs.Count > 0 ? $"not yet: needs {string.Join(", ", Needs.Take(4))}" : "not yet",
    };
}

// Whether a plugin runs in omawin's QML host.
public static class Verdicts
{
    public const string Runs = "runs";        // only what the host provides
    public const string Partly = "partly";    // it loads, but runs programs Windows doesn't have
    public const string NotYet = "not-yet";   // it imports a module or uses a type the host lacks
    public static readonly string[] All = [Runs, Partly, NotYet];
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    WriteIndented = true)]
[JsonSerializable(typeof(GalleryIndex))]
internal partial class GalleryJson : JsonSerializerContext { }
