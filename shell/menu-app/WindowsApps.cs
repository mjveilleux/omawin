// Windows' extra apps (roadmap 8.50): the Store apps Windows put in for you (Clipchamp, News,
// Solitaire, the Xbox app, To Do, …), listed with their size and removed for your account only,
// each reinstallable from the Store; never Windows' own parts. omawin's suggested removals are a
// list in the repo (default/launcher/windows-apps.txt, layered like the launcher's files), asked
// about, never removed silently.
//
// This file decides (what's removable, what the list and a typed name mean, the words); the
// Store calls are WindowsAppsStore.cs. Pure C#: tested in menu-app.Tests.

namespace SystemMenu;

// One package installed for this account, as the Store reports it.
public sealed record AppPackage(
    string Name,          // Microsoft.BingNews
    string Family,        // Microsoft.BingNews_8wekyb3d8bbwe (what the Store's page and reinstall use)
    string FullName,      // the exact version and architecture, what removal takes
    string DisplayName,   // Microsoft News
    bool Framework,       // a runtime other apps use (VCLibs, .NET Native, WinAppRuntime)
    bool Resource,        // a language or scale pack of another package
    bool System,          // signed as part of Windows (the shell, Settings, the lock screen)
    long Bytes);          // its folder's size, -1 when it couldn't be read

// A line of windows-apps.txt: a package name (or a prefix ending in *) and why it's suggested.
public sealed record SuggestedRemoval(string Pattern, string Why, string Origin);

public static class WindowsApps
{
    // Never offered, whatever a list says: the Store (and so every reinstall), winget, Windows
    // Security, the shell's own apps, and the sign-in pieces games and accounts need. Store-signed
    // packages Windows can't do without.
    static readonly string[] Protected =
    [
        "Microsoft.WindowsStore", "Microsoft.StorePurchaseApp", "Microsoft.DesktopAppInstaller", "Microsoft.SecHealthUI",
        "Microsoft.Windows.*", "Microsoft.WindowsAppRuntime*", "Microsoft.UI.Xaml*", "Microsoft.VCLibs*", "Microsoft.NET.*",
        "Microsoft.Services.Store.Engagement", "Microsoft.XboxIdentityProvider", "Microsoft.AAD.BrokerPlugin",
        "Microsoft.AccountsControl", "Microsoft.LockApp", "Microsoft.ApplicationCompatibilityEnhancements",
        "Microsoft.HEIFImageExtension", "Microsoft.HEVCVideoExtension", "Microsoft.VP9VideoExtensions",
        "Microsoft.WebMediaExtensions", "Microsoft.WebpImageExtension", "Microsoft.RawImageExtension", "Microsoft.AV1VideoExtension",
        "MicrosoftWindows.*", "Windows.*", "Microsoft.WindowsTerminal",
    ];

