using System.Collections.Concurrent;
using Microsoft.UI.Dispatching;

namespace SystemMenu;

// Quick Look in Explorer (roadmap 8.4): Space on a file in Explorer's file list shows it, as on
// a Mac; Space again or Esc closes it. The keyboard hook (KeyHook.cs, QuickLookKey in
// KeyRules.cs) decides in microseconds whether Space is meant for it and posts here; this reads
// Explorer's selection on a thread of its own (ExplorerSelection.cs: calls into Explorer's
// process take a few ms each, too slow for the hook), then the UI thread opens the window
// (QuickLookWindow.cs). Nothing selected after all: the Space goes on to Explorer as if never
// taken. explorer.quick-look in the settings catalog turns it off.
public static class QuickLook
{
    // The menu app's UI thread (App.xaml.cs sets it at start).
    public static DispatcherQueue? Ui { get; set; }

    static readonly BlockingCollection<(nint Explorer, nint View)> asks = new(boundedCapacity: 4);
    static Thread? worker;
    static readonly object gate = new();

    // From the hook: never waits (a full queue drops the ask; a fifth Space at once is a mistake).
    public static void Request(nint explorer, nint view)
    {
        lock (gate)
        {
            if (worker == null)
            {
                // A single-threaded apartment, as Explorer's automation expects; started once.
                worker = new Thread(Work) { IsBackground = true, Name = "quick-look" };
                worker.SetApartmentState(ApartmentState.STA);
                worker.Start();
            }
        }
        asks.TryAdd((explorer, view));
    }

    static void Work()
    {
        foreach (var (explorer, view) in asks.GetConsumingEnumerable())
        {
            List<string> paths;
            try { paths = ExplorerSelection.Of(explorer, view); }
            catch (Exception ex) { App.Log(ex); paths = []; }
            if (paths.Count == 0 || Ui == null)
            {
                KeyHook.Press(QuickLookKey.VkSpace); // what Space does in Explorer with nothing selected
                continue;
            }
            Ui.TryEnqueue(() =>
            {
                try { QuickLookWindow.Open(paths, explorer); }
                catch (Exception ex) { App.Log(ex); }
            });
        }
    }
}
