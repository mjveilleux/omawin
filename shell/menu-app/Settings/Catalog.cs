// The Windows settings catalog (default/launcher/settings.txt): every setting omawin can read and
// change itself, one line each (plans/02-settings-and-one-search.md §1-2, roadmap 1B).
// This file only parses: no registry, no P/Invoke, no WinUI, so menu-app.Tests runs it anywhere.
// The handlers that read and write each setting are separate (Settings/Switches.cs, to come).

namespace SystemMenu;

public enum SettingKind { Switch, Choice, Slider, Action, Link }

[Flags]
public enum Takes { Now = 0, SignOut = 1, Restart = 2 }

public sealed record Choice(string Value, string Label);

// The "how" field, tokenized: verb, target, then key=value options and bare flags.
public sealed record How(
    string Verb,                                   // reg | spi | powercfg | builtin | link | omawin
    string Target,                                 // the path, the spi / powercfg / builtin name, the uri
    IReadOnlyList<string> Args,                    // positional words after the target (reg: its type; powercfg: ac|dc|both)
    IReadOnlyDictionary<string, string> Options,   // on=, off=, values=, range=, default=, broadcast=
    IReadOnlySet<string> Flags)                    // refresh-explorer, restart-explorer
{
    public string? On => Options.GetValueOrDefault("on");
    public string? Off => Options.GetValueOrDefault("off");
    public string? Broadcast => Options.GetValueOrDefault("broadcast");
}

public sealed record SettingEntry(
    string Id, string Icon, string Name, string Keywords, SettingKind Kind, How How,
    bool Admin, Takes Takes, int MinBuild, int MaxBuild, bool Unverified,
    IReadOnlyList<Choice> Choices, (int Min, int Max)? Range, string? Default,
    string Origin)                                 // "settings.txt:12", for error messages
{
    public string Area => Id[..Id.IndexOf('.')];
    // Shown on this Windows? (§4: Catalog.Visible does the filtering.)
    public bool FitsBuild(int build) => build >= MinBuild && (MaxBuild == 0 || build <= MaxBuild);
}

public static class Catalog
{
    public static readonly string[] Areas =
        ["look", "explorer", "privacy", "input", "system", "display", "connections", "sound", "developer"];
    static readonly HashSet<string> Verbs = ["reg", "spi", "powercfg", "builtin", "link", "omawin"];

    public sealed record Problem(string Origin, string Message);

    // Every line of every layer, later layers replacing earlier ones by id (the account folder
    // wins over the shared one, which wins over omawin's). Bad lines are skipped and reported.
    public static (List<SettingEntry> Entries, List<Problem> Problems) Parse(IEnumerable<(string File, string[] Lines)> layers)
    {
        var byId = new Dictionary<string, SettingEntry>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        var problems = new List<Problem>();
        foreach (var (file, lines) in layers)
            for (int i = 0; i < lines.Length; i++)
            {
                var origin = $"{Path.GetFileName(file)}:{i + 1}";
                var line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                try
                {
                    var e = ParseLine(line, origin);
                    if (!byId.ContainsKey(e.Id)) order.Add(e.Id);
                    byId[e.Id] = e;
                }
                catch (FormatException ex) { problems.Add(new(origin, ex.Message)); }
            }
        return (order.Select(id => byId[id]).ToList(), problems);
    }

    public static SettingEntry ParseLine(string line, string origin)
    {
        var f = line.Split('|').Select(p => p.Trim()).ToArray();
        if (f.Length is < 6 or > 7) throw new FormatException($"{f.Length} fields, want 7: id | icon | name | keywords | kind | how | notes");
        var id = f[0];
        if (id.IndexOf('.') is <= 0 || !Areas.Contains(id[..id.IndexOf('.')]))
            throw new FormatException($"id '{id}' must be <area>.<name>, area one of {string.Join(' ', Areas)}");
        var kind = f[4] switch
        {
            "switch" => SettingKind.Switch, "choice" => SettingKind.Choice, "slider" => SettingKind.Slider,
            "action" => SettingKind.Action, "link" => SettingKind.Link,
            var k => throw new FormatException($"kind '{k}'"),
        };
        var how = ParseHow(f[5]);
        var notes = f.Length > 6 ? Tokens(f[6]) : [];

        bool admin = false, unverified = false; var takes = Takes.Now; int min = 0, max = 0;
        foreach (var n in notes)
        {
            if (n == "admin") admin = true;
            else if (n == "signout") takes |= Takes.SignOut;
            else if (n == "restart") takes |= Takes.Restart;
            else if (n == "check") unverified = true;
            else if (n.StartsWith("minBuild=") && int.TryParse(n[9..], out var b1)) min = b1;
            else if (n.StartsWith("maxBuild=") && int.TryParse(n[9..], out var b2)) max = b2;
            else throw new FormatException($"note '{n}'");
        }

        // A machine-wide registry value is admin whether or not the line says so: never let an
        // HKLM write reach the non-elevated path (it would fail, or worse, half apply).
        if (how.Verb == "reg" && how.Target.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase) && !admin)
            throw new FormatException("an HKLM value needs the 'admin' note");

