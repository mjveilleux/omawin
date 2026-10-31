using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SystemMenu;

// A plugin's cell in the bar (roadmap 4.2's "slot", 4.9.4): an empty cell of the plugin's width,
// with omawin's QML host (omawin-qs, src/qs-host) drawing the plugin's bar widget in a borderless
// window kept exactly over it. Plugins in Omarchy's format are QML written for Quickshell, which
// only runs on Linux; the host runs them on Windows.
//
// Which plugins are on: %LOCALAPPDATA%\launcher\plugins.txt, one id a line (omawin plugin
// add|remove writes it). Each needs its folder (shell/plugins/<id>, with a bar widget) and the
// host (%LOCALAPPDATA%\omawin\tools\qs-host\omawin-qs.exe, which `omawin plugin add` installs).
// The primary monitor's bar has them; the host is stopped with the bar.
public sealed class PluginSlot : BarWidget
{
    readonly string id, dir;
    readonly Grid cell;
    Process? host;
    string at = "";

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
    static string OnFile => Path.Combine(MenuModel.StateDir, "plugins.txt");

    // The plugins that are on and can run: a folder with a bar widget, and the host installed. The
    // cell's width: the plugin's own title width setting plus its icon, else 200.
    public static List<(string Place, PluginSlot Slot)> On()
    {
        var slots = new List<(string, PluginSlot)>();
        if (!File.Exists(HostExe) || !File.Exists(OnFile)) return slots;
        foreach (var raw in File.ReadAllLines(OnFile))
        {
            var id = raw.Trim();
            if (id.Length == 0 || id.StartsWith('#') || id.IndexOfAny(['/', '\\']) >= 0 || id.Contains("..")) continue;
            var dir = Path.Combine(MenuModel.Repo, "shell", "plugins", id);
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

    // Where the cell is on the screen, in pixels; the host is started there, or moved when the
    // bar's layout moves the cell (started again: it reads where it goes once).
    void Follow()
    {
        if (Bar == null || cell.XamlRoot == null || cell.ActualWidth <= 0) return;
        var scale = cell.XamlRoot.RasterizationScale;
        var p = cell.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, 0));
        var rect = $"{Bar.Screen.X + (int)Math.Round(p.X * scale)},{Bar.Screen.Y + (int)Math.Round(p.Y * scale)},{(int)Math.Round(cell.ActualWidth * scale)},{(int)Math.Round(cell.ActualHeight * scale)}";
        if (rect == at && host is { HasExited: false }) return;
        at = rect;
        Stop();
        try
        {
            var start = new ProcessStartInfo(HostExe) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in new[] { "--plugin-dir", dir, "--at", rect, "--parent", Environment.ProcessId.ToString() }) start.ArgumentList.Add(a);
            host = Process.Start(start);
        }
        catch (Exception ex) { App.Log(new InvalidOperationException($"plugin {id}: the QML host didn't start", ex)); }
    }

    void Stop()
    {
        try { if (host is { HasExited: false }) host.Kill(); } catch { }
        host?.Dispose();
        host = null;
    }
}
