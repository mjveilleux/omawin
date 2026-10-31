namespace SystemMenu.Search;

// `SystemMenu --search …`: what the keys and `omawin launcher | files | menu | search` ask for
// (bin/commands.txt). Pure; App.HandleRequest acts on it.
//
//   --search                         One Search, on All (Win+Space)
//   --search <filter> [text…]        on a filter: files (Win+F), clipboard (Win+Shift+V), settings, …
//   --search <page> [arg]            a view that isn't a filter: audio, quick, packages, localhost,
//                                    reports, keys, vpn <connection>; about is its own window
//   --search command --browse [id]   Commands, browsing the menu's sections (omawin menu [id])
//   --retired-key                    asked by Win+Alt+Space, retired: the note says Win+Space does this now
public sealed record SearchRequest(Tag? Filter, string? Page, string PageArg, bool Browse, string? Section, bool RetiredKey, string Text)
{
    // The launcher's pages that aren't filters: views of the same window, opened as they were.
    public static readonly string[] Pages = ["audio", "quick", "packages", "localhost", "reports", "keys", "vpn", "about"];

    public static SearchRequest Parse(IReadOnlyList<string> args)
    {
        var rest = args.SkipWhile(a => a != "--search").Skip(1).ToList();
        bool browse = rest.Remove("--browse");
        bool retired = rest.Remove("--retired-key");
        Tag? filter = null;
        string? page = null, section = null;
        string pageArg = "";
        if (rest.Count > 0)
        {
            var first = rest[0].TrimStart('@').ToLowerInvariant();
            if (Pages.Contains(first))
            {
                page = first;
                pageArg = string.Join(' ', rest.Skip(1));
                rest.Clear();
            }
            else if (FilterNamed(first) is Tag t)
            {
                filter = t;
                rest.RemoveAt(0);
            }
            else if (first == "all") rest.RemoveAt(0); // no filter, said out loud
        }
        if (browse)
        {
            filter = Tag.Command;
            if (rest.Count > 0) { section = rest[0]; rest.RemoveAt(0); }
        }
        return new(filter, page, pageArg, browse, section, retired, string.Join(' ', rest));
    }

    // A filter by its whole name or its singular ("files", "file", "Clipboard"), never a prefix:
    // a request's first word may be the start of what to search for.
    public static Tag? FilterNamed(string word)
    {
        foreach (var (tag, name) in QueryParser.Filters)
        {
            if (word.Equals(name, StringComparison.OrdinalIgnoreCase)) return tag;
            if (name.EndsWith('s') && word.Equals(name[..^1], StringComparison.OrdinalIgnoreCase)) return tag;
        }
        return null;
    }
}
