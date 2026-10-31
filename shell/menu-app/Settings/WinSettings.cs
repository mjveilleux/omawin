// The settings service as the menu app uses it (roadmap 1B): the catalog from its three layers
// (omawin's default/launcher/settings.txt, the PC's shared folder's, this account's), filtered to
// this Windows build, wired to the real handlers, the undo history and the admin queue in
// %LOCALAPPDATA%\launcher, and Windows' own settings index for search.
//
// The API other parts use (the system menu today, One Search's Settings filter next):
//   WinSettings.Store                 entries, Peek / Set / Toggle / Step / UndoLast, Changed
//   WinSettings.Search(query, limit)  ranked switches and pages for what was typed
//   WinSettings.Open(page)            open a page from the index
//   WinSettings.Remember(hit, query)  you picked it after typing this: it ranks first next time
//   WinSettings.Answer(request)       `omawin setting …` / `omawin settings …` / `omawin tune-up` (Reply.cs)

using System.Diagnostics;

namespace SystemMenu;

public static class WinSettings
{
    static readonly ISettingHandler Reg = new RegHandler(), Spi = new SpiHandler(), Power = new PowerHandler(),
        Builtin = new BuiltinHandler(), Link = new LinkHandler(), Omawin = new OmawinHandler();

    static ISettingHandler HandlerFor(SettingEntry e) => e.How.Verb switch
    {
        "reg" => Reg, "spi" => Spi, "powercfg" => Power, "builtin" => Builtin, "link" => Link, "omawin" => Omawin,
        var v => throw new InvalidOperationException($"no handler for '{v}'"),
    };

    // Windows' real build (.NET reads it from the kernel, not from a compatibility manifest).
    public static readonly int Build = Environment.OSVersion.Version.Build;

    // --- the catalog, read again when one of its files changes ---------------------------------
    static readonly object gate = new();
    static List<SettingEntry> entries = [], all = [];
    static string stamps = "";
    static readonly bool Unverified = Environment.GetEnvironmentVariable("OMAWIN_SETTINGS_UNVERIFIED") == "1";

    // Every entry of the catalog, those hidden on this PC too ("check", another Windows build):
    // for `omawin tune-up`, which says how many wait to be verified.
    public static IReadOnlyList<SettingEntry> AllEntries()
    {
        Entries();
        lock (gate) return all;
    }

    public static IReadOnlyList<SettingEntry> Entries()
    {
        var files = MenuModel.Layered("settings.txt");
        var now = string.Join('|', files.Select(f => File.GetLastWriteTimeUtc(f).Ticks));
        lock (gate)
        {
            if (now == stamps) return entries;
            var (parsed, problems) = Catalog.Parse(files.Where(File.Exists).Select(f => (f, File.ReadAllLines(f))));
            foreach (var p in problems) App.Log(new FormatException($"{p.Origin}: {p.Message}"));
            all = parsed;
            entries = Catalog.Visible(all, Build, Unverified);
            stamps = now;
            return entries;
        }
    }

    static SettingStore? store;

    public static SettingStore Store
    {
        get
        {
            if (store != null) return store;
            lock (gate)
            {
                if (store != null) return store;
                var s = new SettingStore(Entries, HandlerFor,
                    new SettingHistory(Path.Combine(MenuModel.StateDir, "settings-history.tsv")),
                    new SettingAdminQueue(Path.Combine(MenuModel.StateDir, "settings-admin-queue.tsv")),
                    App.Log);
                s.Watch = e => SettingWatch.Ensure(s, e);
                s.RunAdmin = RunElevated;
                return store = s;
            }
        }
    }

    // App start, in the background: keep the history short, and read Windows' settings index if
    // Windows changed it since (an update) or the display language changed.
    public static void Start() => Task.Run(() =>
    {
        try { Store.History.Trim(); } catch (Exception ex) { App.Log(ex); }
        try { RefreshIndex(); } catch (Exception ex) { App.Log(ex); }
    });

    // --- search ----------------------------------------------------------------------------------
    public static List<SettingHit> Search(string query, int limit = 40) =>
        SettingSearch.Rank(Entries(), Pages(), query, (key, q) => History.Load().Boost(key, q), limit);

    public static void Remember(SettingHit hit, string query)
    {
        if (query.Trim().Length > 0) History.Load().Add(hit.Key, query.Trim());
    }

