using System.Text.RegularExpressions;
using SystemMenu.Plugins;

namespace Omawin.PluginGallery;

// What omawin's QML host (src/qs-host) gives a plugin, read from the host's own files, so the
// verdicts follow the host as it grows: its modules (each qml/**/qmldir) and their types, the
// members of its Quickshell singleton, and the Qt modules it ships (deploy/Qt.qml, the file
// windeployqt reads to decide which of Qt's QML modules go in the release).
public sealed class HostCapabilities
{
    public HashSet<string> Modules { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, HashSet<string>> Types { get; } = new(StringComparer.Ordinal);
    public HashSet<string> QuickshellMembers { get; } = new(StringComparer.Ordinal);
    // Omarchy's own qs.Commons and qs.Ui types (its shell/Commons and shell/Ui): a plugin using
    // one the host hasn't got yet needs it. Empty when Omarchy's shell wasn't given.
    public Dictionary<string, string> OmarchyTypes { get; } = new(StringComparer.Ordinal);

    // Quickshell's own types (quickshell.org's reference: the Quickshell and Quickshell.Io
    // modules), by name: a plugin using one the host hasn't got needs it. Quickshell's other
    // modules (Hyprland, Wayland, Services.*) aren't listed: importing them is already a need.
    public static readonly Dictionary<string, string> QuickshellTypes = new(StringComparer.Ordinal)
    {
        ["BoundComponent"] = "Quickshell", ["ColorQuantizer"] = "Quickshell", ["DesktopEntries"] = "Quickshell",
        ["ElapsedTimer"] = "Quickshell", ["FloatingWindow"] = "Quickshell", ["LazyLoader"] = "Quickshell",
        ["ObjectModel"] = "Quickshell", ["PanelWindow"] = "Quickshell", ["PersistentProperties"] = "Quickshell",
        ["PopupAnchor"] = "Quickshell", ["PopupWindow"] = "Quickshell", ["QsMenuAnchor"] = "Quickshell",
        ["QsMenuOpener"] = "Quickshell", ["QsWindow"] = "Quickshell", ["Region"] = "Quickshell",
        ["Reloadable"] = "Quickshell", ["Retainable"] = "Quickshell", ["Scope"] = "Quickshell",
        ["ScriptModel"] = "Quickshell", ["ShellRoot"] = "Quickshell", ["Singleton"] = "Quickshell",
        ["SystemClock"] = "Quickshell", ["TransformWatcher"] = "Quickshell", ["Variants"] = "Quickshell",
        ["DataStreamParser"] = "Quickshell.Io", ["FileView"] = "Quickshell.Io", ["FileViewAdapter"] = "Quickshell.Io",
        ["IpcHandler"] = "Quickshell.Io", ["JsonAdapter"] = "Quickshell.Io", ["JsonObject"] = "Quickshell.Io",
        ["Process"] = "Quickshell.Io", ["Socket"] = "Quickshell.Io", ["SocketServer"] = "Quickshell.Io",
        ["SplitParser"] = "Quickshell.Io", ["StdioCollector"] = "Quickshell.Io",
    };

    // Names Qt's own modules share with Omarchy's: with that Qt module imported, the name may
    // well be Qt's, so it isn't counted as missing.
    static readonly Dictionary<string, string> SharedWithQt = new(StringComparer.Ordinal)
    {
        ["Button"] = "QtQuick.Controls", ["TextField"] = "QtQuick.Controls",
    };

    public bool Provides(string module) => Modules.Contains(module);

    // Whether a type name used in a file is one the host can't give it, and from which module.
    public string? Missing(string name, ICollection<string> imports)
    {
        foreach (var module in imports)
            if (Types.TryGetValue(module, out var types) && types.Contains(name)) return null;
        if (SharedWithQt.TryGetValue(name, out var qt) && imports.Contains(qt)) return null;
        if (OmarchyTypes.TryGetValue(name, out var omarchy) && imports.Contains(omarchy)) return $"{omarchy} {name}";
        if (QuickshellTypes.TryGetValue(name, out var qs) && imports.Contains(qs)) return $"{qs} {name}";
        return null;
    }

    // The host's files: src/qs-host (qml/ and deploy/Qt.qml); Omarchy's shell folder when given.
    public static HostCapabilities Read(string hostDir, string? omarchyShell)
    {
        var host = new HostCapabilities();
        var qml = Path.Combine(hostDir, "qml");
        foreach (var qmldir in Directory.EnumerateFiles(qml, "qmldir", SearchOption.AllDirectories))
        {
            string? module = null;
            var types = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in File.ReadAllLines(qmldir))
            {
                var words = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (words.Length == 2 && words[0] == "module") module = words[1];
                else if (words.Length >= 3 && words[0] == "singleton") types.Add(words[1]);
                else if (words.Length >= 3 && char.IsUpper(words[0][0]) && words[^1].EndsWith(".qml")) types.Add(words[0]);
            }
            if (module == null) continue;
            host.Modules.Add(module);
            host.Types[module] = types;
        }
        // Qt's modules (Omawin.Native, the host's compiled part, is for its own QML: plugins
        // don't import it).
        foreach (var line in File.ReadAllLines(Path.Combine(hostDir, "deploy", "Qt.qml")))
            if (Import.Match(line) is { Success: true } m) host.Modules.Add(m.Groups[1].Value);
        var singleton = File.ReadAllText(Path.Combine(qml, "Quickshell", "Quickshell.qml"));
        foreach (Match m in Regex.Matches(singleton, @"\b(?:function|property\s+\w+)\s+(\w+)")) host.QuickshellMembers.Add(m.Groups[1].Value);
        if (omarchyShell != null)
            foreach (var (folder, module) in new[] { ("Commons", "qs.Commons"), ("Ui", "qs.Ui") })
                foreach (var file in Directory.EnumerateFiles(Path.Combine(omarchyShell, folder), "*.qml"))
                    host.OmarchyTypes[Path.GetFileNameWithoutExtension(file)] = module;
        return host;
    }

