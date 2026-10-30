using System.Runtime.InteropServices;

namespace SystemMenu;

// Stay awake (`omawin toggle stay-awake`), like Omarchy's Trigger › Toggle › Idle / caffeine:
// while it's on, Windows neither sleeps nor turns the screen off. The menu app's UI thread
// holds SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED),
// which lasts as long as that thread does: it's on until toggled off or the menu app stops
// (a rebuild, a sign-out), and a fresh start is always off.
//
// %LOCALAPPDATA%\launcher\stay-awake exists while it's on, for the bar to show it.
public static class StayAwake
{
    const uint ES_CONTINUOUS = 0x80000000, ES_SYSTEM_REQUIRED = 0x1, ES_DISPLAY_REQUIRED = 0x2;
    [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint flags);

    public static string StateFile => Path.Combine(MenuModel.StateDir, "stay-awake");
    public static bool On { get; private set; }

    // At start-up: a file left by a copy that stopped while it was on isn't true any more.
    public static void Reset()
    {
        try { File.Delete(StateFile); } catch { }
    }

    // Call on the UI thread (it lives as long as the app), never on a worker that ends.
    public static void Toggle()
    {
        On = !On;
        SetThreadExecutionState(On ? ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED : ES_CONTINUOUS);
        try
        {
            if (On) { Directory.CreateDirectory(MenuModel.StateDir); File.WriteAllText(StateFile, DateTime.Now.ToString("s")); }
            else File.Delete(StateFile);
        }
        catch (Exception ex) { App.Log(ex); }
        Snip.Say(On ? "Stay awake: on · no sleep, screen stays on" : "Stay awake: off");
    }
}
