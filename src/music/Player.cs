using System.Text.Json.Nodes;

namespace OmawinMusic;

// Plays a playlist through mpv (Mpv.cs). The whole playlist is handed to mpv as links; mpv looks
// each YouTube video's stream up only when it's about to play, so a playlist of hundreds starts at
// once, and a stream that has expired is simply looked up again. What's playing comes back as
// mpv's events (track, pause, position), kept here for the bar (State). Media keys and the lock
// screen come with mpv (Windows' media controls).
//
// mpv is never called while gate is held: its events take gate on mpv's reader thread, and a call
// that waits for an answer would wait behind them.
//
// A Suno playlist plays in Suno's own page instead (SunoEngine.cs, SunoHost.cs: Suno's audio is
// encrypted and only its page plays it): while one plays, the controls, the volume and ducking go
// there, and what's playing comes from its reports.
public class Player
{
    readonly Mpv mpv = new();
    readonly SunoEngine suno = new();
    bool onSuno;   // a Suno playlist is what's playing
    string sunoId = "", sunoTitle = "", sunoArtist = "", sunoArt = "";
    bool sunoPlaying;
    double sunoPosition, sunoDuration;
    readonly object gate = new();
    Collection? playlist;
    string? error;
    DateTime lastActive = DateTime.Now;
    bool shuffle;
    double volume = 1; // what you set; ducking lowers what's heard below it
    double duck = 1;   // 1, or down to DuckTo while you dictate

    // What mpv last said.
    string? path;      // the current track's link (null between tracks)
    int index = -1;    // that track in the playlist (the last one, between tracks)
    bool paused, idle = true, loading, buffering;
    bool sounded;      // this track has played (playback-restart came): an end before that is a failure
    double position, duration;
    // Tracks that wouldn't play, one after another: after a few, stop and say why, rather than
    // going round a playlist that can't play at all.
    int failedInRow;
    const int GiveUpAfter = 3;
    bool gaveUp;       // stopped for that: stays stopped until the next play
    string? problem;   // the last thing mpv or yt-dlp complained about (why a track failed)

    public event Action? Changed;

    public Player()
    {
        mpv.Event += OnEvent;
        mpv.Started += () =>
        {
            foreach (var name in new[] { "path", "pause", "idle-active", "time-pos", "duration", "paused-for-cache" })
                mpv.Send("observe_property", 1, name);
            mpv.Send("request_log_messages", "warn");
            mpv.Send("set_property", "volume", Loudness());
        };
        suno.Event += OnSunoReport;
        mpv.Exited += () =>
        {
            lock (gate)
            {
                idle = true;
                loading = false;
                if (playlist != null) error = "The player closed unexpectedly: press play to start again";
            }
            Changed?.Invoke();
        };
    }

    public bool Playing { get { lock (gate) return onSuno ? sunoPlaying : playlist != null && !idle && !paused; } }

    // Nothing has played for this long: the background copy can quit.
    public TimeSpan Idle => Playing ? TimeSpan.Zero : DateTime.Now - lastActive;

    static string Link(Track t) => t.Audio != "" ? t.Audio : t.Url;

