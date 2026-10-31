// `omawin setting …` and `omawin settings …` (roadmap 1B.4, 1B.5): answered by the menu app,
// which already has the catalog and its handlers loaded, so a command costs no PowerShell start.
// bin/omawin.cs's `ask` action sends the request and prints the answer (Reply.cs). Pure C#
// over a SettingStore: tested in menu-app.Tests.

namespace SystemMenu;

public static class SettingCommands
{
    // An answer: the exit code omawin returns and what it prints. Code 3 asks omawin to confirm
    // (it prints the text, asks y/N, and on yes sends the request again with --yes).
    public readonly record struct Answer(int Code, string Text);

    public const string SettingUsage = "omawin setting list [area] | get <id> | set <id> <value> [--now] | toggle <id> | undo";
    public const string SettingsUsage = "omawin settings save [-Shared] | apply [-NoAdmin] | admin";

    // omawin setting list [area] | get <id> | set <id> <value> [--now] | toggle <id> | undo
    // restartExplorer: done after a change that needs it, when --now is given (otherwise the row
    // says "Restart Explorer to apply": killing the taskbar is never a surprise).
    public static Answer Setting(SettingStore store, string[] a, Action? restartExplorer = null)
    {
        string verb = a.Length > 0 ? a[0] : "list";
        SettingEntry? Entry(int i) => a.Length > i ? store.Find(a[i]) : null;
        switch (verb)
        {
            case "list":
            {
                var area = a.Length > 1 ? a[1] : null;
                if (area == "--ids") return new(0, string.Join('\n', store.Entries().Select(e => e.Id))); // tab completion
                if (area != null && !Catalog.Areas.Contains(area)) return new(1, $"no area '{area}': {string.Join(", ", Catalog.Areas)}");
                var rows = store.Entries().Where(e => area == null || e.Area == area).ToList();
                // Builtins can take a moment (radios, a monitor's DDC/CI): read them side by side.
                var shown = new string[rows.Count];
                Parallel.For(0, rows.Count, i => shown[i] = rows[i].Kind is SettingKind.Link or SettingKind.Action
                    ? (rows[i].Kind == SettingKind.Link ? "opens a page" : "action")
                    : store.Label(rows[i], store.ReadNow(rows[i])));
                if (rows.Count == 0) return new(0, "no settings");
                int w = rows.Max(e => e.Id.Length) + 2, v = Math.Min(24, shown.Max(s => s.Length) + 2);
                return new(0, string.Join('\n', rows.Select((e, i) =>
                    e.Id.PadRight(w) + shown[i].PadRight(v) + e.Name + (e.Admin ? "  [admin]" : ""))));
            }
            case "get":
            {
                if (Entry(1) is not { } e) return new(1, Unknown(a));
                if (e.Kind is SettingKind.Link or SettingKind.Action) return new(1, $"{e.Id}: has no value ({e.Kind.ToString().ToLowerInvariant()})");
                var value = store.ReadNow(e);
                // The value as `set` takes it; for a choice, its label beside it.
                return new(value == null ? 1 : 0, value == null ? "?" : e.Kind == SettingKind.Choice && store.Label(e, value) is var l && l != value ? $"{value}\t{l}" : value);
            }
            case "set":
            {
                if (a.Length < 3) return new(1, "omawin setting set <id> <value>");
                if (Entry(1) is not { } e) return new(1, Unknown(a));
                var r = store.Set(e, string.Join(' ', a[2..].Where(x => x != "--now")), "command");
                if (r.Ok && r.Changed && a.Contains("--now") && e.How.Flags.Contains("restart-explorer")) restartExplorer?.Invoke();
                return Result(r);
            }
            case "toggle":
                return Entry(1) is { } t ? Result(store.Toggle(t, "command")) : new(1, Unknown(a));
            case "undo":
                return Result(store.UndoLast());
            default:
                return new(2, SettingUsage);
        }
    }

    // omawin settings save [-Shared] | apply [-NoAdmin] | admin [--yes]
    // account / shared: the two settings.txt files (the account folder's, the PC's shared folder's).
    public static async Task<Answer> Settings(SettingStore store, string[] a, string account, string shared)
    {
        bool toShared = a.Any(x => x.Equals("-Shared", StringComparison.OrdinalIgnoreCase));
        bool noAdmin = a.Any(x => x.Equals("-NoAdmin", StringComparison.OrdinalIgnoreCase));
        switch (a.FirstOrDefault())
        {
            case "save": return Save(store, toShared ? shared : account);
            case "apply": return await Apply(store, [shared, account], noAdmin);
            case "admin":
            {
                if (store.AdminQueue.Count == 0) return new(0, "No admin changes waiting");
                // First the list, for omawin to show and ask about; then, on yes, the one prompt.
                if (!a.Contains("--yes")) return new(3, "These need admin, in one prompt:\n  " + string.Join("\n  ", store.DescribeAdmin()));
                return Result(await store.ApplyAdminAsync());
            }
            default: return new(2, SettingsUsage);
        }
    }

    // Ids saved: those the file already has, plus every one changed through omawin (the history).
    static Answer Save(SettingStore store, string path)
    {
        string[]? existing = File.Exists(path) ? File.ReadAllLines(path) : null;
        var saved = SettingValueFile.Merge(existing, store.History.ChangedIds(), id =>
            store.Find(id) is { Kind: SettingKind.Switch or SettingKind.Choice or SettingKind.Slider } e ? (e.Id, store.ReadNow(e)) : null);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, saved.Lines);
        return new(0, $"{saved.Count} setting{(saved.Count == 1 ? "" : "s")} in {path} ({saved.Added} new)");
    }

    // The shared folder's file, then the account's (the account wins, as for launcher files).
    static async Task<Answer> Apply(SettingStore store, string[] files, bool noAdmin)
    {
        var wanted = SettingValueFile.Read(files.Where(File.Exists).Select(File.ReadAllLines));
        if (wanted.Count == 0) return new(0, $"No settings.txt to apply ({string.Join(", ", files)})");
        var report = new List<string>();
        int failed = 0;
        foreach (var (id, value) in wanted)
        {
            if (store.Find(id) is not { } e) { report.Add($"skipped {id}: not in this omawin's catalog, or not on this Windows"); continue; }
            if (e.Kind is SettingKind.Link or SettingKind.Action) { report.Add($"skipped {id}: has no value"); continue; }
            var r = store.Set(e, value, "apply");
            if (!r.Ok) failed++;
            if (r.Ok && !r.Changed && !r.Queued) continue; // already so: nothing to say
            report.Add(r.Message);
        }
        int waiting = store.AdminQueue.Count;
        if (waiting > 0 && !noAdmin)
        {
            // Typed by you (omawin settings apply): you asked, so this is the one prompt.
            var o = await store.ApplyAdminAsync();
            report.Add(o.Message);
            if (!o.Ok) failed++;
        }
        else if (waiting > 0) report.Add($"{waiting} admin change{(waiting == 1 ? "" : "s")} waiting: omawin settings admin");
        return new(failed == 0 ? 0 : 1, report.Count == 0 ? "Everything is already as settings.txt says" : string.Join('\n', report));
    }

    static Answer Result(SettingOutcome o) => new(o.Ok ? 0 : 1, o.Message);

    static string Unknown(string[] a) => a.Length > 1 ? $"no setting '{a[1]}' (omawin setting list)" : "which setting? (omawin setting list)";
}
