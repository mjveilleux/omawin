// Monitor profiles that follow the dock (roadmap 8.7): when monitors come or go and a layout is
// saved (omawin display save: %LOCALAPPDATA%\launcher\displays.txt), `omawin display apply --auto`
// puts this set's back (src/windows: QueryDisplayConfig / SetDisplayConfig). Windows' own
// display-change event, no polling; a dock brings its monitors one by one, so it waits for them
// to settle. Applying changes the displays again, and that second run finds nothing to do.

namespace SystemMenu;

public static class DisplayWatch
{
    static Timer? settle;
    static readonly object gate = new();
    static string Saved => Path.Combine(MenuModel.StateDir, "displays.txt");

    public static void Start() => Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) => Changed();

    static void Changed()
    {
        if (!File.Exists(Saved)) return;
        lock (gate)
        {
            settle?.Dispose();
            settle = new Timer(_ => MenuModel.Omawin("display", "apply", "--auto"), null, 2000, Timeout.Infinite);
        }
    }
}
