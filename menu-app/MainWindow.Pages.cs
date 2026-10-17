using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace SystemMenu;

// The launcher's own pages: Projects, Clipboard, Localhost, Reports, Shortcuts, Audio (Win+A)
// and Quick settings (Audio with the switches) (and a VPN that's up offers to disconnect). Each is a
// list or tiles in this same window; Esc goes back to the launcher. Their data is in Pages.cs,
// Background.cs and QuickSettings.cs (Quick settings' switches). About is not one of them: it
// is its own window, drawn like the terminal's fastfetch (AboutWindow.xaml.cs).
public sealed partial class MainWindow
{
    static readonly string[] PageNames = ["projects", "clipboard", "localhost", "reports", "keys", "audio", "quick", "vpn"];
    public static bool IsPage(string? builtIn) => builtIn != null && PageNames.Contains(builtIn);

    string? page;                  // the page on screen (launcher only), or null for the launcher's list
    string pageArg = "";           // the VPN connection's name
    List<Project>? projects;       // null while they're being found
    List<Server>? servers;         // null while ports are being asked
    Server? stopping;              // asking "Stop this server?"
    bool Wide => finding || (launching && page == "clipboard");

    // --page <name> / --launcher <name> (Win+Shift+V: clipboard): the launcher, on that page
    // (just the launcher for a name that isn't one).
    public void OpenPage(string name)
    {
        try
        {
            var parts = name.Split(' ', 2);
            Show("", asLauncher: true);
            if (IsPage(parts[0])) EnterPage(parts[0], parts.Length > 1 ? parts[1] : "");
        }
        catch (Exception ex) { App.Log(ex); }
    }

    void EnterPage(string name, string arg = "")
    {
        // Audio (Win+A, `launcher audio`) is the sound alone; Quick settings (`launcher quick`)
        // is the same page with the machine's switches above the sound. The id stays audio.
        if (name is "audio" or "quick") quickSwitches = name == "quick";
        if (name == "quick") name = "audio";
        if (name == "vpn")
        {
            // Not connected: Windows' connect box, already dialing. Connected: offer to disconnect.
            if (!Vpn.Connected(arg)) { HideMenu(); Vpn.Connect(arg); return; }
        }
        page = name;
        pageArg = arg;
        selected = 0;
        stopping = null;
        routing = null;
        Input.Text = "";
        // Quick settings reads the machine again every time it opens (drawing the last snapshot
        // until that lands), and opens on the first speaker, not on Wi-Fi. Audio reads only the sound.
        if (name == "audio")
        {
            quickList = null; quickNote = null; offArmed = null; quickFirstSound = true; brightTarget.Clear(); wifiNetworks = null; wifiDenied = false;
            if (quickSwitches) { WatchQuick(); LoadQuick(); }
        }
        if (name == "projects") { projects = Projects.Latest; Projects.Find().ContinueWith(t => DispatcherQueue.TryEnqueue(() => { projects = t.IsCompletedSuccessfully ? t.Result : []; if (page == "projects") Render(); })); }
        if (name == "localhost") LoadServers();
        Render();
    }

    void LoadServers()
    {
        servers = null;
        Task.Run(Localhost.FindAsync).ContinueWith(t => DispatcherQueue.TryEnqueue(() => { servers = t.IsCompletedSuccessfully ? t.Result : []; if (page == "localhost") Render(); }));
    }

    // Esc on a page: back to the launcher's list, on that page's item.
    bool LeavePage()
    {
        if (!launching || page == null) return false;
        if (stopping != null) { stopping = null; Render(); return true; }
        if (LeaveQuickList()) return true;
        if (LeaveRouting()) return true;
        var from = page == "audio" && quickSwitches ? "quick" : page;
        page = null;
        if (from is "audio" or "quick") { StopWatchingQuick(); offArmed = null; }
        selectKey = launchItems.FirstOrDefault(i => i.BuiltIn == from)?.Key;
        Render();
        Slide(-1);
        return true;
    }

    // The rows matching what's typed, best first (all of them, in order, when nothing is).
    static List<(T Item, List<int> Hits)> Filter<T>(IEnumerable<T> items, string query, Func<T, string> text)
    {
        if (query.Length == 0) return [.. items.Select(i => (i, new List<int>()))];
        return [.. items.Select(i => { var hits = new List<int>(); return (i, hits, score: Fuzzy.Match(text(i), query, hits)); })
            .Where(x => x.score != Fuzzy.None).OrderByDescending(x => x.score).Select(x => (x.i, x.hits))];
    }

    static string Glyph(int code) => char.ConvertFromUtf32(code);

