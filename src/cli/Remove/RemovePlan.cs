using System.Text.RegularExpressions;
using Omawin.Cli.Pkg;

namespace Omawin.Cli.Remove;

// `omawin remove <part>` (roadmap 2.10): one of omawin's optional parts taken off again, the way
// `omawin uninstall` takes all of it: the plan shown first, one question, then done. A part is a
// preset (install/packages.ps1: dictation, music, ai-agents, data, …; a plugin's programs are a
// preset too), a plugin, or a theme. What omawin put in for it goes (through the manager that
// installed it, `omawin pkg remove`); what you installed yourself, what another picked preset
// needs, and your own files stay, and the plan says so. Pure: tested in cli.Tests.

// One install/packages.ps1 preset: its winget ids, its other installs (Extras: installers of
// their own) and the commands it brings (Tools: also the processes stopped before removing).
public sealed record Preset(string Name, string Summary, string? Plugin, IReadOnlyList<string> Winget, IReadOnlyList<string> Extras, IReadOnlyList<string> Tools);

public static class PresetFile
{
    // Each preset is one line of $Presets: 'name' = @{ Summary = '…'; Winget = @('a', 'b'); … }.
    // cli.Tests reads the real file, so a preset written any other way fails there, not here.
    static readonly Regex Line = new(@"^\s*'(?<name>[a-z0-9-]+)'\s*=\s*@\{(?<body>.*)\}\s*$");

    public static List<Preset> Parse(IEnumerable<string> lines)
    {
        var list = new List<Preset>();
        bool inside = false;
        foreach (var raw in lines)
        {
            if (raw.TrimStart().StartsWith("$Presets", StringComparison.Ordinal)) { inside = true; continue; }
            if (!inside) continue;
            if (raw.Trim() == "}") break;
            var m = Line.Match(raw);
            if (!m.Success) continue;
            var body = m.Groups["body"].Value;
            list.Add(new Preset(m.Groups["name"].Value, Text(body, "Summary") ?? "", Text(body, "Plugin"),
                Array(body, "Winget"), Array(body, "Extras"), Array(body, "Tools")));
        }
        return list;
    }

    static string? Text(string body, string key) =>
        Regex.Match(body, key + @"\s*=\s*(?:'(?<v>(?:[^']|'')*)'|""(?<v>[^""]*)"")") is { Success: true } m ? m.Groups["v"].Value.Replace("''", "'") : null;

    static List<string> Array(string body, string key) =>
        Regex.Match(body, key + @"\s*=\s*@\((?<v>[^)]*)\)") is { Success: true } m
            ? [.. Regex.Matches(m.Groups["v"].Value, @"'([^']+)'").Select(x => x.Groups[1].Value)]
            : [];
}

public abstract record Step(string Says);
public sealed record RunOmawin(string[] Words, string Why) : Step(Why);            // omawin preset remove dictation
public sealed record StopProcess(string Name) : Step($"stop {Name} (it can't be removed while it runs)");
public sealed record RemovePackage(string Key, string Name, string From) : Step($"remove {Name} ({From})");
public sealed record DeleteFolder(string Path, string What) : Step($"delete {What}: {Path}");

public sealed record RemovePlan(string Part, List<Step> Steps, List<string> Left, string? Error = null);

// What's on this PC, as the planner needs it.
public sealed record RemoveFacts(
    IReadOnlyList<Preset> Presets,
    IReadOnlyCollection<string> Picked,          // %LOCALAPPDATA%\launcher\presets.txt
    IReadOnlyList<PackageRow> Record,            // %LOCALAPPDATA%\launcher\packages.tsv
    string AppData);                             // %APPDATA%

public static class RemovePlanner
{
    public const string Usage = "omawin remove <part>: a preset (omawin preset list: dictation, music, ai-agents, …), plugin <id>, or theme <name>; --dry-run shows only, --yes asks nothing";

    // Files omawin itself fetched for a preset, beyond its packages (they'd stay behind them).
    static readonly Dictionary<string, (string Relative, string What)[]> Downloads = new()
    {
        ["dictation"] = [(@"com.pais.handy\models\parakeet-tdt-0.6b-v3-int8", "the speech model omawin downloaded (about 460 MB)")],
    };

    // What stays that's worth saying, beyond the rule (your files are never touched).
    static readonly Dictionary<string, string> Stays = new()
    {
        ["music"] = @"your playlists, podcasts, stations and ratings (Music\omawin), and the player itself, which is part of omawin",
        ["dictation"] = "Win+H stays bound; it does nothing until dictation is back (omawin preset add dictation). Handy's own settings stay in %APPDATA%\\com.pais.handy",
        ["wsl"] = "your Linux: omawin never removes a Linux with your files in it; omawin linux backup, then wsl --unregister, if you mean to",
    };

    // Packages no part removes: the shared runtime other programs (the menu app's .NET build) use.
    static readonly HashSet<string> Shared = new(StringComparer.OrdinalIgnoreCase) { "Microsoft.DotNet.DesktopRuntime.9" };

