using System.Text.RegularExpressions;

namespace Omawin.Bundle;

// One row of install/tools.txt.
public sealed record Tool(string Name, string Source, Regex Asset, string? Rename, string Bin, string? Provides, string? Home = null, bool Later = false, string? Keep = null, bool Sfx = false, int Parts = 0);

// One row of the manifest the install reads (omawin-tools.txt), tab-separated so Windows
// PowerShell splits it with one -split: name, version, file, sha256, bin, winget id, home, flags
// (later, sfx: space-separated), and for a tool split into parts, the parts' files and their
// sha256s (comma-separated): unpacked side by side, one tar each, when the PC's tar reads zstd;
// File is the fallback otherwise (and what an install from before parts takes).
public sealed record Built(string Name, string Version, string File, string Sha256, string Bin, string? Provides, string? Home, bool Later = false, bool Sfx = false,
    IReadOnlyList<(string File, string Sha256)>? Parts = null)
{
    public string Line => string.Join('\t', new[] { Name, Version, File, Sha256, Bin, Provides ?? "", Home ?? "",
        string.Join(' ', new[] { Later ? "later" : null, Sfx ? "sfx" : null }.Where(f => f != null)) }
        .Concat(Parts is { Count: > 0 } p ? [string.Join(',', p.Select(x => x.File)), string.Join(',', p.Select(x => x.Sha256))] : []));
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
            if (p.Length < 4) throw new FormatException($"install/tools.txt: needs name | source | asset | bin: {line}");
            var asset = p[2];
            string? rename = null;
            var arrow = asset.IndexOf("=>", StringComparison.Ordinal);
            if (arrow >= 0) { rename = asset[(arrow + 2)..].Trim(); asset = asset[..arrow].Trim(); }
            if (!Regex.IsMatch(p[0], "^[a-z0-9-]+$")) throw new FormatException($"install/tools.txt: name '{p[0]}'");
            if (p[3].Contains("..") || Path.IsPathRooted(p[3])) throw new FormatException($"install/tools.txt: bin '{p[3]}'");
            string? home = p.Length > 5 && p[5].Length > 0 ? p[5] : null;
            if (home != null && (home.Contains("..") || Path.IsPathRooted(home) || home.StartsWith('\\'))) throw new FormatException($"install/tools.txt: home '{home}'");
            // The 7th field: flags. later (not waited for), keep:<pattern> (only those files of it),
            // sfx (a 7-Zip self-extractor, shipped as it is: the PC runs it to unpack), parts:<n>
            // (also as n tar.zst parts, unpacked at once by n tars: a tool of many files).
            bool later = false, sfx = false; string? keep = null; int parts = 0;
            foreach (var flag in (p.Length > 6 ? p[6] : "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (flag == "later") later = true;
                else if (flag == "sfx") sfx = true;
                else if (flag.StartsWith("parts:") && int.TryParse(flag[6..], out var n) && n is >= 2 and <= 16) parts = n;
                else if (flag.StartsWith("keep:") && flag.Length > 5) keep = flag[5..];
                else throw new FormatException($"install/tools.txt: flag '{flag}' (later, sfx, keep:<pattern>, parts:<2-16>)");
            }
            if (sfx && keep != null) throw new FormatException($"install/tools.txt: {p[0]}: sfx ships the file as it is, so keep: can't apply");
            tools.Add(new Tool(p[0], p[1], new Regex(asset), rename, p[3], p.Length > 4 && p[4].Length > 0 ? p[4] : null, home, later, keep, sfx, parts));
        }
        if (tools.GroupBy(t => t.Name).FirstOrDefault(g => g.Count() > 1) is { } dup) throw new FormatException($"install/tools.txt: {dup.Key} twice");
        return tools;
    }

    // The release file names. No version in them, so the install can take
    // releases/latest/download/<name> straight, without asking GitHub's API which release is newest.
    public const string Manifest = "omawin-tools.txt";
    public static string FileName(string name, bool sfx = false) => $"omawin-tool-{name}{(sfx ? ".exe" : ".zip")}";
    public static string PartName(string name, int part) => $"omawin-tool-{name}.{part}.tar.zst";

    // A tool's files dealt into n parts of about as many files each (writing files, not
    // decompressing, is what takes the time on Windows: each part's tar writes its own share at
    // once). Sorted by path first, so the parts are the same build to build; every folder goes in
    // the first part, so empty ones exist too.
    public static List<List<string>> Split(IEnumerable<string> files, int n)
    {
        var parts = Enumerable.Range(0, n).Select(_ => new List<string>()).ToList();
        int i = 0;
        foreach (var f in files.Order(StringComparer.Ordinal)) parts[i++ % n].Add(f);
        return parts;
    }

    // An archive whose entries all sit in one top folder ("nvim-win64/bin/nvim.exe") is unpacked
    // without it, so every tool's zip has the same shape: the tool's own files at the top.
    // keep:<pattern>[,<pattern>...]: a file kept when its name matches one (* and ?), wherever it
    // is in the archive.
    public static bool Keeps(Tool t, string entry)
    {
        if (t.Keep == null) return true;
        var name = Path.GetFileName(entry.Replace('\\', '/'));
        return t.Keep.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Any(p => System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(p, name, ignoreCase: true));
    }

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
