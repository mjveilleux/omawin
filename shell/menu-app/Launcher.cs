namespace SystemMenu;

// The launcher (Win+Space): default/launcher/items.txt's items, then every installed app, like
// Omarchy's Super+Space. The apps list is %LOCALAPPDATA%\launcher\apps.tsv ("name<TAB>app id"),
// kept fresh in the background (Background.cs).
public sealed record LaunchItem(string Icon, string Name, string Command, string? AppId)
{
    public bool IsApp => AppId != null;

    // Other names to find it by: the program ("pwsh" for PowerShell 7, "taskmgr"), or the
    // Store package's name with its words split ("Windows Calculator"). Matched a little
    // weaker than the name itself.
    public string[] Keywords { get; init; } = KeywordsOf(Command, AppId);

    // A Linux GUI app's distro as its label says it ("Arch"), null for a Windows app (roadmap 3.7).
    public string? Linux { get; init; }

    // How it's remembered (launch-history.json).
    public string Key => AppId ?? Name;

    static string[] KeywordsOf(string command, string? appId)
    {
        var words = new List<string>();
        void Add(string? w)
        {
            if (string.IsNullOrWhiteSpace(w)) return;
            w = System.Text.RegularExpressions.Regex.Replace(w, "(?<=[a-z])(?=[A-Z])", " "); // VisualStudioCode -> Visual Studio Code
            if (!words.Contains(w, StringComparer.OrdinalIgnoreCase)) words.Add(w);
        }
        if (appId != null)
        {
            var exe = System.Text.RegularExpressions.Regex.Match(appId, @"([\w.-]+?)\.exe", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (exe.Success) Add(Path.GetFileName(exe.Groups[1].Value).Split('.')[^1]); // ...\pwsh.exe, Microsoft.Office.EXCEL.EXE.15
            else
            {
                // Store app: Publisher.PackageName_hash!App
                var package = appId.Split('!')[0].Split('_')[0];
                Add(package.Contains('.') ? package[(package.IndexOf('.') + 1)..] : package);
                var entry = appId.Contains('!') ? appId[(appId.IndexOf('!') + 1)..] : null;
                if (entry is not null and not "App") Add(entry);
            }
        }
        else if (command.StartsWith('@')) Add(command[1..].Split(' ')[0]); // @keys: "keys" finds Shortcuts
        // A program, but not the browser a link opens in (Gmail is "chrome ... https://mail...").
        else if (command.Length > 0 && !command.Contains("://"))
        {
            var first = command.StartsWith('"') ? command.Split('"')[1] : command.Split(' ')[0];
            if (!first.Contains(':') || first.Contains('\\')) Add(Path.GetFileNameWithoutExtension(first)); // wt, chrome, taskmgr
            else Add(first.Split(':')[0]); // ms-teams:
        }
        return [.. words];
    }

    // A built-in item (@projects, @clipboard, ..., @files, @menu): a page of this window, the
    // file finder or the system menu.
    public string? BuiltIn => Command.StartsWith('@') ? Command[1..].Split(' ', 2)[0] : null;

    // Its real icon (AppIcons): an app's, or a VPN's own from its Connection Manager profile.
    public string? IconKey => AppId ?? (BuiltIn == "vpn" && Vpn.IconFile(Command.Split(' ', 2) is [_, var name] ? name.Trim() : "") is { } ico ? "path:" + ico : null);

    public string Hint => BuiltIn switch
    {
        "projects" => "projects ›",
        "localhost" => "servers + sites ›",
        "clipboard" => "history ›",
        "reports" => "power bi ›",
        "keys" => "cheat sheet ›",
        "audio" => "volume, mute ›",
        "quick" => "wi-fi, bluetooth, sound ›",
        "menu" => "install, setup, power ›",
        "files" => "files + folders ›",
        "packages" => "winget, choco, scoop ›",
        "vpn" => "vpn",
        _ when Linux != null => Linux + " app",
        _ when IsApp => "app",
        _ when Launcher.Link.IsMatch(Command) => "link",
        _ => "app",
    };
}

public static class Launcher
{
    // A URL or protocol link (https://..., ms-settings:...), not a program: a scheme, then // or
    // anything but the \ of a drive path (C:\...). The window starts commands by the same test.
    public static readonly System.Text.RegularExpressions.Regex Link =
        new(@"^[a-z][\w+.-]*:(//|[^\\])", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    // omawin's items, then the shared folder's and this account's (MenuModel.Layered), where they're there.
    static readonly string[] ItemsFiles = MenuModel.Layered("items.txt");
    static string AppsFile => Path.Combine(MenuModel.StateDir, "apps.tsv");

    // Read and parsed again only when one of the files has changed (each opening asks), like
    // History.Load. The list is shared: read it, don't change it.
    static List<LaunchItem>? cached;
    static string cachedStamp = "";

    public static List<LaunchItem> Load()
    {
        var stamp = string.Join("|", ItemsFiles.Append(AppsFile).Select(f => File.GetLastWriteTimeUtc(f).Ticks)) + "|" + Reports.Available;
        if (cached != null && stamp == cachedStamp) return cached;
        cachedStamp = stamp;
        return cached = Read();
    }

    static List<LaunchItem> Read()
    {
        var items = new List<LaunchItem>();
        foreach (var file in ItemsFiles.Where(File.Exists))
        {
            try
            {
                foreach (var raw in File.ReadAllLines(file))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith('#')) continue;
                    var parts = line.Split('|', 3).Select(p => p.Trim()).ToArray();
                    if (parts.Length < 3) continue;
                    // Reports needs a reports script (Reports.Available).
                    if (parts[2] == "@reports" && !Reports.Available) continue;
                    items.Add(new LaunchItem(parts[0], parts[1], parts[2], null));
                }
            }
            catch { }
        }
        var names = new HashSet<string>(items.Select(i => i.Name), StringComparer.OrdinalIgnoreCase);
        var distros = Projects.Distros();
        try
        {
            foreach (var line in File.ReadAllLines(AppsFile))
            {
                var parts = line.Split('\t', 2);
                if (parts.Length != 2 || !names.Add(parts[0])) continue;
                var item = new LaunchItem(char.ConvertFromUtf32(0xF003B), parts[0], "", parts[1]);
                // A Linux GUI app (WSLg's shortcut): its distro's logo until its own icon loads,
                // found by "linux" and the distro's name too, and its hint says which Linux.
                if (LinuxState.WslgDistro(parts[0], distros) is { } distro)
                {
                    var label = LinuxFiles.Label(distro);
                    item = item with { Icon = char.ConvertFromUtf32(LinuxFiles.Logo(distro)), Linux = label, Keywords = [.. item.Keywords, "linux", label] };
                }
                items.Add(item);
            }
        }
        catch { }
        return items;
    }

