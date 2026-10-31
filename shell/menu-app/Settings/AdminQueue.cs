// Settings that need admin, waiting for the one prompt (roadmap 2.9, plans/02-settings-and-one-search.md §8):
// %LOCALAPPDATA%\launcher\settings-admin-queue.tsv, "<id>\t<value>" per line, the same id twice
// keeping the last. Nothing here elevates: Store.ApplyAdminAsync hands the plan to
// bin/settings-admin.ps1 through one UAC prompt, after the list has been shown.
// Pure C# and a plain file: tested in menu-app.Tests.

namespace SystemMenu;

public sealed class SettingAdminQueue(string path)
{
    public string FilePath { get; } = path;
    readonly object gate = new();

    public List<(string Id, string Value)> Items()
    {
        lock (gate)
        {
            try
            {
                return File.Exists(FilePath)
                    ? File.ReadAllLines(FilePath).Select(l => l.Split('\t')).Where(p => p.Length == 2 && p[0].Length > 0).Select(p => (p[0], p[1])).ToList()
                    : [];
            }
            catch (IOException) { return []; }
        }
    }

    public int Count => Items().Count;

    public void Add(string id, string value)
    {
        lock (gate)
        {
            var items = Items().Where(i => !i.Id.Equals(id, StringComparison.OrdinalIgnoreCase)).Append((Id: id, Value: value));
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllLines(FilePath, items.Select(i => $"{i.Id}\t{i.Value}"));
        }
    }

    public void Remove(string id)
    {
        lock (gate)
        {
            var items = Items();
            if (items.RemoveAll(i => i.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) == 0) return;
            if (items.Count == 0) Clear();
            else File.WriteAllLines(FilePath, items.Select(i => $"{i.Id}\t{i.Value}"));
        }
    }

    public void Clear()
    {
        lock (gate) { try { File.Delete(FilePath); } catch (IOException) { } }
    }

    // One line of the plan the elevated script reads: resolved and checked here, never catalog
    // text it would interpret. "<id>\t<verb>\t<target>\t<type or args>\t<value>"; null when the
    // entry has no admin path (only machine-wide registry values, this account's policies, and a
    // closed list of builtins do; the script checks each again).
    public static readonly string[] AdminBuiltins =
        ["hibernate", "sudo", "windows-feature", "service", "utf8", "firewall-dev", "ssh-agent", "ai-paint"];

    // HKCU\Software\Policies: a standard account can only read it, so its lines are admin too.
    // The elevated run may be another account (a standard user's prompt takes an admin's
    // password), so the script writes them to this account's hive by its SID, never to HKCU there.
    public const string UserPolicies = @"HKCU\Software\Policies\";

    public static string? PlanLine(SettingEntry e, string value)
    {
        if (!e.Admin) return null;
        if (value.Contains('\t') || value.Contains('\n')) return null;
        return e.How.Verb switch
        {
            "reg" when (e.How.Target.StartsWith(@"HKLM\", StringComparison.OrdinalIgnoreCase)
                        || e.How.Target.StartsWith(UserPolicies, StringComparison.OrdinalIgnoreCase)) && e.How.Args is [var type, ..] =>
                string.Join('\t', e.Id, "reg", e.How.Target, type, SettingValues.FromWords(e, value)),
            // A service's switch goes as the start mode its line names for on / off.
            "builtin" when e.How.Target == "service" =>
                ServiceStart.ModeFor(e, value) is { } mode && e.How.Args is [var service] ? string.Join('\t', e.Id, "builtin", "service", service, mode) : null,
            "builtin" when AdminBuiltins.Contains(e.How.Target) =>
                string.Join('\t', e.Id, "builtin", e.How.Target, string.Join(' ', e.How.Args), value),
            _ => null,
        };
    }

    // Every line one change needs: one per value a joined line (reg A + reg B) sets, all or none.
    public static List<string> PlanLines(SettingEntry e, string value)
    {
        var lines = e.Parts.Select(p => PlanLine(p, value)).ToList();
        return lines.All(l => l != null) ? lines.OfType<string>().ToList() : [];
    }

    // How a queued value reads in the list before the prompt: an action's resolved value (the
    // dev runtimes a firewall rule is for) by name, not as the paths it carries.
    public static string? DescribeValue(SettingEntry e, string value) =>
        e.How.Verb == "builtin" && e.How.Target == "firewall-dev" ? DevRuntimes.Describe(value) : null;
}
