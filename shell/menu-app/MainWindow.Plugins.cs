using Microsoft.UI.Xaml;
using SystemMenu.Plugins;

namespace SystemMenu;

// The plugin gallery (Install › Plugins › Gallery, `omawin plugin gallery`; roadmap 4.10): every
// plugin omawin can show, one row each with its author, stars and what it does, whether it's on,
// and whether it runs in omawin's QML host (Plugins/Gallery.cs has the rows; the index comes from
// CI, src/plugin-gallery). Enter turns one on (`omawin plugin add`, in a task window: it may
// download the plugin and the host) or off (`omawin plugin remove`, hidden). A plugin from the
// marketplace asks first: its code comes from someone else's repository.
public sealed partial class MainWindow
{
    bool galleryOpen;                          // showing the gallery
    GalleryRow? galleryAsking;                 // asking "turn this on?" for this one
    List<GalleryRow> galleryRows = [];
    int galleryIndexed;                        // how many the gallery's index lists (0: no index yet)
    // What Enter just did, until plugins.txt says so (or a while has passed: the task failed).
    readonly Dictionary<string, (bool On, DateTime At)> galleryTurned = [];

    MenuEntry? GalleryMenuRow => entries.Values.FirstOrDefault(e => e.Kind == "plugins");

    void OpenGallery()
    {
        galleryOpen = true;
        galleryAsking = null;
        selected = 0;
        LoadGallery();
    }

    // Read once when it opens (and after Enter), not on every key: the index is a few hundred rows.
    void LoadGallery()
    {
        GalleryIndex? index = null;
        try
        {
            if (File.Exists(PluginSlot.GalleryFile)) index = GalleryIndex.Parse(File.ReadAllText(PluginSlot.GalleryFile), out _);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { App.Log(ex); }
        var on = new HashSet<string>(PluginSlot.OnIds());
        // What Enter did shows at once; once plugins.txt agrees (the task finished), it's forgotten.
        foreach (var (id, (state, at)) in galleryTurned.ToList())
        {
            if (on.Contains(id) == state || DateTime.Now - at > TimeSpan.FromMinutes(5)) galleryTurned.Remove(id);
            else if (state) on.Add(id);
            else on.Remove(id);
        }
        galleryRows = Gallery.Rows(PluginFolders.Manifests(PluginSlot.ShippedDir), PluginFolders.Manifests(PluginSlot.DownloadedDir), index, on);
        galleryIndexed = index?.Plugins.Count ?? 0;
    }

    void RenderGallery(string query)
    {
        var at = GalleryMenuRow;
        if (galleryAsking is { } ask)
        {
            SetCrumbs(at?.Parent ?? "", ask.Name);
            Placeholder.Text = $"Turn {ask.Name} on?";
            AddRow(char.ConvertFromUtf32(0xF0431), $"Yes, download {ask.Name} and turn it on", Hex(theme.Green), () => TurnOn(ask));
            Below(ask.Repo.Length > 0 ? $"From {ask.Repo}, at the commit Omarchy's maintainers reviewed. It runs as you, unsandboxed." : "It runs as you, unsandboxed.");
            AddRow(char.ConvertFromUtf32(0xF0156), "No, go back", Hex(theme.Comment), () => { galleryAsking = null; Render(); });
            FooterLeft.Text = ask.Author.Length > 0 ? $"by {ask.Author}" : ask.Name;
            SetKeys(("↵", "choose"), ("esc", "back"));
            return;
        }
        SetCrumbs(at?.Parent ?? "", at?.Label ?? "Gallery");
        Placeholder.Text = "Search plugins…";
        var list = Gallery.Search(galleryRows, query);
        foreach (var row in list.Take(200))
        {
            var color = Hex(row.On ? theme.Green : row.Verdict == Verdicts.Runs ? theme.Accent : row.Verdict == Verdicts.Partly ? theme.Yellow : theme.Comment);
            var hits = new List<int>();
            Fuzzy.Match(row.Name, query, hits);
            var meta = string.Join(" · ", new[] { row.Author, row.Stars > 0 ? $"★ {row.Stars}" : "" }.Where(s => s.Length > 0));
            var (tag, text) = row.On ? (Tag.Installed, "On") : row.CanTurnOn ? (Tag.Text, "off") : (Tag.Text, "not yet");
            var picked = row;
            AddRow(char.ConvertFromUtf32(0xF0431), row.Name, color, () => GalleryEnter(picked), tag, text,
                dim: !row.On && !row.CanTurnOn, sub: meta.Length > 0 ? meta : null, highlight: query.Length > 0 ? hits : null);
            // What it does, and for one that doesn't fully run, why.
            Below(row.Verdict == Verdicts.Runs ? (row.Description.Length > 0 ? row.Description : row.Why)
                : row.Description.Length > 0 ? $"{row.Why} · {row.Description}" : row.Why);
        }
        if (list.Count == 0)
            AddRow(char.ConvertFromUtf32(0xF0156), galleryRows.Count == 0 ? "No plugins yet" : "No matches", Hex(theme.Comment), () => { }, dim: true);
        int onCount = galleryRows.Count(r => r.On);
        FooterLeft.Text = galleryIndexed > 0 ? $"{galleryRows.Count} plugins · {onCount} on" : $"{onCount} on · the gallery comes with omawin's next update";
        SetKeys(("↑↓", "move"), ("↵", "turn on / off"), ("esc", query.Length > 0 ? "clear" : "back"));
    }

    // The row just added gets a second line (taller, like the file finder's rows).
    void Below(string text)
    {
        var v = pool[used - 1];
        v.Row.Height = FileRowDip - 2;
        v.Below.Text = text;
        v.Below.Foreground = Brush(theme.Comment);
        v.Below.Visibility = Visibility.Visible;
    }

    void GalleryEnter(GalleryRow row)
    {
        switch (Gallery.Verb(row))
        {
            case "remove":
                // Quick and quiet: plugins.txt loses a line, and the bar drops it.
                StartCommand($"omawin.exe plugin remove {row.Id}");
                galleryTurned[row.Id] = (false, DateTime.Now);
                LoadGallery();
                // Still on the same plugin, wherever it moved to.
                selected = Math.Max(0, Gallery.Search(galleryRows, Input.Text.Trim()).FindIndex(r => r.Id == row.Id));
                Render();
                break;
            case "add" when !row.Shipped && !row.Downloaded:
                galleryAsking = row;
                selected = 0;
                Render();
                break;
            case "add":
                TurnOn(row);
                break;
            // Not runnable yet: nothing to do; the row says why.
        }
    }

    // `omawin plugin add <id>` in a task window you can watch: it downloads a marketplace plugin,
    // installs the plugin's programs and the QML host when they aren't here yet.
    void TurnOn(GalleryRow row)
    {
        if (!GalleryIndex.IsSafeId(row.Id)) return;
        galleryTurned[row.Id] = (true, DateTime.Now);
        galleryAsking = null;
        RunPowerShell($"-Plugin \"add {row.Id}\"");
        HideMenu();
        LoadGallery();
    }

    // Esc in the gallery: out of the question, else out of the gallery to its row.
    bool LeaveGallery()
    {
        if (!galleryOpen) return false;
        if (galleryAsking != null) { galleryAsking = null; Render(); return true; }
        galleryOpen = false;
        var from = GalleryMenuRow;
        current = from?.Parent ?? "";
        selected = Math.Max(0, MenuModel.Children(entries, current).FindIndex(c => c.Id == from?.Id));
        selectKey = from?.Id;
        Render();
        Slide(-1);
        return true;
    }
}