    public void Play(Collection p, int start)
    {
        if (p.Kind == Library.Suno) { PlaySuno(p, start); return; }
        if (p.Tracks.Count == 0) throw new Exception(p.Title + " has no tracks");
        bool wasSuno;
        lock (gate) { wasSuno = onSuno; onSuno = false; }
        if (wasSuno) suno.SendIfRunning(new { cmd = "pause" });
        bool shuffled;
        lock (gate)
        {
            if (start < 0 || start >= p.Tracks.Count) start = shuffle ? Random.Shared.Next(p.Tracks.Count) : 0;
            playlist = p;
            index = start;
            error = null;
            problem = null;
            failedInRow = 0;
            gaveUp = false;
            loading = true;
            idle = false;
            shuffled = shuffle && p.Kind != Station.Kind; // the station is in its own order
        }
        mpv.Send("stop"); // clears mpv's playlist
        // Music and radio go round again at the end; podcasts stop; the station never ends, it
        // grows (Refill).
        mpv.Send("set_property", "loop-playlist", p.Kind is Library.Podcasts or Station.Kind ? "no" : "inf");
        // A track you rated 1 star never plays again, unless it's the one you picked.
        var ratings = Ratings.All();
        var chosen = p.Tracks[start];
        var queue = p.Tracks.Where(t => t == chosen || !(ratings.TryGetValue(Ratings.Key(t), out var r) && r.Stars == 1)).ToList();
        foreach (var t in queue) mpv.Send("loadfile", Link(t), "append");
        int at = queue.IndexOf(chosen);
        if (shuffled)
        {
            mpv.Send("playlist-shuffle");
            // Where the chosen track landed in the shuffled order.
            var want = Link(chosen);
            if (mpv.Command(true, "get_property", "playlist") is JsonArray entries)
                for (int i = 0; i < entries.Count; i++)
                    if ((string?)entries[i]?["filename"] == want) { at = i; break; }
        }
        mpv.Send("playlist-play-index", at);
        mpv.Send("set_property", "pause", false);
        Changed?.Invoke();
    }

    // --- Suno -------------------------------------------------------------------------------------
    // A Suno playlist (or one song) plays in the Suno window: from the top, or from the song picked.
    void PlaySuno(Collection p, int start)
    {
        string url;
        lock (gate)
        {
            if (start < 0 || start >= p.Tracks.Count) start = 0;
            url = start > 0 ? p.Tracks[start].Url : p.Source;
            playlist = p;
            index = start;
            onSuno = true;
            error = null;
            gaveUp = false;
            loading = true;
            sunoPlaying = false;
            sunoPosition = sunoDuration = 0;
            var t = p.Tracks.ElementAtOrDefault(start);
            (sunoId, sunoTitle, sunoArtist, sunoArt) = (t?.Id ?? "", t?.Title ?? "", t?.Artist ?? "", t?.Thumb ?? "");
        }
        if (mpv.Running) mpv.Send("stop");
        suno.Send(new { cmd = "open", url });
        suno.Send(new { cmd = "gain", to = Loudness() / 100 });
        Changed?.Invoke();
    }

    // The Suno window beside the bar's panel (its Suno tab), in screen pixels; or out of sight
    // (it plays on).
    public void ShowSuno(int x, int y, int w, int h) => suno.Send(new { cmd = "show", x, y, w, h });
    public void HideSuno() => suno.SendIfRunning(new { cmd = "hide" });
    // Suno's create page in that window (the bar's Make a song): you make songs there, in Suno's
    // own form, with your own credits; what's playing plays on.
    public void SunoCreate() => suno.Send(new { cmd = "go", url = "https://suno.com/create" });

    void OnSunoReport(JsonObject e)
    {
        switch ((string?)e["ev"])
        {
            case "state":
                lock (gate)
                {
                    lastActive = DateTime.Now;
                    sunoId = (string?)e["id"] ?? "";
                    sunoTitle = (string?)e["title"] ?? "";
                    sunoArtist = (string?)e["artist"] ?? "";
                    sunoArt = (string?)e["art"] ?? "";
                    sunoPlaying = e["playing"]?.GetValue<bool>() ?? false;
                    sunoPosition = e["position"]?.GetValue<double>() ?? 0;
                    sunoDuration = e["duration"]?.GetValue<double>() ?? 0;
                    if (sunoPlaying) loading = false;
                    if (playlist?.Kind == Library.Suno && sunoId != "")
                    {
                        int found = playlist.Tracks.FindIndex(t => t.Id == sunoId);
                        if (found >= 0) index = found;
                    }
                    if (!onSuno) return; // played in the window itself while something else is on: not ours to show
                }
                break;
            case "log":
                Program.Log(new Exception("suno: " + (string?)e["text"]));
                return;
            case "noplay":
                lock (gate)
                {
                    loading = false;
                    error = "Suno's page had nothing to play (a private playlist? sign in in the Suno tab)";
                }
                break;
            default:
                return;
        }
        Changed?.Invoke();
    }