    public static RemovePlan For(IReadOnlyList<string> args, RemoveFacts facts)
    {
        var words = args.Where(a => !a.StartsWith('-')).ToList();
        if (words.Count == 0) return new("", [], [], Usage);
        switch (words[0].ToLowerInvariant())
        {
            case "theme" when words.Count > 1:
                return new($"the {words[1]} theme", [new RunOmawin(["theme", "remove", words[1]], $"remove the {words[1]} theme (omawin theme remove)")],
                    ["the theme in use, if it's this one, until you pick another"]);
            case "theme":
                return new("", [], [], "omawin remove theme <name> (omawin theme list)");
            case "plugin" when words.Count > 1:
                return Plugin(words[1], facts);
            case "plugin":
                return new("", [], [], "omawin remove plugin <id> (omawin plugin list)");
            case "preset" when words.Count > 1:
                return PresetPart(words[1], facts);
            case "linux" or "wsl" or "arch":
                return new("", [], [], "omawin never removes your Linux (it has your files): omawin linux backup, then wsl --unregister <distro> if you mean to; omawin preset remove wsl stops installs asking for it");
            default:
                // A name alone: a preset (music, dictation, …), else a plugin, else say what there is.
                if (Find(facts, words[0]) is not null) return PresetPart(words[0], facts);
                return new("", [], [], $"no part '{words[0]}'. {Usage}");
        }
    }

    // "agents" means the ai-agents preset; a plugin's short name its preset.
    static Preset? Find(RemoveFacts facts, string name)
    {
        var n = name.ToLowerInvariant();
        if (n is "agents" or "agent") n = "ai-agents";
        return facts.Presets.FirstOrDefault(p => p.Name == n)
            ?? facts.Presets.FirstOrDefault(p => p.Plugin != null && (p.Plugin == n || p.Plugin == "omawin." + n));
    }

    static RemovePlan Plugin(string id, RemoveFacts facts)
    {
        // A plugin with programs of its own is its preset (Plugin in install/packages.ps1).
        if (Find(facts, id) is { Plugin: not null } preset) return PresetPart(preset.Name, facts);
        return new($"the {id} plugin", [new RunOmawin(["plugin", "remove", id], $"turn the {id} plugin off (a gallery plugin's download goes too)")], []);
    }

    static RemovePlan PresetPart(string name, RemoveFacts facts)
    {
        if (Find(facts, name) is not { } preset) return new("", [], [], $"no preset '{name}' (omawin preset list)");
        var steps = new List<Step>();
        var left = new List<string>();
        bool picked = facts.Picked.Contains(preset.Name, StringComparer.OrdinalIgnoreCase);
        if (picked)
            steps.Add(new RunOmawin(["preset", "remove", preset.Name], preset.Plugin != null
                ? $"turn the {preset.Plugin} plugin off and unpick its preset (install stops asking for it)"
                : $"unpick the {preset.Name} preset (install stops asking for it)"));

        // Its packages: those omawin put in, that nothing else picked needs.
        var others = facts.Presets.Where(p => p.Name != preset.Name && facts.Picked.Contains(p.Name, StringComparer.OrdinalIgnoreCase)).ToList();
        var removing = new List<RemovePackage>();
        foreach (var id in preset.Winget)
        {
            var row = facts.Record.FirstOrDefault(r => r.Source == Managers.Winget && r.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (row == null) continue; // not here (or not by winget): nothing to do
            if (row.Yours) { left.Add($"{row.Name}: you installed it yourself"); continue; }
            if (Shared.Contains(id)) { left.Add($"{row.Name}: other programs use it"); continue; }
            if (others.FirstOrDefault(p => p.Winget.Contains(id, StringComparer.OrdinalIgnoreCase)) is { } other)
            {
                left.Add($"{row.Name}: the {other.Name} preset uses it too");
                continue;
            }
            removing.Add(new RemovePackage(row.Key, row.Name, Managers.Label(row.Source)));
        }
        if (removing.Count > 0) steps.AddRange(preset.Tools.Select(t => new StopProcess(t)));
        steps.AddRange(removing);
        foreach (var extra in preset.Extras)
            if (facts.Record.FirstOrDefault(r => r.Name.Equals(extra, StringComparison.OrdinalIgnoreCase)) is { } row && !row.Yours
                && row.Source is Managers.Winget or Managers.Scoop or Managers.Mise or Managers.Choco)
                steps.Add(new RemovePackage(row.Key, row.Name, Managers.Label(row.Source)));
            else
                left.Add($"{extra}: installed its own way; Install › Remove › Packages lists it if a manager has it");
        foreach (var (relative, what) in Downloads.GetValueOrDefault(preset.Name, []))
            steps.Add(new DeleteFolder(Path.Combine(facts.AppData, relative), what));
        if (Stays.TryGetValue(preset.Name, out var stays)) left.Add(stays);
        if (steps.Count == 0) return new(preset.Name, [], left, $"nothing of {preset.Name} is on this PC (it isn't picked, and omawin installed none of it)");
        return new(preset.Name, steps, left);
    }
}
