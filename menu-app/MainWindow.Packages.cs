using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace SystemMenu;

// The launcher's Packages page (Install > Package, `omawin launcher packages`), after Omarchy's
// package picker: pick winget, Chocolatey or Scoop (chips; Tab), type to fuzzy-find a package,
// see what it is beside the list, and Enter installs it in a terminal you can watch.
// Ctrl+Space picks several first. The data is in Packages.cs.
public sealed partial class MainWindow
{
    string pkgSource = "winget";
    readonly Dictionary<string, Package> pkgPicked = [];  // by Package.Key, from any source
    List<Package> pkgShown = [];                           // the rows, in order
    HashSet<string> pkgInstalled = [];
    int pkgPreviewToken;

    // Chocolatey's feed is asked as you type: the last question, its answer (null while asked).
    string? chocoAsked;
    List<Package>? chocoFound;
    string? chocoError;
    CancellationTokenSource? chocoCancel;

    const int PackageRows = 120;

    void RenderPackages(string query)
    {
        SetCrumbs("", "Packages");
        Placeholder.Text = $"Search {Packages.Label(pkgSource)}…";
        PackageChips();
        pkgShown = [];
        pkgInstalled = Packages.Installed();

        if (!Packages.Available(pkgSource))
        {
            string what = pkgSource == "choco" ? "from winget, asks for admin" : "from scoop.sh, into your user folder";
            AddRow(Glyph(0xF03D3), $"{Packages.Label(pkgSource)} isn't on this PC: install it", Hex(SourceColor(pkgSource)),
                () => { HideMenu(); Packages.InstallManager(pkgSource); }, Tag.Text, what);
            FooterLeft.Text = $"no {Packages.Label(pkgSource)}";
            SetKeys(("tab", "winget · choco · scoop"), ("↵", "install it"), ("esc", "back"));
            return;
        }

        List<Package>? list;
        string? waiting = null;
        if (pkgSource == "choco")
        {
            list = query.Length < 2 ? [] : AskChoco(query);
            if (query.Length < 2) waiting = "Type to search Chocolatey's community packages";
            else if (list == null) waiting = "Searching Chocolatey…";
            else if (chocoError != null) waiting = $"Chocolatey didn't answer: {chocoError}";
        }
        else
        {
            list = Packages.Catalog(pkgSource, () => { if (launching && page == "packages") Render(); });
            if (list == null) waiting = pkgSource == "winget" ? "Reading winget's catalog…" : "Reading Scoop's buckets…";
            else if (list.Count == 0) waiting = pkgSource == "scoop" ? "No buckets yet: scoop bucket add extras" : "winget's catalog isn't here yet: run winget once";
        }

        int total = 0;
        if (list != null)
        {
            var matches = MatchPackages(list, query, keepOrder: pkgSource == "choco");
            total = matches.Count;
            foreach (var (p, hits) in matches.Take(PackageRows)) AddPackageRow(p, hits);
        }
        if (pkgShown.Count == 0)
            AddRow(Glyph(waiting != null ? 0xF0996 : 0xF0156), waiting ?? "No matches", Hex(theme.Comment), () => { }, dim: true);

        string count = list == null ? "" : pkgSource == "choco" ? $"{total} found" : query.Length == 0 ? $"{list.Count:N0} packages" : $"{total:N0} of {list.Count:N0}";
        FooterLeft.Text = pkgPicked.Count > 0 ? $"{count} · {pkgPicked.Count} picked" : count;
        SetKeys(("tab", "winget · choco · scoop"), ("ctrl space", "pick"),
            ("↵", pkgPicked.Count > 1 ? $"install {pkgPicked.Count}" : "install"), ("esc", "back"));
    }