    // --- your station and your ratings ----------------------------------------------------------
    // Your station (Station.cs): songs or jams, or, unsaid, the kind of what's playing.
    public void PlayStation(string? flavor)
    {
        Track? now;
        lock (gate) now = Current();
        var station = Station.Start(Station.Flavor(flavor, now));
        Play(station, 0);
    }

    // Stars for what's playing (1-5; 0 takes the rating away). One star skips it at once.
    public void Rate(int stars)
    {
        Track? now;
        lock (gate) now = Current();
        if (now == null) throw new Exception("nothing is playing to rate");
        Ratings.Rate(now, stars);
        if (stars == 1) Next();
        Changed?.Invoke();
    }

    // What's playing. On Suno: what its page says (it may have moved past the playlist we know).
    Track? Current() => onSuno && sunoTitle != ""
        ? new Track { Id = sunoId, Url = sunoId != "" ? "https://suno.com/song/" + sunoId : "", Title = sunoTitle, Artist = sunoArtist, Duration = sunoDuration, Thumb = sunoArt }
        : playlist != null && index >= 0 && index < playlist.Tracks.Count ? playlist.Tracks[index] : null;

    // The station keeps a few tracks ahead: when the one playing is near the end of the queue,
    // more are found (a seed's radio, ~5 s, off mpv's thread) and added to mpv's playlist.
    bool refilling;
    void RefillIfLow()
    {
        Collection? p;
        HashSet<string> recent;
        lock (gate)
        {
            if (playlist is not { Kind: Station.Kind } || refilling || index < playlist.Tracks.Count - 3) return;
            refilling = true;
            p = playlist;
            recent = p.Tracks.Select(Ratings.Key).TakeLast(200).ToHashSet();
        }
        Task.Run(() =>
        {
            try
            {
                var more = Station.More(p.Source, recent, 6);
                lock (gate)
                {
                    if (playlist != p) return; // something else is playing now
                    p.Tracks.AddRange(more);
                }
                foreach (var t in more) mpv.Send("loadfile", Link(t), "append");
                if (more.Count == 0) Program.Log(new Exception("station: found nothing new to play"));
                Changed?.Invoke();
            }
            catch (Exception ex) { Program.Log(ex); }
            finally { lock (gate) refilling = false; }
        });
    }

    // Stopped (given up, or mpv closed): play starts the playlist again, on the track it was on.
    bool Restart()
    {
        Collection? p;
        int at;
        bool running = mpv.Running;
        lock (gate)
        {
            if (playlist == null || (running && !idle)) return false;
            p = playlist;
            at = Math.Max(0, index);
        }
        Play(p, at);
        return true;
    }

    bool OnSuno { get { lock (gate) return onSuno; } }

    public void Toggle()
    {
        lock (gate) if (playlist == null) return;
        if (OnSuno) { suno.Send(new { cmd = "toggle" }); return; }
        if (!Restart()) mpv.Send("cycle", "pause");
    }

    public void Pause()
    {
        if (OnSuno) suno.SendIfRunning(new { cmd = "pause" });
        else if (mpv.Running) mpv.Send("set_property", "pause", true);
    }

    public void Resume()
    {
        lock (gate) if (playlist == null) return;
        if (OnSuno) { suno.Send(new { cmd = "play" }); return; }
        if (!Restart()) mpv.Send("set_property", "pause", false);
    }

    public void Next()
    {
        if (OnSuno) { suno.SendIfRunning(new { cmd = "next" }); return; }
        if (!mpv.Running) return;
        mpv.Send("playlist-next");
        mpv.Send("set_property", "pause", false);
    }