    static bool Like(string name, string pattern) => pattern.EndsWith('*')
        ? name.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase)
        : name.Equals(pattern, StringComparison.OrdinalIgnoreCase);

    // Something you could remove and get back from the Store: an app of its own, not part of
    // Windows, not a runtime or a language pack, not on the protected list.
    public static bool Removable(AppPackage p) =>
        !p.Framework && !p.Resource && !p.System && !Protected.Any(x => Like(p.Name, x));

    public static (List<SuggestedRemoval> List, List<Catalog.Problem> Problems) Parse(IEnumerable<(string File, string[] Lines)> layers)
    {
        var byPattern = new Dictionary<string, SuggestedRemoval>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        var problems = new List<Catalog.Problem>();
        foreach (var (file, lines) in layers)
            for (int i = 0; i < lines.Length; i++)
            {
                var origin = $"{Path.GetFileName(file)}:{i + 1}";
                var line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var f = line.Split('|', 2).Select(s => s.Trim()).ToArray();
                // "-Name": a later layer (yours) takes one off omawin's list.
                bool drop = f[0].StartsWith('-');
                var pattern = drop ? f[0][1..].Trim() : f[0];
                if (pattern.Length == 0 || pattern.Contains(' ') || pattern.IndexOf('*') is >= 0 and var star && star != pattern.Length - 1)
                { problems.Add(new(origin, $"'{f[0]}': a package name, or a prefix ending in *")); continue; }
                if (Protected.Any(x => Like(pattern.TrimEnd('*'), x)))
                { problems.Add(new(origin, $"{pattern} is part of Windows or the Store, never removed")); continue; }
                if (drop) { byPattern.Remove(pattern); continue; }
                if (f.Length < 2 || f[1].Length == 0) { problems.Add(new(origin, $"{pattern}: say why it's suggested")); continue; }
                if (!byPattern.ContainsKey(pattern)) order.Add(pattern);
                byPattern[pattern] = new SuggestedRemoval(pattern, f[1], origin);
            }
        return (order.Where(byPattern.ContainsKey).Select(p => byPattern[p]).ToList(), problems);
    }

    // The suggested removals installed here, each with the line that names it.
    public static List<(AppPackage App, SuggestedRemoval Why)> Suggested(IEnumerable<AppPackage> installed, IEnumerable<SuggestedRemoval> list)
    {
        var apps = installed.Where(Removable).ToList();
        var found = new List<(AppPackage, SuggestedRemoval)>();
        foreach (var s in list)
            foreach (var a in apps.Where(a => Like(a.Name, s.Pattern)))
                if (!found.Any(x => x.Item1.FullName == a.FullName)) found.Add((a, s));
        return found;
    }

    // What a typed word means: a package's name or display name, exactly, else the one that
    // contains it (more than one is ambiguous, and said).
    public static (AppPackage? App, List<AppPackage> Ambiguous) Match(IEnumerable<AppPackage> installed, string typed)
    {
        var apps = installed.Where(Removable).ToList();
        var exact = apps.Where(a => a.Name.Equals(typed, StringComparison.OrdinalIgnoreCase) || a.DisplayName.Equals(typed, StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count == 1) return (exact[0], []);
        var near = apps.Where(a => a.Name.Contains(typed, StringComparison.OrdinalIgnoreCase) || a.DisplayName.Contains(typed, StringComparison.OrdinalIgnoreCase)).ToList();
        return near.Count == 1 ? (near[0], []) : (null, near);
    }

    public static string Size(long bytes) => bytes switch
    {
        < 0 => "?",
        < 1024 * 1024 => "<1 MB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024 * 1024)} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.0} GB",
    };

    // `omawin windows-apps`: every removable app, biggest first, the suggested ones marked.
    public static string Format(IReadOnlyList<AppPackage> installed, IReadOnlyList<SuggestedRemoval> list)
    {
        var apps = installed.Where(Removable).OrderByDescending(a => a.Bytes).ThenBy(a => a.DisplayName).ToList();
        if (apps.Count == 0) return "No Store apps to remove for this account.";
        var suggested = Suggested(apps, list).Select(s => s.App.FullName).ToHashSet();
        int w = Math.Min(40, apps.Max(a => a.DisplayName.Length) + 2);
        var lines = apps.Select(a => $"{(suggested.Contains(a.FullName) ? "* " : "  ")}{a.DisplayName.PadRight(w)}{Size(a.Bytes),8}  {a.Name}").ToList();
        lines.Insert(0, $"Store apps installed for you ({apps.Count}); * omawin suggests removing:");
        lines.Add("");
        lines.Add(suggested.Count > 0
            ? $"omawin windows-apps remove: the {suggested.Count} marked *, asked first. Or name some: omawin windows-apps remove <name> …"
            : "omawin windows-apps remove <name> …: removes those for you, asked first.");
        lines.Add("Removed only for your account; each comes back from the Store.");
        return string.Join('\n', lines);
    }

    // The list shown before removing, with why each is suggested.
    public static string Confirm(IReadOnlyList<(AppPackage App, string? Why)> todo) =>
        $"Remove {todo.Count} app{(todo.Count == 1 ? "" : "s")} for your account (each comes back from the Store):\n  "
        + string.Join("\n  ", todo.Select(t => $"{t.App.DisplayName} ({Size(t.App.Bytes)})" + (t.Why is { } why ? $": {why}" : "")));

    // The Store's page for a package family: how to get one back.
    public static string StoreLink(AppPackage p) => $"ms-windows-store://pdp/?PFN={p.Family}";
}