        var choices = how.Options.TryGetValue("values", out var vs) ? ParseChoices(vs) : [];
        (int, int)? range = how.Options.TryGetValue("range", out var r) ? ParseRange(r) : null;

        switch (kind)
        {
            case SettingKind.Switch when how.Verb == "reg" && (how.On is null || how.Off is null):
                throw new FormatException("a reg switch needs on= and off=");
            case SettingKind.Choice when how.Verb is "reg" or "spi" or "powercfg" && choices.Count == 0:
                throw new FormatException("a choice needs values=");
            case SettingKind.Slider when range is null:
                throw new FormatException("a slider needs range=a..b");
            case SettingKind.Link when how.Verb != "link":
                throw new FormatException("a link row's how is 'link <uri>'");
        }
        if (how.Verb == "reg" && (how.Args.Count == 0 || how.Args[0] is not ("dword" or "qword" or "sz" or "expand_sz")))
            throw new FormatException("reg needs a type after the path: dword | qword | sz | expand_sz");

        return new SettingEntry(id, f[1], f[2], f[3], kind, how, admin, takes, min, max, unverified,
            choices, range, how.Options.GetValueOrDefault("default"), origin);
    }

    public static How ParseHow(string text)
    {
        var t = Tokens(text);
        if (t.Count < 2 || !Verbs.Contains(t[0])) throw new FormatException($"how '{text}': want <verb> <target> …, verb one of {string.Join(' ', Verbs)}");
        var args = new List<string>();
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // link and omawin take the rest of the line as written (a command and its arguments).
        if (t[0] is "link" or "omawin") return new How(t[0], string.Join(' ', t.Skip(1)), [], options, flags);
        foreach (var w in t.Skip(2))
        {
            int eq = w.IndexOf('=');
            if (eq > 0) options[w[..eq]] = w[(eq + 1)..];
            else if (w is "refresh-explorer" or "restart-explorer") flags.Add(w);
            else args.Add(w);
        }
        return new How(t[0], t[1], args, options, flags);
    }

    // Space-separated words; double quotes group, anywhere in a word (values="1:This PC,2:Home").
    public static List<string> Tokens(string text)
    {
        var words = new List<string>();
        var current = new System.Text.StringBuilder();
        bool quoted = false, any = false;
        foreach (var c in text)
        {
            if (c == '"') { quoted = !quoted; any = true; continue; }
            if (c == ' ' && !quoted)
            {
                if (any) words.Add(current.ToString());
                current.Clear(); any = false; continue;
            }
            current.Append(c); any = true;
        }
        if (quoted) throw new FormatException($"unclosed quote in '{text}'");
        if (any) words.Add(current.ToString());
        return words;
    }

    // "1:This PC,2:Home" -> (1, This PC), (2, Home). A bare "on" is its own label.
    static List<Choice> ParseChoices(string text) =>
        text.Split(',').Select(p => p.Split(':', 2) is [var v, var l] ? new Choice(v.Trim(), l.Trim()) : new Choice(p.Trim(), p.Trim())).ToList();

    static (int, int) ParseRange(string text) =>
        text.Split("..") is [var a, var b] && int.TryParse(a, out var lo) && int.TryParse(b, out var hi) && lo <= hi
            ? (lo, hi) : throw new FormatException($"range '{text}': want a..b");

    // The registry target split for the reg handler: hive, key path, value name.
    public static (string Hive, string Key, string Value) SplitReg(string target)
    {
        int first = target.IndexOf('\\'), last = target.LastIndexOf('\\');
        if (first <= 0 || last == first) throw new FormatException($"reg path '{target}': want HIVE\\key\\value");
        var hive = target[..first].ToUpperInvariant();
        if (hive is not ("HKCU" or "HKLM")) throw new FormatException($"hive '{hive}': HKCU or HKLM");
        return (hive, target[(first + 1)..last], target[(last + 1)..]);
    }
}
