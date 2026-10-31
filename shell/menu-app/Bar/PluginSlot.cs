using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SystemMenu.Plugins;

namespace SystemMenu;

// A plugin's cell in the bar (roadmap 4.2's "slot", 4.9.4): an empty cell of the plugin's width,
// with omawin's QML host (omawin-qs, src/qs-host) drawing the plugin's bar widget in a borderless
// window kept exactly over it. Plugins in Omarchy's format are QML written for Quickshell, which
// only runs on Linux; the host runs them on Windows.
//
// Which plugins are on: %LOCALAPPDATA%\launcher\plugins.txt, one id a line (omawin plugin
// add|remove writes it; the bars are made again when it changes, Bar.WatchPlugins). Each needs its
// folder (shell/plugins/<id>, or %LOCALAPPDATA%\omawin\plugins\<id> for one from the gallery, with
// a bar widget) and the host (%LOCALAPPDATA%\omawin\tools\qs-host\omawin-qs.exe, which `omawin
// plugin add` installs). Every monitor's bar has them, like Omarchy's: one host per plugin per
// monitor, each over its own cell, told over stdin whether its monitor is GlazeWM's focused one
// (src/qs-host/src/bar.rs), so a key that opens a plugin's panel opens it there. The hosts end
// with the bar.
public sealed class PluginSlot : BarWidget
{
    readonly string id, dir;
    readonly Grid cell;
    Process? host;
    string at = "";
    bool? focused; // what the host was last told; null: nothing yet
    bool closed;

    PluginSlot(string id, string dir, double width)
    {
        this.id = id;
        this.dir = dir;
        cell = new Grid { Width = width, Height = BarWindow.Height };
        cell.LayoutUpdated += (_, _) => Follow();
        cell.Unloaded += (_, _) => Stop();
    }

    public override FrameworkElement Element => cell;

    static string HostExe => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"omawin\tools\qs-host\omawin-qs.exe");
    public static string OnFile => Path.Combine(MenuModel.StateDir, "plugins.txt");
    public static string ShippedDir => Path.Combine(MenuModel.Repo, "shell", "plugins");
    public static string DownloadedDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "omawin", "plugins");
    public static string GalleryFile => Path.Combine(MenuModel.StateDir, "plugin-gallery", GalleryIndex.FileName);

    // The ids that are on, as plugins.txt says (empty without it).
    public static List<string> OnIds()
    {
        try { return File.Exists(OnFile) ? PluginFolders.ReadOn(File.ReadAllLines(OnFile)) : []; }
        catch (IOException) { return []; }
    }

    // What the bars' plugin cells depend on: the ids that are on, and whether the host is here.
    public static string State() => (File.Exists(HostExe) ? "host\n" : "") + string.Join('\n', OnIds());

    // The plugins that are on and can run, for one bar: a folder with a bar widget, and the host
    // installed. Called once per monitor, so each bar has cells (and hosts) of its own. The cell's
    // width: the plugin's own title width setting plus its icon, else 200.
    public static List<(string Place, PluginSlot Slot)> On()
    {
        var slots = new List<(string, PluginSlot)>();
        if (!File.Exists(HostExe)) return slots;
        foreach (var id in OnIds())
        {
            if (PluginFolders.Find(id, ShippedDir, DownloadedDir) is not { } dir) continue;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "manifest.json")));
                var root = doc.RootElement;
                if (!root.TryGetProperty("entryPoints", out var entry) || !entry.TryGetProperty("barWidget", out _)) continue;
                string place = "right";
                double width = 200;
                if (root.TryGetProperty("barWidget", out var bw))
                {
                    if (bw.TryGetProperty("defaultSection", out var s) && s.GetString() is { Length: > 0 } section) place = section;
                    if (bw.TryGetProperty("defaults", out var d) && d.TryGetProperty("titleWidth", out var tw) && tw.TryGetInt32(out var w)) width = w + 40;
                }
                slots.Add((place, new PluginSlot(id, dir, width)));
            }
            catch (Exception ex) { App.Log(ex); }
        }
        return slots;
    }

    // Whether this bar's monitor is GlazeWM's focused one (it holds the focused workspace); the
    // host hears of a change at once.
    public override void Update(BarData data)
    {
        bool now = Bar?.Monitor?.Workspaces.Any(w => w.Focused) == true;
        if (now == focused) return;
        focused = now;
        Tell();
    }

    void Tell()
    {
        if (focused is not { } on || host is not { HasExited: false }) return;
        try
        {
            host.StandardInput.WriteLine(on ? "focused 1" : "focused 0");
            host.StandardInput.Flush();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException) { } // it ended; Follow starts it again
    }

    // Where the cell is on the screen, in pixels; the host is started there, or moved when the
    // bar's layout moves the cell (started again: it reads where it goes once).
    void Follow()
    {
        if (closed || Bar == null || cell.XamlRoot == null || cell.ActualWidth <= 0) return;
        var scale = cell.XamlRoot.RasterizationScale;
        var p = cell.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, 0));
        var rect = $"{Bar.Screen.X + (int)Math.Round(p.X * scale)},{Bar.Screen.Y + (int)Math.Round(p.Y * scale)},{(int)Math.Round(cell.ActualWidth * scale)},{(int)Math.Round(cell.ActualHeight * scale)}";
        if (rect == at && host is { HasExited: false }) return;
        at = rect;
        Stop();
        try
        {
            // stdin stays open: it's how the bar talks to the host (focused 1|0).
            var start = new ProcessStartInfo(HostExe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
            foreach (var a in new[] { "--plugin-dir", dir, "--at", rect, "--parent", Environment.ProcessId.ToString() }) start.ArgumentList.Add(a);
            host = Process.Start(start);
            Tell();
        }
        catch (Exception ex) { App.Log(new InvalidOperationException($"plugin {id}: the QML host didn't start", ex)); }
    }

    // The bar is going: the host ends, and isn't started again by a last layout pass.
    public void Close()
    {
        closed = true;
        Stop();
    }

    void Stop()
    {
        try { if (host is { HasExited: false }) host.Kill(); } catch { }
        host?.Dispose();
        host = null;
    }
}