    // The packages matching what's typed, best first: the launcher's fuzzy matching (Fuzzy.cs) on
    // the name, and a little weaker on the id. A quick check first (the letters there in order
    // at all) keeps winget's ~16,000 from being scored on every key. Nothing typed: by name.
    // keepOrder: the list comes ranked already (Chocolatey's search), its matches stay in front.
    static List<(Package Item, List<int> Hits)> MatchPackages(List<Package> list, string query, bool keepOrder)
    {
        if (query.Length == 0) return [.. list.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Select(p => (p, new List<int>()))];
        var words = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var scored = new List<(Package Item, List<int> Hits, double Score, int Order)>();
        for (int i = 0; i < list.Count; i++)
        {
            var p = list[i];
            if (!keepOrder && !words.All(w => InOrder(p.Lower, w))) continue;
            var hits = new List<int>();
            double score = Fuzzy.Match(p.Name, query, hits);
            double byId = p.Id == p.Name ? Fuzzy.None : Fuzzy.Match(p.Id, query) - 0.5;
            if (byId > score) { score = byId; hits.Clear(); }
            if (score == Fuzzy.None)
            {
                if (!keepOrder) continue;
                score = double.MinValue; // found by Chocolatey (e.g. in its description): after the matches
            }
            scored.Add((p, hits, score, i));
        }
        return [.. scored.OrderByDescending(x => x.Score).ThenBy(x => keepOrder ? x.Order : x.Item.Name.Length).Select(x => (x.Item, x.Hits))];
    }

    // Whether the letters of word are in text, in order.
    static bool InOrder(string text, string word)
    {
        int at = 0;
        foreach (char c in word)
        {
            at = text.IndexOf(c, at);
            if (at < 0) return false;
            at++;
        }
        return true;
    }