    // Lists, except Audio's tiles (returned to be drawn as tiles).
    TileLayout? RenderPage(string query)
    {
        string title = page switch { "projects" => "Projects", "clipboard" => "Clipboard", "localhost" => "Localhost", "reports" => "Reports", "keys" => "Shortcuts", "audio" => quickSwitches ? "Quick settings" : "Audio", _ => "VPN" };
        SetCrumbs("", title);
        switch (page)
        {
            case "projects":
                Placeholder.Text = "Search projects…";
                if (projects == null) { AddRow(Glyph(0xF0996), "Finding projects…", Hex(theme.Comment), () => { }, dim: true); break; }
                foreach (var (p, hits) in Filter(projects, query, p => p.Name))
                {
                    var project = p;
                    AddRow(Glyph(p.Wsl ? 0xF31B : 0xE83A), p.Name, Hex(p.Wsl ? theme.Accent : theme.Green), () => { HideMenu(); Projects.Open(project); },
                        Tag.Text, p.Wsl ? "ubuntu" : "windows", sub: p.Shown, highlight: hits);
                }
                if (projects.Count == 0) AddRow(Glyph(0xF0156), "No git repos found", Hex(theme.Comment), () => { }, dim: true);
                FooterLeft.Text = $"{projects.Count} projects · zoxide's first";
                SetKeys(("↵", "open"), ("esc", "back"));
                break;

            case "clipboard":
                Placeholder.Text = "Search what you copied…";
                var entries = ClipboardHistory.Entries();
                clipFiles = [];
                foreach (var (file, hits) in Filter(entries, query, f => OneLine(f, 300)))
                {
                    var f = file;
                    clipFiles.Add(f);
                    AddRow(Glyph(0xF014C), OneLine(f, 90), Hex(theme.Muted), () => Paste(f), Tag.Text, Ago(ClipboardHistory.When(f)), highlight: hits);
                }
                if (entries.Count == 0) AddRow(Glyph(0xF014C), "Nothing copied yet: copy some text and it shows up here", Hex(theme.Comment), () => { }, dim: true);
                FooterLeft.Text = $"{entries.Count} copied";
                SetKeys(("↵", "paste"), ("ctrl x", "delete"), ("esc", "back"));
                break;

            case "localhost":
                Placeholder.Text = "Search servers and sites…";
                if (stopping is { } s)
                {
                    SetCrumbs("", "Stop server?");
                    AddRow(Glyph(0xF04DB), $"Stop {s.Title}", Hex(theme.Red), () => { Localhost.Stop(s); stopping = null; LoadServers(); Render(); }, Tag.Text, $":{s.Port} · {Program(s)}");
                    AddRow(Glyph(0xF0156), "No, keep it running", Hex(theme.Comment), () => { stopping = null; Render(); });
                    SetKeys(("↵", "choose"), ("esc", "back"));
                    break;
                }
                if (servers == null) AddRow(Glyph(0xF048B), "Looking for running servers…", Hex(theme.Comment), () => { }, dim: true);
                else
                    foreach (var (sv, hits) in Filter(servers, query, x => x.Title))
                    {
                        var server = sv;
                        AddRow(Glyph(0xF048B), sv.Title, GradientAt(1), () => { HideMenu(); Localhost.Open(server.Url); }, Tag.Text, "running",
                            sub: $":{sv.Port} · {Program(sv)}", highlight: hits);
                        pageRows[rows.Count - 1] = sv;
                    }
                foreach (var (site, hits) in Filter(Localhost.Sites(), query, x => x.Name))
                {
                    var url = site.Url;
                    AddRow(site.Icon, site.Name, Hex(theme.Accent), () => { HideMenu(); Localhost.Open(url); }, Tag.Text, "site",
                        sub: System.Text.RegularExpressions.Regex.Replace(url, "^https?://(www\\.)?|/$", ""), highlight: hits);
                }
                FooterLeft.Text = servers == null ? "looking…" : servers.Count == 1 ? "1 server running" : $"{servers.Count} servers running";
                SetKeys(("↵", "open in chrome"), ("ctrl x", "stop server"), ("esc", "back"));
                break;

            case "reports":
                Placeholder.Text = "Search reports…";
                var reports = Reports.Load();
                // Each tag its own colour, the same every time (tags are whatever the reports script says).
                string[] tagColors = [theme.Cyan, theme.Purple, theme.Green, theme.Red];
                string TagColor(string tag) => string.IsNullOrEmpty(tag) ? theme.Accent : tagColors[tag.Sum(c => c) % tagColors.Length];
                foreach (var (r, hits) in Filter(reports, query, x => x.Name).Take(150))
                {
                    var report = r;
                    AddRow(Glyph(0xF0128), r.Name, Hex(TagColor(r.Tag)), () => { HideMenu(); Reports.Import(report); },
                        Tag.Text, r.Modified.ToString("MMM d, yyyy"), sub: $"{r.Tag} · {r.Folder}", highlight: hits);
                }
                if (reports.Count == 0) AddRow(Glyph(0xF0996), "No reports yet: " + Reports.Age(), Hex(theme.Comment), () => { }, dim: true);
                FooterLeft.Text = $"{reports.Count} reports · {Reports.Age()}";
                SetKeys(("↵", "import"), ("esc", "back"));
                break;

            case "keys":
                Placeholder.Text = "Search shortcuts…";
                var keys = Shortcuts.Load();
                var sections = keys.Select(k => k.Section).Distinct().ToList();
                foreach (var (k, hits) in Filter(keys, query, x => $"{x.Keys} {x.What} {x.Section}"))
                {
                    var color = GradientAt(sections.IndexOf(k.Section) / (double)Math.Max(1, sections.Count - 1));
                    AddRow(Glyph(0xF030C), k.Keys, color, () => { }, Tag.Text, k.Section, sub: k.What);
                }
                FooterLeft.Text = $"{keys.Count} shortcuts";
                SetKeys(("↑↓", "move"), ("esc", "back"));
                break;

            case "audio":
                return QuickPage(query);

            case "vpn":
                SetCrumbs("", "VPN connected");
                Placeholder.Text = pageArg;
                AddRow(Glyph(0xF0582), "Disconnect", Hex(theme.Red), () => { HideMenu(); Vpn.Disconnect(pageArg); }, Tag.Text, pageArg);
                AddRow(Glyph(0xF0156), "No, stay connected", Hex(theme.Comment), () => LeavePage());
                SetKeys(("↵", "choose"), ("esc", "back"));
                break;
        }
        return null;
    }

