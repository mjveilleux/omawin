using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace SystemMenu;

// The theme picker's two chips (Style › Theme; Tab moves between them): Built in, the themes on
// this PC (MainWindow.Tiles.cs, ThemePicker), and Community, every theme omarchy.org/themes lists,
// from the index CI rebuilds weekly (CommunityThemes.cs): its picture and colours, searchable by
// name, author and light / dark, most stars first. Resting on one shows it across the desktop
// (themes/theme.ps1 preview: its colours and wallpaper, nothing installed); moving to a built-in
// theme, Esc or closing puts yours back. Enter installs it at the commit the index checked
// (omawin theme add) and keeps it; Ctrl+Enter opens its repo. The footer credits its author.
public sealed partial class MainWindow
{
    bool communityThemes;          // the picker's Community chip
    string? previewing;            // the community theme shown across the desktop, not installed
    string? previewNext;           // the one the selection rests on, shown once it stays a moment
    Microsoft.UI.Dispatching.DispatcherQueueTimer? previewTimer;

    // The picker's part of Render: the chips, then the built-in grid or the community one.
    TileLayout? RenderThemePicker(string query)
    {
        SetCrumbs("style", "Theme");
        ThemeChips();
        if (!communityThemes)
        {
            Placeholder.Text = "Search themes…";
            var home = ThemePicker(query);
            FooterLeft.Text = $"{home.Cells.Count} themes";
            SetKeys(("←↑↓→", "move"), ("↵", "apply"), ("tab", "community"), ("esc", "back"));
            return home;
        }
        Placeholder.Text = "Search community themes: name, author, light, dark…";
        var grid = CommunityPicker(query);
        SetKeys(("↵", "install"), ("ctrl+↵", "its repo"), ("tab", "built in"), ("esc", "back"));
        if (grid.Cells.Count > 0)
        {
            FooterLeft.Text = $"{grid.Cells.Count} community themes";
            return grid;
        }
        previewTimer?.Stop(); // nothing selected to show
        previewNext = null;
        AddRow(char.ConvertFromUtf32(0xF0156), CommunityThemes.All().Count == 0
                ? "The community themes arrive a minute after the menu starts (it needs the network)"
                : "No matches", Hex(theme.Comment), () => { }, dim: true);
        FooterLeft.Text = "omarchy.org/themes";
        return null;
    }

    TileLayout CommunityPicker(string query)
    {
        var layout = new TileLayout();
        // Installed ones checked: their clones are in launcher\themes\sources (theme.ps1).
        var sources = Path.Combine(MenuModel.InstalledThemesDir, "sources");
        var installed = Directory.Exists(sources)
            ? Directory.GetDirectories(sources).Select(d => Path.GetFileName(d)).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>();
        layout.Flow(CommunityThemes.All().Where(c => CommunityList.Matches(c, query)).Select(c =>
        {
            var t = CommunityThemes.ThemeOf(c);
            return new TileSpec
            {
                Key = CommunityThemes.KeyPrefix + c.Name, Style = "swatch", Label = c.Title, Swatch = t,
                SwatchFile = CommunityThemes.KeyPrefix + c.Name, Current = installed.Contains(c.Name),
                Color = Hex(t.Accent), Run = () => InstallCommunity(c),
            };
        }), rowHeight: SwatchRowDip);
        return layout;
    }