    // Typing: fzy-style fuzzy scoring (Fuzzy.cs) of the name, and a little weaker of its
    // keywords; a typo per word is tolerated. On top: an exact name or name start counts
    // extra, your own items (items.txt) a little, and what you've opened before, most of all
    // what you opened after typing the same thing (launch-history.json).
    public static List<LaunchMatch> Search(List<LaunchItem> items, string query)
    {
        query = query.Trim();
        if (query.Length == 0) return items.Select(i => new LaunchMatch(i, [], 0)).ToList();
        var history = History.Load();
        var matches = new List<LaunchMatch>();
        foreach (var item in items)
        {
            var positions = new List<int>();
            double score = Fuzzy.Match(item.Name, query, positions);
            foreach (var keyword in item.Keywords)
            {
                double k = Fuzzy.Match(keyword, query) - 0.5;
                if (k > score) { score = k; positions.Clear(); }
            }
            if (score == Fuzzy.None) continue;
            if (item.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) score += 2;
            score -= 0.02 * Math.Max(0, item.Name.Length - query.Length); // shorter names first
            if (!item.IsApp) score += 0.3;
            score += history.Boost(item.Key, query);
            matches.Add(new LaunchMatch(item, positions, score));
        }
        return matches.OrderByDescending(m => m.Score).ThenBy(m => m.Item.IsApp ? 1 : 0)
            .ThenBy(m => m.Item.Name.Length).ThenBy(m => m.Item.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static void Remember(LaunchItem item, string query) => History.Load().Add(item.Key, query.Trim());
}

public sealed record LaunchMatch(LaunchItem Item, List<int> Positions, double Score);

// What you've opened from the launcher, and after typing what: frecency, so the things you
// use come first, and "c" learns it means Chrome after you've picked Chrome for it.
// %LOCALAPPDATA%\launcher\launch-history.json.
public sealed class History
{
    public sealed class Entry
    {
        public int Count { get; set; }
        public long Last { get; set; }
        public Dictionary<string, int> Queries { get; set; } = [];
    }

    public Dictionary<string, Entry> Items { get; set; } = [];
    static string FilePath => Path.Combine(MenuModel.StateDir, "launch-history.json");
    static History? cached;
    static DateTime cachedAt;

    public static History Load()
    {
        var stamp = File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : DateTime.MinValue;
        if (cached != null && stamp == cachedAt) return cached;
        try { cached = System.Text.Json.JsonSerializer.Deserialize(File.ReadAllText(FilePath), Json.Default.History) ?? new(); }
        catch { cached = new(); }
        cachedAt = stamp;
        return cached;
    }

    public void Add(string key, string query)
    {
        if (!Items.TryGetValue(key, out var e)) Items[key] = e = new Entry();
        e.Count++;
        e.Last = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (query.Length > 0) e.Queries[query.ToLowerInvariant()] = e.Queries.GetValueOrDefault(query.ToLowerInvariant()) + 1;
        try
        {
            Directory.CreateDirectory(MenuModel.StateDir);
            File.WriteAllText(FilePath, System.Text.Json.JsonSerializer.Serialize(this, Json.Default.History));
        }
        catch { }
    }

    // Up to ~1 for use (more opens, more recently), plus up to 3 when you've opened it after
    // typing this (or something this is the start of, or that starts with this).
    public double Boost(string key, string query)
    {
        if (!Items.TryGetValue(key, out var e)) return 0;
        double age = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - e.Last) / 86400.0;
        double recency = age < 3 ? 1 : age < 14 ? 0.7 : age < 60 ? 0.4 : 0.2;
        double use = Math.Min(1, Math.Log2(1 + e.Count) / 4) * recency;
        query = query.ToLowerInvariant();
        int hits = e.Queries.Where(q => q.Key.StartsWith(query) || query.StartsWith(q.Key)).Sum(q => q.Value);
        return use + Math.Min(3, hits * 1.5);
    }
}