    // Back: to the start of this track if it's past a few seconds, else the one before.
    public void Previous()
    {
        if (OnSuno) { suno.SendIfRunning(new { cmd = "prev" }); return; } // Suno's own back does the same
        if (!mpv.Running) return;
        double at;
        lock (gate) at = position;
        if (at > 4) mpv.Send("seek", 0, "absolute");
        else mpv.Send("playlist-prev");
        mpv.Send("set_property", "pause", false);
    }

    public void Seek(double seconds)
    {
        if (OnSuno) { suno.SendIfRunning(new { cmd = "seek", to = Math.Max(0, seconds) }); return; }
        if (!mpv.Running) return;
        lock (gate) if (playlist?.Kind == Library.Radio) return; // live: there is nowhere to seek to
        mpv.Send("seek", Math.Max(0, seconds), "absolute");
    }

    public void Shuffle()
    {
        bool on, live;
        lock (gate)
        {
            shuffle = !shuffle;
            on = shuffle;
            live = playlist != null;
        }
        if (live && !OnSuno && mpv.Running) mpv.Send(on ? "playlist-shuffle" : "playlist-unshuffle"); // Suno has its own
        Changed?.Invoke();
    }

    public void SetVolume(double v)
    {
        lock (gate) volume = Math.Clamp(v, 0, 1);
        ApplyVolume();
        Changed?.Invoke();
    }

    // mpv's volume (0-100): what you set, lowered while you dictate.
    double Loudness() { lock (gate) return Math.Round(volume * duck * 100, 1); }

    // Both engines hear it: mpv's volume, and the Suno page's gain (Suno's own volume times it).
    void ApplyVolume()
    {
        var loud = Loudness();
        if (mpv.Running) mpv.Send("set_property", "volume", loud);
        suno.SendIfRunning(new { cmd = "gain", to = loud / 100 });
    }

    // While you dictate (Handy on Win+H, or Windows voice typing), the music drops to 10% so it
    // doesn't distract, quickly; when you stop, it rises back over a few seconds. Checked ten
    // times a second while playing (Dictation.cs reads the registry); while paused or stopped,
    // nothing is heard, so it only un-ducks and looks again twice a second.
    const double DuckTo = 0.1;
    public void DuckLoop()
    {
        while (true)
        {
            if (!Playing)
            {
                if (duck < 1) { duck = 1; ApplyVolume(); }
                Thread.Sleep(500);
                continue;
            }
            Thread.Sleep(100);
            double target = Dictation.Listening() ? DuckTo : 1;
            if (Math.Abs(duck - target) < 0.001) continue;
            duck = target < duck ? Math.Max(target, duck - 0.25) : Math.Min(target, duck + 0.035); // ~0.4 s down, ~2.5 s up
            ApplyVolume();
        }
    }

    // Closes mpv: the player is quitting.
    public void Shutdown()
    {
        mpv.Stop();
        suno.Stop();
    }

