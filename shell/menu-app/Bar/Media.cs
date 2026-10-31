using Windows.Media.Control;

namespace SystemMenu;

// What's playing, for the bar's media widget (roadmap 1.6): Windows' own media controls
// (GlobalSystemMediaTransportControlsSessionManager), the session Windows' media flyout shows,
// so any app that reports to it (Spotify, a browser's video, the Media Player) appears. Event
// driven: Windows says when the session, its song or its state changes; nothing is polled.
// One for every bar (each monitor's widget reads Now); Changed asks the bars to redraw.
public static class Media
{
    public sealed record State(string Title, string Artist, string App, bool Playing, bool CanToggle, bool CanNext, bool CanPrevious);

    public static event Action? Changed;
    public static State? Now { get; private set; }

    static GlobalSystemMediaTransportControlsSessionManager? manager;
    static GlobalSystemMediaTransportControlsSession? session;
    static bool started;
    static int reading;
    static readonly object gate = new();

    public static void Start()
    {
        if (started) return;
        started = true;
        _ = Task.Run(async () =>
        {
            try
            {
                manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                manager.CurrentSessionChanged += (m, _) => Follow(m.GetCurrentSession());
                Follow(manager.GetCurrentSession());
            }
            catch (Exception ex) { App.Log(ex); } // no media controls (a Windows without them): the widget stays hidden
        });
    }

    // Listen to the session Windows shows now, and only to it.
    static void Follow(GlobalSystemMediaTransportControlsSession? next)
    {
        lock (gate)
        {
            if (ReferenceEquals(session, next)) return;
            if (session != null)
            {
                session.MediaPropertiesChanged -= OnProperties;
                session.PlaybackInfoChanged -= OnPlayback;
            }
            session = next;
            if (session != null)
            {
                session.MediaPropertiesChanged += OnProperties;
                session.PlaybackInfoChanged += OnPlayback;
            }
        }
        _ = Read();
    }

    static void OnProperties(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) => _ = Read();
    static void OnPlayback(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) => _ = Read();

    // The session's song and state. A newer read wins over one still waiting for its properties.
    static async Task Read()
    {
        int token = Interlocked.Increment(ref reading);
        State? state = null;
        try
        {
            var s = session;
            if (s != null)
            {
                var info = s.GetPlaybackInfo();
                var status = info.PlaybackStatus;
                if (status is not (GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed or GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped))
                {
                    var props = await s.TryGetMediaPropertiesAsync();
                    var c = info.Controls;
                    bool playing = status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                    state = new State(props?.Title ?? "", props?.Artist ?? "", s.SourceAppUserModelId ?? "", playing,
                        playing ? c.IsPauseEnabled || c.IsPlayPauseToggleEnabled : c.IsPlayEnabled || c.IsPlayPauseToggleEnabled,
                        c.IsNextEnabled, c.IsPreviousEnabled);
                    if (state.Title.Length == 0 && state.Artist.Length == 0 && MediaText.AppName(state.App) == null) state = null; // nothing to show
                    // omawin's own player (its mpv) has the record in the bar already: not twice.
                    else if (MediaText.AppName(state.App) == "Mpv" && OwnPlayerRunning()) state = null;
                }
            }
        }
        catch (Exception ex) { App.Log(ex); } // the app went away mid-read: shown as nothing playing
        if (token != Volatile.Read(ref reading) || Equals(state, Now)) return;
        Now = state;
        Changed?.Invoke();
    }

    static bool OwnPlayerRunning()
    {
        var found = System.Diagnostics.Process.GetProcessesByName("omawin-music");
        foreach (var p in found) p.Dispose();
        return found.Length > 0;
    }

    public static void Toggle() => Do(s => s.TryTogglePlayPauseAsync());
    public static void Next() => Do(s => s.TrySkipNextAsync());
    public static void Previous() => Do(s => s.TrySkipPreviousAsync());

    static void Do(Func<GlobalSystemMediaTransportControlsSession, Windows.Foundation.IAsyncOperation<bool>> act)
    {
        var s = session;
        if (s == null) return;
        _ = Task.Run(async () =>
        {
            try { await act(s); }
            catch (Exception ex) { App.Log(ex); }
        });
    }
}
