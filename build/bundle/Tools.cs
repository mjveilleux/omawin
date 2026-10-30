using System.Text.RegularExpressions;

namespace Omawin.Bundle;

// One row of bundle/tools.txt.
public sealed record Tool(string Name, string Source, Regex Asset, string? Rename, string Bin, string? Provides, string? Home = null, bool Later = false);

// One row of the manifest the install reads (omawin-tools.txt), tab-separated so Windows
// PowerShell splits it with one -split: name, version, file, sha256, bin, winget id, home, later.
public sealed record Built(string Name, string Version, string File, string Sha256, string Bin, string? Provides, string? Home, bool Later = false)
{
    public string Line => string.Join('\t', Name, Version, File, Sha256, Bin, Provides ?? "", Home ?? "", Later ? "later" : "");
}

public static class Tools
{
    public static List<Tool> Parse(IEnumerable<string> lines)
    {
        var tools = new List<Tool>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var p = line.Split('|').Select(x => x.Trim()).ToArray();
            if (p.Length < 4) throw new FormatException($"bundle/tools.txt: needs name | source | asset | bin: {line}");
            var asset = p[2];
            string? rename = null;
            var arrow = asset.IndexOf("=>", StringComparison.Ordinal);
            if (arrow >= 0) { rename = asset[(arrow + 2)..].Trim(); asset = asset[..arrow].Trim(); }
            if (!Regex.IsMatch(p[0], "^[a-z0-9-]+$")) throw new FormatException($"bundle/tools.txt: name '{p[0]}'");
            if (p[3].Contains("..") || Path.IsPathRooted(p[3])) throw new FormatException($"bundle/tools.txt: bin '{p[3]}'");
            string? home = p.Length > 5 && p[5].Length > 0 ? p[5] : null;
            if (home != null && (home.Contains("..") || Path.IsPathRooted(home) || home.StartsWith('\\'))) throw new FormatException($"bundle/tools.txt: home '{home}'");
            var later = p.Length > 6 && p[6] == "later";
            if (p.Length > 6 && p[6].Length > 0 && !later) throw new FormatException($"bundle/tools.txt: '{p[6]}' (the 7th field is later, or nothing)");
            tools.Add(new Tool(p[0], p[1], new Regex(asset), rename, p[3], p.Length > 4 && p[4].Length > 0 ? p[4] : null, home, later));
        }
        if (tools.GroupBy(t => t.Name).FirstOrDefault(g => g.Count() > 1) is { } dup) throw new FormatException($"bundle/tools.txt: {dup.Key} twice");
        return tools;
    }

    // The release file names. No version in them, so the install can take
    // releases/latest/download/<name> straight, without asking GitHub's API which release is newest.
    public const string Manifest = "omawin-tools.txt";
    public static string FileName(string name) => $"omawin-tool-{name}.zip";

    // An archive whose entries all sit in one top folder ("nvim-win64/bin/nvim.exe") is unpacked
    // without it, so every tool's zip has the same shape: the tool's own files at the top.
    public static string? CommonTop(IEnumerable<string> entries)
    {
        string? top = null;
        foreach (var e in entries.Select(e => e.Replace('\\', '/')).Where(e => e.Length > 0))
        {
            var slash = e.IndexOf('/');
            if (slash < 0) return null;          // a file at the top: nothing to strip
            var first = e[..slash];
            if (top == null) top = first;
            else if (top != first) return null;
        }
        return top;
    }
}
