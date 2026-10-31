using System.Text;

namespace Omawin.Cli.Release;

// A release's notes (plans/09-for-everyone.md §7), made in CI and shown by `omawin update`
// before it applies: the commits' "Area: what changed" subjects grouped by area, then each new
// migration's summary under "What changes on your PC".
public static class Notes
{
    // "Plugins: manifest and protocol" is area Plugins; a subject without a short (up to three
    // words) prefix before a colon goes under Other.
    static (string Area, string Text) Split(string subject)
    {
        var colon = subject.IndexOf(':');
        if (colon > 0 && colon <= 30 && subject[..colon].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 3 && colon + 1 < subject.Length)
            return (subject[..colon].Trim(), subject[(colon + 1)..].Trim());
        return ("Other", subject.Trim());
    }

    public static string Make(IEnumerable<string> subjects, IEnumerable<(string File, string Summary)> migrations)
    {
        var sb = new StringBuilder();
        var groups = subjects
            .Where(s => !string.IsNullOrWhiteSpace(s) && !s.StartsWith("Merge ", StringComparison.Ordinal))
            .Select(Split)
            .GroupBy(x => x.Area, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key == "Other").ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var g in groups)
        {
            sb.Append("### ").Append(g.First().Area).Append('\n');
            foreach (var x in g.DistinctBy(x => x.Text)) sb.Append("- ").Append(char.ToUpperInvariant(x.Text[0])).Append(x.Text[1..]).Append('\n');
            sb.Append('\n');
        }
        var m = migrations.ToList();
        if (m.Count > 0)
        {
            sb.Append("### What changes on your PC\n");
            foreach (var (file, summary) in m) sb.Append("- ").Append(summary).Append(" (`").Append(file).Append("`)\n");
        }
        return sb.ToString().TrimEnd() + "\n";
    }
}
