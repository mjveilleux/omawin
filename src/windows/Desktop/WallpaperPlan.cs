namespace Omawin.Windows;

// Which picture goes on which monitor (8.8). Pure: Wallpaper.cs reads the monitors and sets them.
public sealed record MonitorPlace(string Id, int Left, int Top);

public static class WallpaperPlan
{
    // Monitor i (left to right, then top to bottom) gets pictures[i % count]: with more monitors
    // than pictures they repeat; with fewer, the first ones are used.
    public static List<(string Monitor, string Picture)> Assign(IEnumerable<MonitorPlace> monitors, IReadOnlyList<string> pictures)
    {
        if (pictures.Count == 0) return [];
        return monitors.OrderBy(m => m.Left).ThenBy(m => m.Top).Select((m, i) => (m.Id, pictures[i % pictures.Count])).ToList();
    }
}