    // Built in | Community, where the file finder has its folder chips; click one, or Tab.
    void ThemeChips()
    {
        Scopes.Children.Clear();
        void Chip(string label, bool community, int glyph)
        {
            bool on = communityThemes == community;
            var chip = new Border
            {
                CornerRadius = new CornerRadius(14), Padding = new Thickness(10, 4, 12, 5),
                Background = on ? Brush(Hex(theme.Accent), 0x33) : Brush(Hex(theme.Selection), 0x90),
                BorderBrush = on ? Brush(Hex(theme.Accent), 0x99) : new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(1),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 6,
                    Children =
                    {
                        new TextBlock { Text = char.ConvertFromUtf32(glyph), FontFamily = iconFont, FontSize = 13, Foreground = Brush(on ? theme.Accent : theme.Muted), VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = label, FontFamily = textFont, FontSize = 12.5, FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal, Foreground = Brush(on ? theme.Accent : theme.Muted), VerticalAlignment = VerticalAlignment.Center },
                    },
                },
            };
            chip.Tapped += (_, _) => { if (communityThemes != community) ToggleCommunity(); Input.Focus(FocusState.Programmatic); };
            Scopes.Children.Add(chip);
        }
        Chip("Built in", false, 0xF03D8);  // palette
        int count = CommunityThemes.All().Count;
        Chip(count > 0 ? $"Community · {count}" : "Community", true, 0xF0849); // account-group
    }

    // Tab in the theme picker: the other chip. Back on the built-in themes, yours is selected and
    // a community theme being shown is put back.
    void ToggleCommunity()
    {
        communityThemes = !communityThemes;
        selected = 0;
        if (!communityThemes)
        {
            EndPreview();
            if (Input.Text.Length == 0) selected = Math.Max(0, MenuModel.Themes().FindIndex(t => t.File == themeName));
        }
        Render();
    }

    CommunityTheme? SelectedCommunity() =>
        communityThemes && tileMode && selected < tiles.Count && tiles[selected].Spec.Key.StartsWith(CommunityThemes.KeyPrefix)
            ? CommunityThemes.Find(tiles[selected].Spec.Key[CommunityThemes.KeyPrefix.Length..]) : null;

    // From Highlight: the selected community theme's credit in the footer, and a preview of it
    // once the selection has rested on it a moment (arrowing past tiles doesn't re-theme the
    // desktop at each one).
    void ThemeHighlighted()
    {
        if (!communityThemes) return;
        var c = SelectedCommunity();
        previewTimer ??= MakePreviewTimer();
        previewTimer.Stop();
        previewNext = c?.Name;
        if (c == null) return;
        var facts = new List<string> { $"{c.Title} by {c.Author}" };
        if (c.License.Length > 0) facts.Add(c.License);
        if (c.Stars > 0) facts.Add($"{c.Stars} ★");
        facts.Add($"github.com/{c.Repo}");
        FooterLeft.Text = string.Join(" · ", facts);
        previewTimer.Start();
    }

    Microsoft.UI.Dispatching.DispatcherQueueTimer MakePreviewTimer()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(450);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            if (!pickingTheme || !communityThemes || previewNext == null || previewNext == previewing || !appWindow.IsVisible) return;
            previewing = previewNext;
            CommunityThemes.Preview(previewing);
        };
        return timer;
    }

    // Your theme back on the desktop, if a community one is showing.
    void EndPreview()
    {
        previewTimer?.Stop();
        previewNext = null;
        if (previewing == null) return;
        previewing = null;
        CommunityThemes.Preview(null);
    }

    // Leaving the picker (Esc, another submenu, closing): your theme back, the chip back on Built
    // in, and the community pictures let go.
    void LeaveThemePicker()
    {
        EndPreview();
        communityThemes = false;
        CommunityThemes.Forget();
    }

    // Enter: installed at the index's commit and applied for real (theme.ps1 add), so there's no
    // preview to put back.
    void InstallCommunity(CommunityTheme c)
    {
        previewTimer?.Stop();
        previewNext = previewing = null;
        CommunityThemes.EndPreviewForInstall();
        MenuModel.Omawin("theme", "add", c.Name);
        HideMenu();
    }

    // Ctrl+Enter: the selected community theme's repo, its author's page for it.
    void OpenCommunityRepo()
    {
        if (SelectedCommunity() is not { } c || CommunityList.RepoOf(c.Repo) != c.Repo) return;
        try { Process.Start(new ProcessStartInfo($"https://github.com/{c.Repo}") { UseShellExecute = true }); }
        catch (Exception ex) { App.Log(ex); }
        HideMenu();
    }
}