    // Open a page: an ms-settings: link or a console / program and its arguments.
    public static void Open(IndexedSetting page)
    {
        try
        {
            if (Launcher.Link.IsMatch(page.Uri)) QuickSettings.Open(page.Uri);
            else
            {
                var (exe, args) = page.Uri.Split(' ', 2) is [var x, var a] ? (x, a) : (page.Uri, "");
                Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true })?.Dispose();
            }
        }
        catch (Exception ex) { App.Log(ex); }
    }

    // --- Windows' settings index and the curated pages ---------------------------------------
    static string IndexCache => Path.Combine(MenuModel.StateDir, "settings-index.tsv");
    static List<IndexedSetting>? curated, indexed, pages;
    static string curatedStamp = "";

    // The curated pages first (their names and page labels are omawin's own), then every
    // setting Windows' index has.
    public static List<IndexedSetting> Pages()
    {
        var files = MenuModel.Layered("settings-pages.txt");
        var now = string.Join('|', files.Select(f => File.GetLastWriteTimeUtc(f).Ticks));
        lock (gate)
        {
            if (curated == null || now != curatedStamp)
            {
                curated = SettingsIndex.ParsePages(MenuModel.LayeredLines("settings-pages.txt"));
                curatedStamp = now;
                pages = null;
            }
            if (indexed == null)
            {
                try { indexed = File.Exists(IndexCache) ? SettingsIndex.ParseCache(File.ReadLines(IndexCache)) : []; }
                catch (IOException) { indexed = []; }
                pages = null;
            }
            return pages ??= [.. curated, .. indexed];
        }
    }

    static void RefreshIndex()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "ImmersiveControlPanel", "Settings");
        var xml = Directory.Exists(dir) ? Directory.GetFiles(dir, "AllSystemSettings_*.xml").FirstOrDefault() : null;
        if (xml == null) return; // the curated pages are all there is
        var stamp = SettingsIndex.Stamp(xml, File.GetLastWriteTimeUtc(xml).Ticks, System.Globalization.CultureInfo.CurrentUICulture.Name);
        try { if (File.Exists(IndexCache) && File.ReadLines(IndexCache).FirstOrDefault() == stamp) return; } catch (IOException) { }
        var known = Pages();
        List<IndexedSetting> rows;
        using (var reader = new StreamReader(xml))
            rows = SettingsIndex.ReadXml(reader, Resolve, id => SettingsIndex.PageOf(id, known));
        if (rows.Count < SettingsIndex.TooFew)
        {
            // Not the format this reader expects (a new Windows build): the curated pages stand
            // in, and the log says so, for whoever looks at it next.
            App.Log(new FormatException($"settings index: {rows.Count} rows from {xml}; has its format changed?"));
            return;
        }
        Directory.CreateDirectory(MenuModel.StateDir);
        File.WriteAllLines(IndexCache, SettingsIndex.CacheLines(stamp, rows));
        lock (gate) { indexed = null; pages = null; }
    }

    // "@%SystemRoot%\system32\shell32.dll,-21817" or "@{Package?ms-resource://…}" in this PC's
    // language; plain text as it is; null when Windows can't resolve it.
    static unsafe string? Resolve(string? text)
    {
        if (string.IsNullOrEmpty(text) || text[0] != '@') return text;
        char* buffer = stackalloc char[1024];
        return SettingsNative.SHLoadIndirectString(text, buffer, 1024, 0) == 0 ? new string(buffer) : null;
    }

    // --- the one elevated run (roadmap 2.9) ------------------------------------------------------
    // Windows PowerShell 5.1 (always there, and an elevated per-user pwsh.exe could be swapped by
    // anything that can write to the user's folder, AGENTS.md), hidden, with fixed verbs: it reads
    // the plan and checks every line against its own allow-list. Declined: nothing ran.
    static async Task<AdminRun> RunElevated(IReadOnlyList<string> plan)
    {
        var dir = MenuModel.StateDir;
        Directory.CreateDirectory(dir);
        var planFile = Path.Combine(dir, "settings-admin-plan.tsv");
        var resultFile = Path.Combine(dir, "settings-admin-result.tsv");
        File.WriteAllLines(planFile, plan);
        File.Delete(resultFile);
        var script = Path.Combine(MenuModel.Repo, "bin", "settings-admin.ps1");
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var info = new ProcessStartInfo(powershell,
            $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\" -Plan \"{planFile}\" -Result \"{resultFile}\"")
        { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
        try
        {
            using var p = Process.Start(info);
            if (p != null) await p.WaitForExitAsync();
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { return new(false, []); } // ERROR_CANCELLED: UAC declined
        var failures = File.Exists(resultFile)
            ? File.ReadAllLines(resultFile).Select(l => l.Split('\t')).Where(f => f.Length >= 2 && f[1] == "fail").Select(f => $"{f[0]}: {(f.Length > 2 ? f[2] : "failed")}").ToList()
            : ["the admin script left no result"];
        try { File.Delete(planFile); } catch (IOException) { }
        return new(true, failures);
    }

    // --- omawin's recommended settings (default/launcher/tune-up.txt, roadmap 8G) ----------------
    // Read on each `omawin tune-up`: a terminal command, rare, and the file is small. A line that
    // names no setting, or a value it doesn't take, goes to the log and is left out.
    static List<Recommendation> Recommendations()
    {
        var (recs, problems) = TuneUp.Parse(MenuModel.Layered("tune-up.txt").Where(File.Exists).Select(f => (f, File.ReadAllLines(f))));
        foreach (var p in problems) App.Log(new FormatException($"{p.Origin}: {p.Message}"));
        var catalog = AllEntries();
        return recs.Where(r =>
        {
            var bad = TuneUp.Check([r], catalog);
            foreach (var p in bad) App.Log(new FormatException($"{p.Origin}: {p.Message}"));
            return bad.Count == 0;
        }).ToList();
    }

    // --- `omawin setting …` / `omawin settings …` / `omawin tune-up` -----------------------------
    public static async Task<SettingCommands.Answer> Answer(string[] request) => request switch
    {
        ["--setting", .. var rest] => SettingCommands.Setting(Store, rest, ExplorerRestart.Now),
        ["--settings", .. var rest] => await SettingCommands.Settings(Store, rest,
            Path.Combine(MenuModel.Work, "settings.txt"), Path.Combine(MenuModel.Shared, "settings.txt")),
        ["--tune-up", .. var rest] => await SettingCommands.TuneUp(Store, rest, Recommendations(), AllEntries(), Build, Unverified),
        _ => new(2, "unknown request: " + string.Join(' ', request)),
    };
}
