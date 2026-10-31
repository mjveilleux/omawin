// Every setting Windows has, as rows that open its page (roadmap 1B.1, plans/02-settings-and-one-search.md §10):
// the catalog's entries switch in place; this is the long tail. Two sources:
//   - Windows' own search data, %WINDIR%\ImmersiveControlPanel\Settings\AllSystemSettings_*.xml:
//     one SearchableContent element per setting with its name, keywords and page, the names as
//     resource strings ("@shell32.dll,-21817") in the PC's language. Undocumented, so the reader
//     collects text by element name anywhere inside each SearchableContent and survives layout
//     changes; WinSettings caches what it read in settings-index.tsv.
//   - default/launcher/settings-pages.txt (and its layers): the documented ms-settings: pages,
//     the admin consoles and the classic dialogs, always merged in, and all there is when the
//     XML can't be read.
// This file is the pure part (parsing, the cache's format); resolving resource strings and
// finding the file are WinSettings's. Tested in menu-app.Tests.

using System.Xml;

namespace SystemMenu;

public sealed record IndexedSetting(string Name, string Page, string Keywords, string Uri);

public static class SettingsIndex
{
    // "uri | name | page | keywords"; a uri with spaces is a program and its arguments.
    public static List<IndexedSetting> ParsePages(IEnumerable<string> lines) => lines
        .Select(l => l.Trim()).Where(l => l.Length > 0 && l[0] != '#')
        .Select(l => l.Split('|').Select(p => p.Trim()).ToArray()).Where(p => p.Length == 4 && p[0].Length > 0 && p[1].Length > 0)
        .Select(p => new IndexedSetting(p[1], p[2], p[3], p[0])).ToList();

    // Windows' settings XML. resolve turns "@dll,-id" into text (null: couldn't); pageOf names a
    // page id's ms-settings: link and its label, for entries without a deep link of their own.
    public static List<IndexedSetting> ReadXml(TextReader text, Func<string?, string?> resolve, Func<string, (string Uri, string Label)?> pageOf)
    {
        var rows = new List<IndexedSetting>();
        var seen = new HashSet<(string, string)>();
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, IgnoreComments = true, XmlResolver = null };
        using var reader = XmlReader.Create(text, settings);
        Dictionary<string, string>? item = null;
        string? field = null;
        while (reader.Read())
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Element when reader.LocalName == "SearchableContent":
                    item = new(StringComparer.Ordinal);
                    field = null;
                    break;
                case XmlNodeType.Element when item != null:
                    field = reader.LocalName;
                    break;
                case XmlNodeType.EndElement when reader.LocalName == "SearchableContent" && item != null:
                    if (Row(item, resolve, pageOf) is { } row && seen.Add((row.Name.ToLowerInvariant(), row.Uri))) rows.Add(row);
                    item = null;
                    field = null;
                    break;
                case XmlNodeType.EndElement:
                    field = null;
                    break;
                case XmlNodeType.Text or XmlNodeType.CDATA when item != null && field != null:
                    item.TryAdd(field, reader.Value.Trim()); // the first of each name wins
                    break;
            }
        }
        return rows;
    }

    static IndexedSetting? Row(Dictionary<string, string> item, Func<string?, string?> resolve, Func<string, (string Uri, string Label)?> pageOf)
    {
        var name = Clean(resolve(item.GetValueOrDefault("Name")));
        if (name.Length == 0) return null;
        var pageId = item.GetValueOrDefault("PageID") ?? "";
        var page = pageOf(pageId);
        var link = item.GetValueOrDefault("DeepLink") ?? "";
        // Only links to Settings itself are followed: the file is Windows', but a row runs what it says.
        var uri = link.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase) ? link : page?.Uri ?? "ms-settings:";
        var words = string.Join(' ', new[] { "HighKeywords", "Keywords", "Description" }
            .Select(k => Clean(resolve(item.GetValueOrDefault(k)))).Where(s => s.Length > 0));
        return new(name, page?.Label ?? "Settings", words, uri);
    }

    public static string Clean(string? s) => s == null ? "" : string.Join(' ', s.Split(['\t', '\r', '\n', ' ', ';'], StringSplitOptions.RemoveEmptyEntries));

    // --- the cache: settings-index.tsv ---------------------------------------------------------
    // First line "#\t<xml path>\t<its write time, ticks>\t<UI language>": rebuilt when any of
    // them changes (a Windows update, another display language). Then name\tpage\tkeywords\turi.
    public static string Stamp(string xmlPath, long ticks, string language) => $"#\t{xmlPath}\t{ticks}\t{language}";

    public static IEnumerable<string> CacheLines(string stamp, IEnumerable<IndexedSetting> rows) =>
        rows.Select(r => string.Join('\t', Clean(r.Name), Clean(r.Page), Clean(r.Keywords), Clean(r.Uri))).Prepend(stamp);

    public static List<IndexedSetting> ParseCache(IEnumerable<string> lines) => lines
        .Where(l => !l.StartsWith('#')).Select(l => l.Split('\t')).Where(p => p.Length == 4)
        .Select(p => new IndexedSetting(p[0], p[1], p[2], p[3])).ToList();

    // Below this many rows, Windows' file wasn't what the reader expects: the pages list stands in.
    public const int TooFew = 50;
}