    static string Program(Server s) => s.Program == "wslrelay" ? "wsl" : s.Program;

    // --- the clipboard -------------------------------------------------------------------
    List<string> clipFiles = [];
    readonly Dictionary<int, Server> pageRows = [];

    static string OneLine(string file, int max)
    {
        string text;
        try { text = File.ReadAllText(file); } catch { return ""; }
        text = System.Text.RegularExpressions.Regex.Replace(text.Trim().Replace("\r", ""), "\n+", " ⏎ ");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
        return text.Length > max ? text[..(max - 1)] + "…" : text;
    }

    [DllImport("user32.dll")] static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);

    // Back on the clipboard, then pasted (Ctrl+V) into the window you were in.
    void Paste(string file)
    {
        string text;
        try { text = File.ReadAllText(file); } catch { return; }
        ClipboardHistory.SetText(text);
        var target = previous;
        HideMenu();
        if (target != IntPtr.Zero) SetForegroundWindow(target);
        Task.Delay(200).ContinueWith(_ =>
        {
            keybd_event(0x11, 0, 0, UIntPtr.Zero);      // ctrl
            keybd_event(0x56, 0, 0, UIntPtr.Zero);      // v
            keybd_event(0x56, 0, 2, UIntPtr.Zero);
            keybd_event(0x11, 0, 2, UIntPtr.Zero);
        });
    }

    // Ctrl+X: delete the clipboard entry, or (Localhost) stop the server, after asking.
    void PageDelete()
    {
        if (page == "clipboard" && selected < clipFiles.Count)
        {
            try { File.Delete(clipFiles[selected]); } catch { }
            Render();
        }
        else if (page == "localhost" && pageRows.TryGetValue(selected, out var server))
        {
            stopping = server;
            selected = 1; // "No" first, so Enter twice doesn't stop it by accident
            Render();
        }
    }

    // The clipboard page's preview: the whole text (its first lines), when, how long.
    void ShowClipPreview()
    {
        if (selected >= clipFiles.Count) { Preview.Visibility = Visibility.Collapsed; return; }
        Preview.Visibility = Visibility.Visible;
        var file = clipFiles[selected];
        string text;
        try { text = File.ReadAllText(file); } catch { text = ""; }
        Preview.Background = Brush(Hex(theme.Surface), 0xB0);
        Preview.BorderBrush = Brush(Hex(theme.Border), 0x80);
        PreviewArt.Visibility = Visibility.Collapsed;
        PreviewName.Text = $"Copied {Ago(ClipboardHistory.When(file))}";
        PreviewName.Foreground = Brush(theme.Foreground);
        PreviewPath.Children.Clear();
        PreviewFacts.Children.Clear();
        int lines = text.Split('\n').Length;
        PreviewFacts.Children.Add(Pill($"{text.Length:N0} characters", theme.Accent));
        PreviewFacts.Children.Add(Pill(lines == 1 ? "1 line" : $"{lines:N0} lines", theme.Muted));
        PreviewText.FontFamily = monoFont;
        PreviewText.Foreground = Brush(theme.Foreground);
        PreviewText.TextWrapping = TextWrapping.Wrap;
        PreviewTextBox.Background = Brush(Hex(theme.Background), 0x90);
        PreviewText.Text = string.Join("\n", text.Replace("\r", "").Split('\n').Take(30));
        PreviewTextBox.Visibility = Visibility.Visible;
    }
}