    // --- what mpv says ---------------------------------------------------------------------------
    void OnEvent(JsonObject e)
    {
        bool changed = true;
        bool giveUp = false;
        switch ((string?)e["event"])
        {
            case "property-change":
            {
                var data = e["data"] as JsonValue;
                lock (gate)
                {
                    lastActive = DateTime.Now;
                    switch ((string?)e["name"])
                    {
                        case "path":
                            if (gaveUp || onSuno) break; // the one after the last failure, already being stopped; or Suno's turn
                            path = data?.GetValue<string>();
                            if (path != null && playlist != null)
                            {
                                int found = playlist.Tracks.FindIndex(t => Link(t) == path);
                                if (found >= 0) index = found;
                            }
                            break;
                        case "pause": paused = data?.GetValue<bool>() ?? false; break;
                        case "idle-active": idle = data?.GetValue<bool>() ?? true; break;
                        case "paused-for-cache": buffering = data?.GetValue<bool>() ?? false; break;
                        case "duration": duration = data?.GetValue<double>() ?? 0; break;
                        case "time-pos":
                            position = data?.GetValue<double>() ?? 0;
                            changed = false; // the bar hears the position once a second (Program.Serve)
                            break;
                    }
                }
                break;
            }
            case "start-file":
                lock (gate) { loading = true; sounded = false; position = 0; duration = 0; }
                break;
            case "playback-restart":
                // Sound is coming: this track works.
                lock (gate) { loading = false; sounded = true; failedInRow = 0; error = null; problem = null; }
                break;
            // A failed track: an error, or an "end" before it made any sound (a stream that broke
            // off while it opened looks like the end of the file to mpv).
            case "end-file" when (string?)e["reason"] == "error" || ((string?)e["reason"] == "eof" && !sounded):
                lock (gate)
                {
                    var why = problem ?? (string?)e["file_error"] ?? "it ended before playing";
                    var title = playlist != null && index >= 0 && index < playlist.Tracks.Count ? playlist.Tracks[index].Title : path;
                    Program.Log(new Exception($"couldn't play {title}: {why}"));
                    failedInRow++;
                    giveUp = failedInRow >= Math.Min(GiveUpAfter, playlist?.Tracks.Count ?? 1);
                    error = giveUp
                        ? $"Stopped: {failedInRow} tracks in a row wouldn't play ({why})"
                        : $"Couldn't play {title} ({why})";
                    problem = null;
                    if (giveUp) { gaveUp = true; loading = false; }
                }
                break;
            case "log-message":
            {
                // ytdl_hook passes on yt-dlp's own "ERROR: ..." line, the best reason a track
                // failed; its summary after it ("youtube-dl failed: ...") says less, so it only
                // counts when nothing better came first.
                var text = ((string?)e["text"])?.Trim();
                if (string.IsNullOrEmpty(text)) return;
                var reason = YtDlp.Reason(text);
                lock (gate)
                {
                    if (reason != null) problem = reason;
                    else problem ??= text.Length > 120 ? text[..120] + "…" : text;
                }
                return;
            }
            default:
                return;
        }
        // Stopping from here: a plain Send, which doesn't wait, so it's safe on this thread.
        if (giveUp) mpv.Send("stop");
        else RefillIfLow();
        if (changed) Changed?.Invoke();
    }

    // What the bar shows: written as one JSON line to every watcher.
    public object State()
    {
        lock (gate)
        {
            var t = Current();
            string state = playlist == null || gaveUp ? "stopped"
                : onSuno ? (sunoPlaying ? "playing" : loading ? "loading" : "paused")
                : idle && !loading ? "stopped"
                : paused ? "paused"
                : loading || buffering || path == null ? "loading"
                : "playing";
            double length = onSuno ? (sunoDuration > 0 ? sunoDuration : t?.Duration ?? 0) : duration > 0 ? duration : t?.Duration ?? 0;
            double at = onSuno ? sunoPosition : position;
            return new
            {
                running = true,
                state,
                live = playlist?.Kind == Library.Radio, // a station: no end, no position worth showing
                kind = playlist?.Kind,
                playlist = playlist?.Slug,
                playlistTitle = playlist?.Title,
                index = playlist == null ? -1 : index,
                count = playlist?.Tracks.Count ?? 0,
                id = t?.Id,
                title = t?.Title,
                artist = t?.Artist,
                published = t?.Published,
                thumb = t?.Thumb,
                stars = t == null ? 0 : Ratings.Stars(t), // your rating of it (0: none)
                station = playlist?.Kind == Station.Kind ? playlist.Source : null, // songs | jams
                position = Math.Round(at, 1),
                duration = Math.Round(length, 1),
                shuffle,
                volume = Math.Round(volume, 2),
                ducked = duck < 1,
                error,
            };
        }
    }
}
