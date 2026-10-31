using Windows.Devices.Enumeration;
using Windows.Devices.Lights;

namespace SystemMenu;

// The keyboard's lights follow the theme (roadmap 8.9): every RGB keyboard, mouse or light strip
// Windows 11 drives through Dynamic Lighting (Windows.Devices.Lights.LampArray, any brand that
// speaks the HID lighting standard) is painted in the theme's accent when it's plugged in and on
// every theme switch.
//
// Nothing runs for a PC without one: a device watcher (Windows' own arrival events, no polling)
// is all there is until a lamp array turns up, and only then is the theme file watched. On by
// default; look.keyboard-lights in the settings catalog turns it off (launcher\keyboard-lights).
//
// Windows lets an app drive the lamps while it's in front, or always once you pick it as the
// background controller in Settings › Personalization › Dynamic Lighting. (check) Whether an
// unpackaged app can be picked there or needs package identity first (roadmap 8.22's sparse
// package); until then the accent shows while omawin's launcher or menu is in front (where the
// theme is switched) and Windows' own effect the rest of the time.
public static class KeyboardLights
{
    static readonly object gate = new();
    static readonly Dictionary<string, LampArray> arrays = [];
    static DeviceWatcher? watcher;
    static FileSystemWatcher? themeWatcher;
    static string paintedWith = "";

    static string ValueFile => Path.Combine(MenuModel.StateDir, "keyboard-lights");

    // On unless turned off (the file says "off").
    public static bool On
    {
        get
        {
            try { return !File.Exists(ValueFile) || File.ReadAllText(ValueFile).Trim() != "off"; }
            catch (IOException) { return true; }
        }
    }

    public static void Start()
    {
        if (On) Watch();
    }

    // The setting's switch (Settings/Builtins.cs): written down, then started or stopped at once.
    public static void Set(bool on)
    {
        Directory.CreateDirectory(MenuModel.StateDir);
        File.WriteAllText(ValueFile, on ? "on" : "off");
        if (on) Watch();
        else Stop();
    }

    static void Watch()
    {
        lock (gate)
        {
            if (watcher != null) return;
            try
            {
                var w = DeviceInformation.CreateWatcher(LampArray.GetDeviceSelector());
                w.Added += async (_, info) => await Add(info.Id); // Add catches its own failures
                w.Removed += (_, update) => { lock (gate) arrays.Remove(update.Id); };
                w.Start();
                watcher = w;
            }
            catch (Exception ex) { App.Log(ex); } // no Dynamic Lighting on this Windows: nothing to do
        }
    }

    static void Stop()
    {
        lock (gate)
        {
            try { if (watcher?.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted) watcher.Stop(); }
            catch (Exception ex) { App.Log(ex); }
            watcher = null;
            themeWatcher?.Dispose();
            themeWatcher = null;
            // Let go of the devices: Windows hands them back to its own effect.
            arrays.Clear();
            paintedWith = "";
        }
    }

    static async Task Add(string id)
    {
        try
        {
            var lamps = await LampArray.FromIdAsync(id);
            if (lamps == null) return;
            lock (gate)
            {
                if (watcher == null) return; // turned off meanwhile
                arrays[id] = lamps;
                WatchTheme();
            }
            Paint(lamps, Accent());
        }
        catch (Exception ex) { App.Log(ex); }
    }

    // The theme file (set-theme.ps1 writes the theme's name there), watched only once there's a
    // device to paint. Several change events come for one write; the colour is compared first.
    static void WatchTheme()
    {
        if (themeWatcher != null) return;
        try
        {
            Directory.CreateDirectory(MenuModel.StateDir);
            var w = new FileSystemWatcher(MenuModel.StateDir, Path.GetFileName(MenuModel.CurrentThemeFile))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };
            FileSystemEventHandler changed = (_, _) => Repaint();
            w.Changed += changed;
            w.Created += changed;
            w.Renamed += (_, _) => Repaint();
            w.EnableRaisingEvents = true;
            themeWatcher = w;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException) { App.Log(ex); }
    }

    static void Repaint()
    {
        var accent = Accent();
        LampArray[] all;
        lock (gate)
        {
            if (accent == paintedWith) return;
            all = [.. arrays.Values];
        }
        foreach (var lamps in all) Paint(lamps, accent);
    }

    static string Accent()
    {
        try { return MenuModel.LoadTheme(MenuModel.CurrentThemeName()).Accent; }
        catch (IOException) { return ""; }
    }

    // Every lamp in the accent, at full brightness (the device's own brightness setting still
    // applies on top). A theme with no readable accent leaves the lights as they are.
    static void Paint(LampArray lamps, string accent)
    {
        if (LightColor.Parse(accent) is not { } c) return;
        try
        {
            lamps.BrightnessLevel = 1.0;
            lamps.SetColor(Windows.UI.Color.FromArgb(255, c.R, c.G, c.B));
            lock (gate) paintedWith = accent;
        }
        catch (Exception ex) { App.Log(ex); } // unplugged as it was painted
    }
}
