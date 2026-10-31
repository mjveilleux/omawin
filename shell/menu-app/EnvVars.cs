// `omawin env` (roadmap 8.36): your environment variables and the PC's, read and changed without
// Windows' 1995 dialog, every change undoable. Yours are written at once and announced
// (WM_SETTINGCHANGE "Environment", so new terminals see them); the PC's need admin and go through
// the one prompt (bin/settings-admin.ps1's env verb), after the change is shown.
//
// This file is the command and its rules; the registry is behind IEnvStore (EnvVarsWin.cs on
// Windows, a dictionary in menu-app.Tests). Every change goes to %LOCALAPPDATA%\launcher\
// env-history.tsv through SettingHistory, ids "env:user:<NAME>" / "env:machine:<NAME>", values
// "=<value>" or "-" for not set, so undo puts back exactly what was there, absence included.

namespace SystemMenu;

public enum EnvScope { User, Machine }

public sealed record EnvVar(string Name, string Value, bool Expand);

public interface IEnvStore
{
    IReadOnlyList<EnvVar> All(EnvScope scope);
    EnvVar? Get(EnvScope scope, string name);
    // Yours: written now. Null value: deleted. Returns null, or why not.
    string? SetUser(string name, string? value);
    // The PC's, in the one elevated run: true when it ran (the prompt was approved), with any failure.
    Task<(bool Ran, string? Error)> SetMachineAsync(string name, string? value);
    // Tell running programs (Explorer, and through it what starts next) the environment changed.
    void Announce();
}

public static class EnvVars
{
    public const string Usage = "omawin env [user|machine] | get <name> | set <name> <value> [-Machine] | unset <name> [-Machine] | undo";