    // Asks Chocolatey's feed once typing pauses; the answer redraws the page.
    List<Package>? AskChoco(string query)
    {
        if (query == chocoAsked) return chocoFound;
        chocoAsked = query;
        chocoFound = null;
        chocoError = null;
        chocoCancel?.Cancel();
        var cancel = chocoCancel = new CancellationTokenSource();
        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(250, cancel.Token);
                var found = await Packages.SearchChoco(query, cancel.Token);
                DispatcherQueue.TryEnqueue(() => { if (chocoAsked == query) { chocoFound = found; if (launching && page == "packages") Render(); } });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                DispatcherQueue.TryEnqueue(() => { if (chocoAsked == query) { chocoFound = []; chocoError = ex.Message; if (launching && page == "packages") Render(); } });
            }
        });
        return null;
    }

    string SourceColor(string source) => source switch { "winget" => theme.Accent, "choco" => theme.Yellow, _ => theme.Green };

    // A package: its name (the typed letters lit), its id, version and what it is under it; on
    // the right, installed or picked.
    void AddPackageRow(Package p, List<int> hits)
    {
        bool here = pkgInstalled.Contains(p.Key);
        bool picked = pkgPicked.ContainsKey(p.Key);
        var sub = string.Join(" · ", new[] { p.Id == p.Name ? "" : p.Id, p.Version, p.Detail }.Where(s => s.Length > 0));
        var package = p;
        AddRow(Glyph(picked ? 0xF05E0 : 0xF03D3), p.Name, Hex(picked ? theme.Accent : SourceColor(p.Source)), () => InstallPackages(package),
            here ? Tag.Installed : Tag.Text, here ? "installed" : picked ? "picked" : Packages.Label(p.Source),
            sub: sub.Length > 0 ? sub : null, highlight: hits);
        pkgShown.Add(p);
    }

    // The source chips (where the file finder has its folder chips): click one, or Tab.
    void PackageChips()
    {
        Scopes.Children.Clear();
        foreach (var source in Packages.Sources)
        {
            bool on = source == pkgSource;
            bool here = Packages.Available(source);
            var color = on ? SourceColor(source) : here ? theme.Muted : theme.Comment;
            var chip = new Border
            {
                CornerRadius = new CornerRadius(14), Padding = new Thickness(10, 4, 12, 5),
                Background = on ? Brush(Hex(SourceColor(source)), 0x33) : Brush(Hex(theme.Selection), 0x90),
                BorderBrush = on ? Brush(Hex(SourceColor(source)), 0x99) : new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(1),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 6,
                    Children =
                    {
                        new TextBlock { Text = Glyph(source switch { "winget" => 0xF05B3, "choco" => 0xF0176, _ => 0xF0E07 }), FontFamily = iconFont, FontSize = 13, Foreground = Brush(color), VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = here ? Packages.Label(source) : $"{Packages.Label(source)} (not here)", FontFamily = textFont, FontSize = 12.5, FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal, Foreground = Brush(color), VerticalAlignment = VerticalAlignment.Center },
                    },
                },
            };
            var s = source;
            chip.Tapped += (_, _) => { SetPackageSource(s); Input.Focus(FocusState.Programmatic); };
            Scopes.Children.Add(chip);
        }
    }

    void SetPackageSource(string source)
    {
        pkgSource = source;
        selected = 0;
        Render();
    }

    // Tab / Shift+Tab: the next / previous source.
    void CyclePackageSource(int by)
    {
        int at = Array.IndexOf(Packages.Sources, pkgSource);
        SetPackageSource(Packages.Sources[(at + by + Packages.Sources.Length) % Packages.Sources.Length]);
    }

    // Ctrl+Space: pick the package (or drop it), and move on to the next, as fzf's Tab does.
    void TogglePackagePick()
    {
        if (selected >= pkgShown.Count) return;
        var p = pkgShown[selected];
        if (!pkgPicked.Remove(p.Key)) pkgPicked[p.Key] = p;
        int keep = Math.Min(selected + 1, pkgShown.Count - 1);
        Render();
        selected = Math.Min(keep, Math.Max(0, rows.Count - 1));
        Highlight();
    }

    // Enter: the picked ones (from any source), or this one.
    void InstallPackages(Package current)
    {
        var picked = pkgPicked.Count > 0 ? pkgPicked.Values.ToList() : [current];
        pkgPicked.Clear();
        HideMenu();
        Packages.Install(picked);
    }

    // Beside the list: the package's name and where it's from, its version, whether it's here,
    // and what its manager says about it (winget's comes in a second or two).
    void ShowPackagePreview()
    {
        int token = ++pkgPreviewToken;
        if (selected >= pkgShown.Count) { Preview.Visibility = Visibility.Collapsed; return; }
        var p = pkgShown[selected];
        Preview.Visibility = Visibility.Visible;
        Preview.Background = Brush(Hex(theme.Surface), 0xB0);
        Preview.BorderBrush = Brush(Hex(theme.Border), 0x80);

        PreviewArt.Visibility = Visibility.Visible;
        PreviewArt.Height = 150;
        PreviewArt.Background = Brush(Hex(theme.Background), 0x90);
        PreviewArt.Child = new TextBlock
        {
            Text = Glyph(0xF03D3), FontFamily = iconFont, FontSize = 64, Foreground = Brush(SourceColor(p.Source)),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        PreviewName.Text = p.Name;
        PreviewName.Foreground = Brush(theme.Foreground);

        PreviewPath.Children.Clear();
        PreviewPath.Children.Add(new TextBlock { Text = Packages.Label(p.Source), FontFamily = textFont, FontSize = 12, Foreground = Brush(SourceColor(p.Source)) });
        PreviewPath.Children.Add(new TextBlock { Text = "›", FontSize = 12, Foreground = Brush(theme.Comment) });
        PreviewPath.Children.Add(new TextBlock { Text = p.Id, FontFamily = monoFont, FontSize = 12, Foreground = Brush(theme.Muted) });

        PreviewFacts.Children.Clear();
        if (p.Version.Length > 0) PreviewFacts.Children.Add(Pill(p.Version, theme.Accent));
        if (pkgInstalled.Contains(p.Key)) PreviewFacts.Children.Add(Pill("installed", theme.Green));
        if (pkgPicked.ContainsKey(p.Key)) PreviewFacts.Children.Add(Pill("picked", theme.Purple));

        PreviewText.FontFamily = textFont;
        PreviewText.Foreground = Brush(theme.Foreground);
        PreviewText.TextWrapping = TextWrapping.Wrap;
        PreviewTextBox.Background = Brush(Hex(theme.Background), 0x90);
        PreviewTextBox.Visibility = Visibility.Visible;
        PreviewText.Text = p.Detail.Length > 0 ? p.Detail : "…";
        Packages.Details(p).ContinueWith(t => DispatcherQueue.TryEnqueue(() =>
        {
            if (token != pkgPreviewToken || !t.IsCompletedSuccessfully || t.Result.Length == 0) return;
            PreviewText.Text = t.Result;
        }));
    }
}