    internal static readonly Regex Import = new(@"^\s*import\s+([A-Za-z][\w.]*)(?:\s+\d+(?:\.\d+)?)?(?:\s+as\s+(\w+))?\s*;?\s*$", RegexOptions.CultureInvariant);
}

// One plugin's verdict, from its files: what its QML imports and uses, and what it runs.
public sealed record ScanResult(string Verdict, List<string> Needs, List<string> Commands);

public static class Scanner
{
    // Programs a plugin can run on Windows as they're named (Rust's Command finds name.exe on the
    // PATH): Windows' own, and omawin's. Anything else a plugin runs is a Linux program, for now
    // (roadmap 4.9.1's stand-ins and 4.9.4's runs-in-Arch come later).
    static readonly HashSet<string> WindowsPrograms = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd", "powershell", "pwsh", "curl", "git", "wsl", "explorer", "notepad", "tar", "ssh", "where", "code",
        "omawin", "omawin-music", "omawin-qs", "glazewm",
    };

    // A command's first word, where a plugin names one: a Process's command: [...], an
    // execDetached([...]), a bar.run("..."); and inside sh -c / bash -c, the script's first word.
    static readonly Regex CommandArray = new(@"(?:\bcommand\s*:|execDetached\s*\(|\bexec\s*\()\s*\[\s*(""[^""]*""|'[^']*')(?:\s*,\s*(""[^""]*""|'[^']*'))?(?:\s*,\s*(""[^""]*""|'[^']*'))?", RegexOptions.CultureInvariant);
    static readonly Regex BarRun = new(@"\bbar\.run\s*\(\s*(""[^""]*""|'[^']*')", RegexOptions.CultureInvariant);
    // Type names used: `Name {` (an object), and `Alias.Name` / `Name.member` (a qualified type or a singleton).
    static readonly Regex ObjectDecl = new(@"(?<![\w.])(?:(\w+)\.)?([A-Z]\w*)\s*\{", RegexOptions.CultureInvariant);
    static readonly Regex Member = new(@"(?<![\w.])(?:(\w+)\.)?([A-Z]\w*)\.([a-zA-Z_]\w*)", RegexOptions.CultureInvariant);

    // The verdict for one plugin. `files`: its files by path (relative, with /), QML, JavaScript
    // and shell scripts; anything else is ignored.
    public static ScanResult Scan(IReadOnlyDictionary<string, string> files, HostCapabilities host)
    {
        var needs = new SortedSet<string>(StringComparer.Ordinal);
        var commands = new SortedSet<string>(StringComparer.Ordinal);
        // Its own components: a file Name.qml anywhere in it is a type it brings.
        var local = new HashSet<string>(files.Keys.Where(f => f.EndsWith(".qml")).Select(f => Path.GetFileNameWithoutExtension(f)), StringComparer.Ordinal);

        foreach (var (path, raw) in files)
        {
            if (path.EndsWith(".sh")) { commands.Add("sh scripts"); continue; }
            if (!path.EndsWith(".qml") && !path.EndsWith(".js") && !path.EndsWith(".mjs")) continue;
            var text = StripComments(raw);
            var imports = new HashSet<string>(StringComparer.Ordinal);
            var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
            var body = new System.Text.StringBuilder(text.Length);
            foreach (var line in text.Split('\n'))
            {
                var m = HostCapabilities.Import.Match(line);
                // The rest of the file is what uses types (an import line's own dots aren't uses).
                if (!m.Success) { body.Append(line).Append('\n'); continue; }
                var module = m.Groups[1].Value;
                imports.Add(module);
                if (m.Groups[2].Success) aliases[m.Groups[2].Value] = module;
                if (!host.Provides(module)) needs.Add(module);
            }
            if (path.EndsWith(".qml"))
            {
                var code = StripStrings(body.ToString());
                foreach (Match m in ObjectDecl.Matches(code)) Check(m.Groups[1].Value, m.Groups[2].Value);
                foreach (Match m in Member.Matches(code))
                {
                    Check(m.Groups[1].Value, m.Groups[2].Value);
                    // The Quickshell singleton's members beyond the host's (Quickshell.screens, .shellDir, ...).
                    if (m.Groups[1].Value.Length == 0 && m.Groups[2].Value == "Quickshell" && imports.Contains("Quickshell") && !host.QuickshellMembers.Contains(m.Groups[3].Value))
                        needs.Add("Quickshell." + m.Groups[3].Value);
                }
            }
            foreach (Match m in CommandArray.Matches(text))
            {
                var first = Unquote(m.Groups[1].Value);
                var program = ProgramOf(first);
                // sh -c "<script>": the script's own first word is what runs.
                if (program is "sh" or "bash" && m.Groups[2].Success && Unquote(m.Groups[2].Value) == "-c" && m.Groups[3].Success)
                    program = ProgramOf(Unquote(m.Groups[3].Value).TrimStart().Split(' ', 2)[0]);
                Note(program);
            }
            foreach (Match m in BarRun.Matches(text)) Note(ProgramOf(Unquote(m.Groups[1].Value).TrimStart().Split(' ', 2)[0]));

            void Check(string qualifier, string name)
            {
                if (qualifier.Length > 0)
                {
                    // Alias.Name: a type of that import; anything else (Qt.AlignLeft, a property's
                    // object) isn't a type reference.
                    if (!aliases.TryGetValue(qualifier, out var module)) return;
                    if (host.Missing(name, [module]) is { } missing) needs.Add(missing);
                    return;
                }
                if (local.Contains(name)) return;
                if (host.Missing(name, imports) is { } gap) needs.Add(gap);
            }
        }
        foreach (var unknown in commands.ToList())
            if (WindowsPrograms.Contains(unknown)) commands.Remove(unknown);
        var verdict = needs.Count > 0 ? Verdicts.NotYet : commands.Count > 0 ? Verdicts.Partly : Verdicts.Runs;
        return new ScanResult(verdict, [.. needs], [.. commands]);

        void Note(string? program) { if (!string.IsNullOrEmpty(program)) commands.Add(program); }
    }

    // A command's program, as a name: /usr/bin/notify-send is notify-send; a path into the plugin
    // or a variable isn't a name it can know.
    static string? ProgramOf(string word)
    {
        word = word.Trim();
        if (word.Length == 0 || word.Contains('$') || word.Contains('{')) return null;
        var name = word.Split('/').Last();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        if (name.EndsWith(".sh")) return "sh scripts";
        return Regex.IsMatch(name, @"^[A-Za-z0-9][A-Za-z0-9._+-]*$") ? name : null;
    }

    static string Unquote(string s) => s.Length >= 2 ? s[1..^1] : s;

    // Comments out (// and /* */), strings kept: commands live in strings.
    static string StripComments(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c is '"' or '\'' or '`')
            {
                int end = SkipString(text, i);
                sb.Append(text, i, end - i);
                i = end - 1;
            }
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n') i++;
                if (i < text.Length) sb.Append('\n');
            }
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? text.Length : end + 1;
                sb.Append(' ');
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    // Strings emptied (their quotes kept): words in text aren't types.
    static string StripStrings(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c is '"' or '\'' or '`')
            {
                int end = SkipString(text, i);
                sb.Append(c).Append(c);
                i = end - 1;
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    // Where the string starting at i ends (just past its closing quote).
    static int SkipString(string text, int i)
    {
        char quote = text[i];
        for (int j = i + 1; j < text.Length; j++)
        {
            if (text[j] == '\\') { j++; continue; }
            if (text[j] == quote) return j + 1;
            if (text[j] == '\n' && quote != '`') return j + 1;
        }
        return text.Length;
    }
}
