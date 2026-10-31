// The settings service (plans/02-settings-and-one-search.md §6): the catalog's entries with their
// values, read lazily and cached; every change through Set, so validation, the admin rule and the
// undo history can't be skipped. The handlers that touch Windows are handed in (WinSettings wires
// the real ones; menu-app.Tests an in-memory registry), so this file is pure C# and tested.
//
// What the system menu and One Search use: Entries, Peek (a row's value, never blocking the UI),
// Set / Toggle / Step / UndoLast (off the UI thread), Changed (a value changed: redraw that row).

using System.Collections.Concurrent;

namespace SystemMenu;

public sealed record SettingOutcome(bool Ok, string Message, bool Queued = false, bool Changed = false);

// What the one elevated run did: whether it ran at all (false: the prompt was declined), and
// "<id>: <reason>" for each change that failed.
public sealed record AdminRun(bool Ran, IReadOnlyList<string> Failures);

public sealed class SettingStore(
    Func<IReadOnlyList<SettingEntry>> entries,
    Func<SettingEntry, ISettingHandler> handlerFor,
    SettingHistory history,
    SettingAdminQueue adminQueue,
    Action<Exception>? log = null)
{
    // Last known value per id; missing: not read yet. A null value: read, but Windows wouldn't say.
    readonly ConcurrentDictionary<string, string?> values = new(StringComparer.OrdinalIgnoreCase);
    readonly ConcurrentDictionary<string, byte> reading = new(StringComparer.OrdinalIgnoreCase);
    readonly ConcurrentDictionary<string, byte> watched = new(StringComparer.OrdinalIgnoreCase);

    public SettingHistory History { get; } = history;
    public SettingAdminQueue AdminQueue { get; } = adminQueue;

    // An id's value changed (read for the first time, written here, or changed elsewhere). Raised
    // on any thread: a window marshals it to its dispatcher.
    public event Action<string>? Changed;

    // Called once per entry, the first time it's read: start watching what it depends on, so a
    // change made elsewhere shows (Watch.cs). Only entries someone looked at are watched.
    public Action<SettingEntry>? Watch { get; set; }

    // The one elevated run for queued admin changes: given the plan's lines, returns what happened.
    // Unset (tests, or a PC where it can't run): admin changes stay queued.
    public Func<IReadOnlyList<string>, Task<AdminRun>>? RunAdmin { get; set; }

    // An admin change whose value is worked out on this side before it's queued (the dev
    // runtimes found on this account's PATH, which the elevated run, maybe another account,
    // can't see). Returns the value to queue, or an error to show instead. Unset: queued as given.
    public Func<SettingEntry, string, (string? Value, string? Error)>? ResolveAdmin { get; set; }

    public IReadOnlyList<SettingEntry> Entries() => entries();

    public SettingEntry? Find(string id) => Entries().FirstOrDefault(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public ISettingHandler Handler(SettingEntry e) => handlerFor(e);

    // A choice's values: the handler's (refresh rates, audio devices: from the PC) or the catalog's.
    // Asking the PC can take a moment (the audio devices), so its answer is kept for a while.
    readonly ConcurrentDictionary<string, (long At, IReadOnlyList<Choice> List)> choices = new(StringComparer.OrdinalIgnoreCase);
    const long ChoicesKeptMs = 30_000;

    public IReadOnlyList<Choice> Choices(SettingEntry e)
    {
        if (e.Kind != SettingKind.Choice) return e.Choices;
        if (choices.TryGetValue(e.Id, out var kept) && Environment.TickCount64 - kept.At < ChoicesKeptMs) return kept.List;
        IReadOnlyList<Choice> list;
        try { list = Handler(e).Choices(e); }
        catch (Exception ex) { log?.Invoke(ex); list = e.Choices; }
        choices[e.Id] = (Environment.TickCount64, list);
        return list;
    }

    public string Label(SettingEntry e, string? value) => SettingValues.Label(e, value, Choices(e));

    // The same without waiting, for a row being drawn: the choices known so far (the catalog's
    // until the PC's arrive, then Changed redraws it).
    public string LabelNow(SettingEntry e, string? value)
    {
        if (e.Kind != SettingKind.Choice || choices.ContainsKey(e.Id)) return Label(e, value);
        bool fast;
        try { fast = Handler(e).Fast(e); } catch (Exception ex) { log?.Invoke(ex); fast = true; }
        if (fast) return Label(e, value);
        Task.Run(() => { Choices(e); Changed?.Invoke(e.Id); });
        return SettingValues.Label(e, value, e.Choices);
    }

    static bool HasValue(SettingEntry e) => e.Kind is SettingKind.Switch or SettingKind.Choice or SettingKind.Slider;

    // Known without asking Windows? (A row can then draw its final state at once.)
    public bool TryCached(SettingEntry e, out string? value) => values.TryGetValue(e.Id, out value);

    // For a row on screen: the value if known; else a read is started (at once for fast handlers,
    // on the thread pool for the rest) and null comes back now ("…" in the row), with Changed
    // raised when it lands.
    public string? Peek(SettingEntry e)
    {
        if (!HasValue(e)) return null;
        if (values.TryGetValue(e.Id, out var v)) return v;
        bool fast;
        try { fast = Handler(e).Fast(e); } catch (Exception ex) { log?.Invoke(ex); return null; }
        if (fast) return ReadNow(e);
        if (reading.TryAdd(e.Id, 0))
            Task.Run(() => { try { ReadNow(e); } finally { reading.TryRemove(e.Id, out _); } });
        return null;
    }

    // Ask Windows now. Blocks for as long as the handler takes: not on the UI thread for builtins.
    public string? ReadNow(SettingEntry e)
    {
        if (!HasValue(e)) return null;
        string? v;
        try { v = Handler(e).Read(e); }
        catch (Exception ex) { log?.Invoke(ex); v = null; }
        bool known = values.TryGetValue(e.Id, out var old);
        values[e.Id] = v;
        if (watched.TryAdd(e.Id, 0))
            try { Watch?.Invoke(e); } catch (Exception ex) { log?.Invoke(ex); }
        if (!known || old != v) Changed?.Invoke(e.Id);
        return v;
    }

    // Something the entry depends on changed (Watch.cs): read it again, in the background.
    public void Invalidate(SettingEntry e) => Task.Run(() => ReadNow(e));

    // Forget every cached value (the window was shown after a while: power settings have no
    // change event worth wiring).
    public void Forget(Func<SettingEntry, bool>? which = null)
    {
        foreach (var e in Entries())
            if (which == null || which(e)) { values.TryRemove(e.Id, out _); choices.TryRemove(e.Id, out _); }
    }

    // Every change: normalize, validate, queue it if it needs admin, write, record the value
    // before it, read back to confirm. Never on the UI thread (radios and WMI block).
    public SettingOutcome Set(SettingEntry e, string value, string source = "menu")
    {
        var choices = Choices(e);
        value = SettingValues.Normalize(e, value, choices);
        if (HasValue(e) && SettingValues.Validate(e, value, choices) is { } bad) return new(false, bad);
        string? before = HasValue(e) ? ReadNow(e) : null;
        if (HasValue(e) && before == value)
        {
            // Asked for what it already is: a queued admin change to something else is dropped.
            AdminQueue.Remove(e.Id);
            return new(true, $"{e.Name}: already {SettingValues.Label(e, value, choices)}");
        }
        if (e.Admin)
        {
            if (ResolveAdmin != null)
            {
                (string? Value, string? Error) resolved;
                try { resolved = ResolveAdmin(e, value); }
                catch (Exception ex) { log?.Invoke(ex); resolved = (null, ex.Message); }
                if (resolved.Value == null) return new(false, $"{e.Name}: {resolved.Error ?? "nothing to do"}");
                value = resolved.Value;
            }
            AdminQueue.Add(e.Id, value);
            int n = AdminQueue.Count;
            var shown = SettingAdminQueue.DescribeValue(e, value) ?? SettingValues.Label(e, value, choices);
            if (!HasValue(e))
                return new(true, $"{e.Name}{(shown.Length > 0 ? $" ({shown})" : "")} needs admin; queued"
                    + (n > 1 ? $" with {n - 1} other change{(n > 2 ? "s" : "")}" : "") + ". Apply in one prompt: omawin settings admin", Queued: true);
            return new(true, $"{e.Name}: {SettingValues.Label(e, value, choices)} needs admin; queued"
                + (n > 1 ? $" with {n - 1} other change{(n > 2 ? "s" : "")}" : "") + ". Apply in one prompt: omawin settings admin", Queued: true);
        }
        string? error;
        try { error = Handler(e).Write(e, value); }
        catch (Exception ex) { log?.Invoke(ex); error = ex.Message; }
        if (error != null) return new(false, $"{e.Name}: {error}");
        if (!HasValue(e)) return new(true, e.Kind == SettingKind.Link ? $"{e.Name}: opened" : $"{e.Name}: done");
        History.Append(e.Id, before, value, source);
        var after = ReadNow(e);
        var label = SettingValues.Label(e, value, choices);
        // Null after: the handler can't read it back (a radio mid-change); trust the write.
        return after == value || after == null
            ? new(true, $"{e.Name}: {label}{SettingValues.WhenItApplies(e)}", Changed: true)
            : new(false, $"{e.Name}: set to {label}, but Windows still says {SettingValues.Label(e, after, choices)} (a policy, or a value this Windows ignores)", Changed: true);
    }

    // Enter on a row: a switch flips, a choice steps to the next value, an action or link is done.
    public SettingOutcome Toggle(SettingEntry e, string source = "menu") => e.Kind switch
    {
        SettingKind.Switch or SettingKind.Choice => Step(e, +1, source),
        SettingKind.Slider => new(false, $"{e.Name}: a slider: ← / → or omawin setting set {e.Id} <number>"),
        _ => Set(e, "", source),
    };

    // ← / → on a row: the next or previous value.
    public SettingOutcome Step(SettingEntry e, int by, string source = "menu")
    {
        // A queued admin change counts as where it is now, so a second press steps past it.
        var current = e.Admin && AdminQueue.Items().FirstOrDefault(i => i.Id.Equals(e.Id, StringComparison.OrdinalIgnoreCase)) is { Id: not null } q
            ? q.Value : ReadNow(e);
        return SettingValues.Next(e, current, by, Choices(e)) is { } next ? Set(e, next, source) : new(false, $"{e.Name}: nothing to choose from");
    }

    // Ctrl+Z in the menu, the Undo row, `omawin setting undo`: put the last change back.
    public SettingOutcome UndoLast()
    {
        if (History.LastUndoable() is not { } last) return new(false, "Nothing to undo");
        if (Find(last.Id) is not { } e) return new(false, $"{last.Id}: no longer in the catalog (or not on this Windows)");
        if (last.Before == "?") return new(false, $"{e.Name}: its earlier value wasn't known; set it yourself");
        var r = Set(e, last.Before, $"undo:{last.Line}");
        // Already that value: nothing to write, but the change is still settled, so the next undo
        // goes further back instead of stopping here.
        if (r.Ok && !r.Changed && !r.Queued) History.Append(e.Id, last.Before, last.Before, $"undo:{last.Line}");
        return r with { Message = "Undone: " + r.Message };
    }

    // The words shown before the one prompt (2.9: it says exactly what it will change).
    public List<string> DescribeAdmin() => AdminQueue.Items().Select(i => Find(i.Id) is { } e
        ? $"{e.Name}: {SettingAdminQueue.DescribeValue(e, i.Value) ?? Label(e, i.Value)}" + (e.Takes.HasFlag(Takes.Restart) ? " (needs a restart)" : e.Takes.HasFlag(Takes.SignOut) ? " (after you sign out)" : "")
        : $"{i.Id}: not in this omawin's catalog, skipped").ToList();

    // Apply everything queued in one elevated run, then read each back and record what changed.
    // Declined: the queue stays, nothing else is touched.
    public async Task<SettingOutcome> ApplyAdminAsync()
    {
        var items = AdminQueue.Items();
        if (items.Count == 0) return new(true, "No admin changes waiting");
        if (RunAdmin == null) return new(false, "Admin changes can't be applied from here: omawin settings admin");
        var known = items.Select(i => (i.Id, i.Value, Entry: Find(i.Id))).Where(x => x.Entry is { Admin: true }).ToList();
        var plan = known.Select(x => SettingAdminQueue.PlanLine(x.Entry!, x.Value)).OfType<string>().ToList();
        if (plan.Count == 0) { AdminQueue.Clear(); return new(false, "Nothing queued has an admin path; queue cleared"); }
        var before = known.ToDictionary(x => x.Id, x => ReadNow(x.Entry!), StringComparer.OrdinalIgnoreCase);
        AdminRun run;
        try { run = await RunAdmin(plan); }
        catch (Exception ex) { log?.Invoke(ex); return new(false, "The admin run failed: " + ex.Message); }
        if (!run.Ran) return new(false, "Not now: nothing changed (still queued: omawin settings admin)");
        int changed = 0;
        foreach (var (id, value, entry) in known)
            if (ReadNow(entry!) is { } now && now != before[id]) { History.Append(id, before[id], now, "admin"); changed++; }
        AdminQueue.Clear();
        bool restart = known.Any(x => x.Entry!.Takes.HasFlag(Takes.Restart));
        return run.Failures.Count == 0
            ? new(true, $"Applied {plan.Count} admin change{(plan.Count == 1 ? "" : "s")}" + (restart ? "; restart Windows to finish" : ""), Changed: changed > 0)
            : new(false, string.Join("; ", run.Failures), Changed: changed > 0);
    }
}
