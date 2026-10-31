using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace SystemMenu;

// Install › Remove › Packages (`@packages remove`, roadmap 2.10): the Packages page turned round.
// Everything installed, by any manager, each with where it came from (`omawin pkg list --tsv`,
// InstalledPackages.cs); Tab steps through the sources, typing finds, Ctrl+Space picks several,
// Enter twice removes through the manager that put it in, in a terminal you watch. omawin's own
// tools and programs with their own uninstaller are listed too, saying why Enter won't.
public sealed partial class MainWindow
{
    List<InstalledPackage>? removeList;      // null while omawin pkg list answers
    Dictionary<string, InstalledPackage> removeByKey = [];
    string removeFilter = "all";
    string? removeArmed;                      // the row (or "picked") Enter has asked about once
    string pkgMode = "";                      // install | remove: picks never carry from one to the other
    int removeToken;

    // The page's mode changed (or it was opened again): picks dropped, the list read afresh.
    void EnterPackageMode(string mode)
    {
        if (mode == pkgMode) return;
        pkgMode = mode;
        pkgPicked.Clear();
        removeArmed = null;
        if (mode == "remove") LoadRemovals();
    }

    void LoadRemovals()
    {
        removeList = null;
        int token = ++removeToken;
        Task.Run(() =>
        {
            var lines = new List<string>();
            try
            {
                var info = new ProcessStartInfo("omawin.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, StandardOutputEncoding = System.Text.Encoding.UTF8 };
                foreach (var a in new[] { "pkg", "list", "--tsv" }) info.ArgumentList.Add(a);
                using var proc = Process.Start(info)!;
                string? line;
                while ((line = proc.StandardOutput.ReadLine()) != null) lines.Add(line);
                proc.WaitForExit();
            }
            catch (Exception ex) { App.Log(ex); }
            return InstalledPackages.Parse(lines);
        }).ContinueWith(t => DispatcherQueue.TryEnqueue(() =>
        {
            if (token != removeToken) return;
            removeList = t.IsCompletedSuccessfully ? t.Result : [];
            removeByKey = removeList.GroupBy(p => ToPackage(p).Key).ToDictionary(g => g.Key, g => g.First());
            if (launching && page == "packages" && pkgMode == "remove") Render();
        }));
    }

    static Package ToPackage(InstalledPackage p) => new(p.Source, p.Id, p.Name, p.Version, InstalledPackages.Says(p));

    void RenderRemovals(string query)
    {
        SetCrumbs("", "Remove");
        Placeholder.Text = $"Search what's installed{(removeFilter == "all" ? "" : " from " + InstalledPackages.Label(removeFilter))}…";
        RemoveChips();
        pkgShown = [];
        pkgInstalled = [.. (removeList ?? []).Select(p => ToPackage(p).Key)];
        if (removeList == null)
        {
            // The first time on a PC, omawin pkg list asks every manager (winget's export takes a while).
            AddRow(Glyph(0xF0996), "Reading what's installed, from every package manager…", Hex(theme.Comment), () => { }, dim: true);
            FooterLeft.Text = "omawin pkg list";
            SetKeys(("esc", "back"));
            return;
        }
        var here = removeList.Where(p => InstalledPackages.InFilter(p, removeFilter)).Select(ToPackage).ToList();
        var matches = MatchPackages(here, query, keepOrder: false);
        foreach (var (p, hits) in matches.Take(PackageRows)) AddRemovalRow(p, hits);
        if (pkgShown.Count == 0)
            AddRow(Glyph(0xF0156), removeList.Count == 0 ? "Nothing recorded: omawin pkg list --refresh asks each manager" : "No matches", Hex(theme.Comment), () => { }, dim: true);
        string count = query.Length == 0 ? $"{here.Count} installed" : $"{matches.Count} of {here.Count}";
        FooterLeft.Text = removeArmed == "picked" ? $"Enter again removes the {pkgPicked.Count} picked"
            : pkgPicked.Count > 0 ? $"{count} · {pkgPicked.Count} picked" : $"{count} · {InstalledPackages.Label(removeFilter)}";
        SetKeys(("tab", "source"), ("ctrl space", "pick"), ("↵", pkgPicked.Count > 1 ? $"remove {pkgPicked.Count}" : "remove"), ("esc", "back"));
    }

    // A row: the name, where it came from and what Enter does under it; on the right its source,
    // or "picked", or the second Enter's question in red.
    void AddRemovalRow(Package p, List<int> hits)
    {
        var item = removeByKey.GetValueOrDefault(p.Key);
        bool picked = pkgPicked.ContainsKey(p.Key);
        bool removable = item != null && InstalledPackages.How(item) == Removal.Manager;
        var sub = string.Join(" · ", new[] { p.Id == p.Name ? "" : p.Id, p.Version, p.Detail }.Where(s => s.Length > 0));
        var package = p;
        if (removeArmed == p.Key)
        {
            AddRow(Glyph(0xF0A7A), p.Name, Hex(theme.Red), () => RemovePackages(package), Tag.Remove, "Enter again: remove", sub: sub, highlight: hits);
        }
        else
        {
            AddRow(Glyph(picked ? 0xF05E0 : 0xF03D3), p.Name, Hex(picked ? theme.Accent : removable ? SourceColor(p.Source) : theme.Comment),
                () => RemovePackages(package), Tag.Text, picked ? "picked" : InstalledPackages.Label(p.Source),
                dim: !removable, sub: sub, highlight: hits);
        }
        pkgShown.Add(p);
    }

    // Enter: asks once (the row turns red and says so), then removes. With picks, all of them.
    void RemovePackages(Package current)
    {
        var picked = pkgPicked.Count > 0 ? pkgPicked.Keys.ToList() : [current.Key];
        var items = picked.Select(k => removeByKey.GetValueOrDefault(k)).OfType<InstalledPackage>().ToList();
        if (InstalledPackages.RemoveCommand(items) is not { } command)
        {
            // Nothing here a manager can remove: say how it does come off.
            FooterLeft.Text = items.Count > 0 ? InstalledPackages.Says(items[0]) : "Nothing to remove";
            return;
        }
        var ask = pkgPicked.Count > 0 ? "picked" : current.Key;
        if (removeArmed != ask)
        {
            removeArmed = ask;
            int keep = selected;
            Render();
            selected = Math.Min(keep, Math.Max(0, rows.Count - 1));
            Highlight();
            return;
        }
        removeArmed = null;
        pkgPicked.Clear();
        pkgMode = ""; // the list is read again next time
        HideMenu();
        MenuModel.Omawin("launch", "terminal", "--title", "menu-task", "--", "pwsh", "-NoLogo", "-Command",
            command + "; Write-Host \"`n  done · press any key to close\" -ForegroundColor DarkGray; $null = [Console]::ReadKey($true)");
    }

    // Tab / Shift+Tab: the next source.
    void CycleRemoveFilter(int by)
    {
        int at = Array.IndexOf(InstalledPackages.Filters, removeFilter);
        removeFilter = InstalledPackages.Filters[(at + by + InstalledPackages.Filters.Length) % InstalledPackages.Filters.Length];
        removeArmed = null;
        selected = 0;
        Render();
    }

    // The source chips, as the install side has them; each with how many it has.
    void RemoveChips()
    {
        Scopes.Children.Clear();
        foreach (var filter in InstalledPackages.Filters)
        {
            int count = (removeList ?? []).Count(p => InstalledPackages.InFilter(p, filter));
            if (count == 0 && filter != "all" && filter != removeFilter) continue;
            bool on = filter == removeFilter;
            var color = on ? (filter == "all" ? theme.Foreground : SourceColor(filter)) : theme.Muted;
            var chip = new Border
            {
                CornerRadius = new CornerRadius(14), Padding = new Thickness(10, 4, 12, 5),
                Background = on ? Brush(Hex(color), 0x33) : Brush(Hex(theme.Selection), 0x90),
                BorderBrush = on ? Brush(Hex(color), 0x99) : new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = removeList == null ? InstalledPackages.Label(filter) : $"{InstalledPackages.Label(filter)} {count}",
                    FontFamily = textFont, FontSize = 12.5, FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal, Foreground = Brush(color),
                },
            };
            var f = filter;
            chip.Tapped += (_, _) => { removeFilter = f; removeArmed = null; selected = 0; Render(); Input.Focus(FocusState.Programmatic); };
            Scopes.Children.Add(chip);
        }
    }
}
