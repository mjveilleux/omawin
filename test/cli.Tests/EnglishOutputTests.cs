using System.Text.RegularExpressions;

namespace Omawin.Cli.Tests;

// Windows translates these tools' output, so reading it breaks on a German or Japanese PC
// (plans/09-for-everyone.md §5). Use the API, JSON or machine output instead; a line that
// truly must parse it says why with "any-language:" in a comment.
public partial class EnglishOutputTests
{
    [GeneratedRegex(@"\b(powercfg|netsh|ipconfig|systeminfo|w32tm|route\s+print|sc(\.exe)?\s+query|tasklist|schtasks)\b[^#\n]*(Select-String|-match|-like|-split|\.Contains\(|\.StartsWith\(|findstr|Regex)", RegexOptions.IgnoreCase)]
    private static partial Regex ParsesOutput();

    static string FindRepo()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "bin", "commands.txt"))) return d.FullName;
        throw new InvalidOperationException("repo not found");
    }

    // Docs, tests and build output aren't code that runs on the PC.
    static bool Skipped(string relative)
    {
        var parts = relative.Split('/', '\\');
        if (parts.Any(p => p is "docs" or "obj" or ".git" || p.EndsWith(".Tests"))) return true;
        for (int i = 0; i + 1 < parts.Length; i++)
            if (parts[i] == "bin" && parts[i + 1] is "Debug" or "Release") return true;
        return false;
    }

    [Fact]
    public void NoCodeParsesTranslatedOutput()
    {
        var root = FindRepo();
        var hits = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".ps1") || f.EndsWith(".cs"))
            .Select(f => (Full: f, Rel: Path.GetRelativePath(root, f)))
            .Where(f => !Skipped(f.Rel))
            .SelectMany(f => File.ReadLines(f.Full).Select((line, i) => (f.Rel, Line: i + 1, Text: line)))
            .Where(x => !x.Text.TrimStart().StartsWith('#') && !x.Text.TrimStart().StartsWith("//"))
            .Where(x => ParsesOutput().IsMatch(x.Text) && !x.Text.Contains("any-language:"))
            .Select(x => $"{x.Rel}:{x.Line}: {x.Text.Trim()}");
        Assert.Empty(hits);
    }
}
