namespace SystemMenu.Search;

// One Search's home, the bento shown while nothing is typed (plans/one-search.md, guardrails 1
// and 2): tiles you learn by position. Places never move; only what's in them changes (the latest
// project, the last copy, the switches' states, your most used apps). Pure: which tile goes
// where; the window (MainWindow.Search.cs) fills and draws them.
//
//   Terminal (big)            | Projects (wide)
//                             | Clipboard (wide)
//   your next four items, one row
//   Browse (wide)             | Theme (wide: its palette as one gradient)
//   QUICK     Wi-Fi · Bluetooth · Dark mode · Microphone
//   FREQUENT  the four apps you open most

public enum HomeStyle { Hero, Wide, Tile, Switch }

// Key: a launcher item's key, a setting's id ("setting:look.dark-apps"), or "browse" / "theme".
public sealed record HomeSlot(string Key, HomeStyle Style, int ColSpan = 1);

// Top: the hero and its two wides; else tiles flowing four to a row under an optional title.
public sealed record HomeRow(string? Title, IReadOnlyList<HomeSlot> Slots, bool Top = false);

// One of your launcher items (items.txt), or an app: its key, and its page when it's built in.
public sealed record HomeItem(string Key, string? BuiltIn, bool IsApp);

public static class HomePlan
{
    public const string Browse = "browse", Theme = "theme", SettingPrefix = "setting:";

    // The quick switches, in their places: settings from the catalog (default/launcher/settings.txt).
    public static readonly string[] Switches = ["connections.wifi", "connections.bluetooth", "look.dark-apps", "privacy.microphone"];

    // items: your items then the apps, in the launcher's order; byUse: keys, most used first
    // (the launcher's history); switchesHere: the switches this PC's catalog has (a desktop
    // without Wi-Fi has no Wi-Fi switch; that's the PC, not the moment, so places still hold).
    public static List<HomeRow> Arrange(IReadOnlyList<HomeItem> items, IEnumerable<string> byUse, ISet<string> switchesHere)
    {
        var rows = new List<HomeRow>();
        var own = items.Where(i => !i.IsApp).ToList();
        var placed = new HashSet<string>();

        // Your first plain item (the terminal) big; Projects and Clipboard wide beside it.
        if (own.FirstOrDefault(i => i.BuiltIn is null) is { } first)
        {
            var top = new List<HomeSlot> { new(first.Key, HomeStyle.Hero, 2) };
            placed.Add(first.Key);
            foreach (var page in new[] { "projects", "clipboard" })
                if (own.FirstOrDefault(i => i.BuiltIn == page) is { } item)
                {
                    top.Add(new(item.Key, HomeStyle.Wide, 2));
                    placed.Add(item.Key);
                }
            rows.Add(new(null, top, Top: true));
        }

        // Your next four items. The menu item is the Browse tile below, so it's not one of them.
        var next = own.Where(i => !placed.Contains(i.Key) && i.BuiltIn != "menu").Take(4).ToList();
        foreach (var i in next) placed.Add(i.Key);
        if (next.Count > 0) rows.Add(new(null, [.. next.Select(i => new HomeSlot(i.Key, HomeStyle.Tile))]));

        rows.Add(new(null, [new(Browse, HomeStyle.Wide, 2), new(Theme, HomeStyle.Wide, 2)]));

        var switches = Switches.Where(switchesHere.Contains).Select(id => new HomeSlot(SettingPrefix + id, HomeStyle.Switch)).ToList();
        if (switches.Count > 0) rows.Add(new("Quick", switches));

        // What you open most; before there's any history, the first apps in order.
        var known = new HashSet<string>(items.Select(i => i.Key));
        var menu = own.FirstOrDefault(i => i.BuiltIn == "menu")?.Key;
        var frequent = byUse.Where(k => known.Contains(k) && !placed.Contains(k) && k != menu).Distinct()
            .Concat(items.Where(i => i.IsApp && !placed.Contains(i.Key)).Select(i => i.Key))
            .Distinct().Take(4).ToList();
        if (frequent.Count > 0) rows.Add(new("Frequent", [.. frequent.Select(k => new HomeSlot(k, HomeStyle.Tile))]));
        return rows;
    }
}