    // Windows takes almost anything in a name except '=', but a name a shell can't type is a
    // mistake waiting to happen: letters, digits, _ ( ) . - starting with a letter, _ or ( (as
    // ProgramFiles(x86) does). bin/settings-admin.ps1 checks the same again.
    public static bool ValidName(string name) =>
        name.Length is > 0 and <= 255 && (char.IsAsciiLetter(name[0]) || name[0] is '_' or '(')
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '(' or ')' or '.' or '-');

    // Values are kept on one line in the history and the elevated plan.
    public static bool ValidValue(string value) => !value.Contains('\t') && !value.Contains('\n') && !value.Contains('\r') && value.Length <= 32_767;

    public static string Id(EnvScope scope, string name) => $"env:{(scope == EnvScope.User ? "user" : "machine")}:{name}";

    public static (EnvScope Scope, string Name)? ParseId(string id) =>
        id.Split(':', 3) is ["env", var s, var n] && s is "user" or "machine" ? (s == "user" ? EnvScope.User : EnvScope.Machine, n) : null;

    public static string Recorded(string? value) => value == null ? "-" : "=" + value;
    public static string? FromRecorded(string recorded) => recorded.StartsWith('=') ? recorded[1..] : null;

    // A PATH-like value shown as its entries, one a line.
    public static bool IsList(EnvVar v) => v.Name.Equals("Path", StringComparison.OrdinalIgnoreCase) || v.Name.Equals("PATHEXT", StringComparison.OrdinalIgnoreCase)
        || v.Name.Equals("PSModulePath", StringComparison.OrdinalIgnoreCase);

    public static async Task<SettingCommands.Answer> Answer(IEnvStore store, SettingHistory history, string[] a)
    {
        bool machine = a.Any(x => x.Equals("-Machine", StringComparison.OrdinalIgnoreCase));
        bool yes = a.Contains("--yes");
        var words = a.Where(x => !x.Equals("-Machine", StringComparison.OrdinalIgnoreCase) && x != "--yes").ToList();
        var scope = machine ? EnvScope.Machine : EnvScope.User;
        switch (words.FirstOrDefault())
        {
            case null or "list" or "user" or "machine":
            {
                var only = words.FirstOrDefault() switch { "user" => EnvScope.User, "machine" => EnvScope.Machine, _ => (EnvScope?)null };
                return new(0, Format(store, only));
            }
            case "get":
            {
                if (words.Count < 2) return new(1, "omawin env get <name>");
                var v = store.Get(scope, words[1]) ?? (machine ? null : store.Get(EnvScope.Machine, words[1]));
                return v == null ? new(1, $"{words[1]} isn't set{(machine ? " for the PC" : "")}") : new(0, v.Value);
            }
            case "set" or "unset":
            {
                bool unset = words[0] == "unset";
                if (words.Count < (unset ? 2 : 3)) return new(1, unset ? "omawin env unset <name> [-Machine]" : "omawin env set <name> <value> [-Machine]");
                var name = words[1];
                if (!ValidName(name)) return new(1, $"'{name}': a name of letters, digits, _ ( ) . - starting with a letter, _ or (");
                string? value = unset ? null : string.Join(' ', words.Skip(2));
                if (value != null && !ValidValue(value)) return new(1, "a value is one line, at most 32767 characters");
                return await Change(store, history, scope, name, value, yes, $"command");
            }
            case "undo":
            {
                if (history.LastUndoable() is not { } last || ParseId(last.Id) is not { } target) return new(1, "Nothing to undo");
                var back = FromRecorded(last.Before);
                var r = await Change(store, history, target.Scope, target.Name, back, yes, $"undo:{last.Line}");
                return r with { Text = r.Code == 0 && !r.Text.StartsWith("Undone") ? "Undone: " + r.Text : r.Text };
            }
            default:
                return new(2, Usage);
        }
    }

    // One change, recorded with what was there; the PC's after the change is shown (code 3) and
    // approved, in the one prompt.
    static async Task<SettingCommands.Answer> Change(IEnvStore store, SettingHistory history, EnvScope scope, string name, string? value, bool yes, string source)
    {
        var before = store.Get(scope, name);
        if (before?.Value == value)
        {
            // Undoing to what it already is: settled, so the next undo goes further back.
            if (source.StartsWith("undo:")) history.Append(Id(scope, name), Recorded(value), Recorded(value), source);
            return new(0, value == null ? $"{name} isn't set" : $"{name} is already that");
        }
        var what = value == null ? $"remove {name}" : $"{name} = {value}";
        string said;
        if (scope == EnvScope.User)
        {
            if (store.SetUser(name, value) is { } error) return new(1, $"{name}: {error}");
            said = value == null ? $"{name} removed" : $"{name} set";
        }
        else
        {
            if (!yes) return new(3, $"For every account on this PC (one admin prompt): {what}" + (before != null ? $"\n  now: {before.Value}" : ""));
            var (ran, error) = await store.SetMachineAsync(name, value);
            if (!ran) return new(1, "Not now: nothing changed");
            if (error != null) return new(1, $"{name}: {error}");
            said = value == null ? $"{name} removed for every account" : $"{name} set for every account";
        }
        history.Append(Id(scope, name), Recorded(before?.Value), Recorded(value), source);
        store.Announce();
        return new(0, said + "; new terminals see it (omawin env undo puts it back)");
    }

    public static string Format(IEnvStore store, EnvScope? only)
    {
        var lines = new List<string>();
        foreach (var scope in new[] { EnvScope.User, EnvScope.Machine })
        {
            if (only is { } o && o != scope) continue;
            var vars = store.All(scope).OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase).ToList();
            lines.Add(scope == EnvScope.User ? $"Yours ({vars.Count}):" : $"The PC's, for every account ({vars.Count}; changing one needs admin):");
            int w = vars.Count == 0 ? 0 : Math.Min(28, vars.Max(v => v.Name.Length) + 2);
            foreach (var v in vars)
            {
                if (IsList(v))
                {
                    var parts = v.Value.Split(';', StringSplitOptions.RemoveEmptyEntries);
                    lines.Add($"  {v.Name.PadRight(w)}{parts.Length} entries (omawin path checks them)");
                    lines.AddRange(parts.Select(p => $"  {"".PadRight(w)}  {p}"));
                }
                else lines.Add($"  {v.Name.PadRight(w)}{v.Value}");
            }
            lines.Add("");
        }
        lines.Add(Usage);
        return string.Join('\n', lines);
    }
}
