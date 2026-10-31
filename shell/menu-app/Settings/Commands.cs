// `omawin setting …`, `omawin settings …` (roadmap 1B.4, 1B.5) and `omawin tune-up` (8G): answered by the menu app,
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
    public const string SettingsUsage = "omawin settings save [-Shared] | apply [-NoAdmin] [-Changed] | admin";
    public const string TuneUpUsage = "omawin tune-up [apply [-Auto]]";

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

    // omawin settings save [-Shared] | apply [-NoAdmin] [-Changed] | admin [--yes]
    // account / shared: the two settings.txt files (the account folder's, the PC's shared folder's);
    // applied: the record of what apply last set (settings-applied.txt; null: none kept).
    public static async Task<Answer> Settings(SettingStore store, string[] a, string account, string shared, string? applied = null)
    {
        bool Flag(string name) => a.Any(x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
        switch (a.FirstOrDefault())
        {
            case "save": return Save(store, Flag("-Shared") ? shared : account);
            case "apply": return await Apply(store, [shared, account], Flag("-NoAdmin"), applied, Flag("-Changed"));
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
    // changedOnly (an update, roadmap 1B.5): only the lines that changed since the last apply, so
    // a setting you've changed here since isn't put back by an update that didn't touch it.
    static async Task<Answer> Apply(SettingStore store, string[] files, bool noAdmin, string? applied, bool changedOnly)
    {
        var wanted = SettingValueFile.Read(files.Where(File.Exists).Select(File.ReadAllLines));
        if (wanted.Count == 0) return new(0, $"No settings.txt to apply ({string.Join(", ", files)})");
        var before = applied != null && File.Exists(applied) ? SettingValueFile.Read([File.ReadAllLines(applied)]) : [];
        if (changedOnly)
        {
            wanted = SettingValueFile.Changed(wanted, before);
            if (wanted.Count == 0) return new(0, "settings.txt hasn't changed since it was last applied");
        }
        var report = new List<string>();
        var done = new List<(string Id, string Value)>();
        int failed = 0;
        foreach (var (id, value) in wanted)
        {
            if (store.Find(id) is not { } e) { report.Add($"skipped {id}: not in this omawin's catalog, or not on this Windows"); continue; }
            if (e.Kind is SettingKind.Link or SettingKind.Action) { report.Add($"skipped {id}: has no value"); continue; }
            var r = store.Set(e, value, "apply");
            if (!r.Ok) failed++;
            else done.Add((id, value)); // set, already so, or waiting for the admin prompt: not set again by an update
            if (r.Ok && !r.Changed && !r.Queued) continue; // already so: nothing to say
            report.Add(r.Message);
        }
        if (applied != null && done.Count > 0)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(applied)!);
                File.WriteAllLines(applied, SettingValueFile.Record(before, done));
            }
            catch (IOException) { } // it only costs setting them again on the next update
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

    // omawin tune-up | tune-up apply [-Auto] [--yes] (default/launcher/tune-up.txt, TuneUp.cs).
    // catalog: every entry, "check" ones too, so the report can say how many wait to be verified;
    // only the visible ones are read or set. apply first lists what it will change and asks
    // (code 3), then sets each through the store (undoable, admin ones queued) and, on that same
    // yes, applies the admin ones in the one prompt. -Auto (install, an update): only the 'auto'
    // ones, never admin, nothing asked.
    public static async Task<Answer> TuneUp(SettingStore store, string[] a, IReadOnlyList<Recommendation> recs,
        IReadOnlyList<SettingEntry> catalog, int build, bool unverified = false)
    {
        bool apply = a.FirstOrDefault() == "apply";
        if (a.Length > 0 && !apply && a[0] != "list") return new(2, TuneUpUsage);
        bool autoOnly = a.Any(x => x.Equals("-Auto", StringComparison.OrdinalIgnoreCase));
        var rows = SystemMenu.TuneUp.Report(recs, catalog, Reader(store, recs, catalog, build, unverified), build, unverified);
        if (!apply) return new(0, SystemMenu.TuneUp.Format(rows).TrimEnd());

        var todo = SystemMenu.TuneUp.ToApply(rows, autoOnly);
        if (autoOnly) todo = todo.Where(r => !r.Entry.Admin).ToList(); // auto never prompts (TuneUp.Check holds the file to it)
        if (todo.Count == 0) return new(0, "Every recommended setting is already applied (omawin tune-up lists them)");
        if (!autoOnly && !a.Contains("--yes"))
        {
            int admin = todo.Count(r => r.Entry.Admin);
            return new(3, $"omawin tune-up apply sets {todo.Count} Windows setting{(todo.Count == 1 ? "" : "s")}"
                + (admin > 0 ? $" ({admin} in one admin prompt)" : "") + ":\n  "
                + string.Join("\n  ", todo.Select(r => $"{r.Entry.Name}: {store.Label(r.Entry, r.Rec.Value)}" + (r.Entry.Admin ? " [admin]" : "")))
                // The prompt applies the whole queue: what was already waiting is named too.
                + (admin > 0 && OtherAdmin(store, todo) is { Count: > 0 } other ? "\nAlready waiting for admin, in the same prompt:\n  " + string.Join("\n  ", other) : ""));
        }
        var report = new List<string>();
        int failed = 0;
        foreach (var r in todo)
        {
            var o = store.Set(r.Entry, r.Rec.Value, "tune-up");
            if (!o.Ok) failed++;
            report.Add(o.Message);
        }
        // You said yes to the list, admin ones included: this is the one prompt.
        if (!autoOnly && todo.Any(r => r.Entry.Admin) && store.AdminQueue.Count > 0)
        {
            var o = await store.ApplyAdminAsync();
            if (!o.Ok) failed++;
            report.Add(o.Message);
        }
        report.Add("Each is undone like any setting: omawin setting undo, or in the system menu.");
        return new(failed == 0 ? 0 : 1, string.Join('\n', report));
    }

    static List<string> OtherAdmin(SettingStore store, List<TuneUpRow> todo) => store.AdminQueue.Items()
        .Where(i => !todo.Any(r => r.Entry.Id.Equals(i.Id, StringComparison.OrdinalIgnoreCase)))
        .Select(i => store.Find(i.Id) is { } e ? $"{e.Name}: {store.Label(e, i.Value)}" : $"{i.Id}: not in this omawin's catalog, skipped").ToList();

    // The visible recommended settings, read side by side (builtins can take a moment), then
    // looked up as the report asks for each.
    static Func<SettingEntry, string?> Reader(SettingStore store, IReadOnlyList<Recommendation> recs,
        IReadOnlyList<SettingEntry> catalog, int build, bool unverified)
    {
        var wanted = new HashSet<string>(recs.Select(r => r.Id), StringComparer.OrdinalIgnoreCase);
        var read = catalog.Where(e => wanted.Contains(e.Id) && (!e.Unverified || unverified) && e.FitsBuild(build)).ToList();
        var values = new System.Collections.Concurrent.ConcurrentDictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        Parallel.ForEach(read, e => values[e.Id] = store.ReadNow(e));
        return e => values.TryGetValue(e.Id, out var v) ? v : store.ReadNow(e);
    }

    static Answer Result(SettingOutcome o) => new(o.Ok ? 0 : 1, o.Message);

    static string Unknown(string[] a) => a.Length > 1 ? $"no setting '{a[1]}' (omawin setting list)" : "which setting? (omawin setting list)";
}
