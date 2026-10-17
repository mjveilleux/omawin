using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OmawinMusic;

// mpv, the engine that plays: started hidden by the player (--serve) and driven over its JSON
// IPC pipe. It looks YouTube streams up itself (its ytdl hook runs our yt-dlp, with Bun), decodes
// with its own ffmpeg, plays to whatever speaker Windows is using (and follows a switch), and shows
// in Windows' media controls (media keys, lock screen, the bar's media provider). Windows' own
// MediaPlayer, used before, stopped playing YouTube's streams in October 2026: it opened them and
// then failed every track (SourceNotSupportedError 0xC00D0035), so it isn't used any more.
//
// Never call Command(wait: true) from an Event handler: answers arrive on the thread that raises
// events, so it would wait on itself.
sealed class Mpv
{
    static readonly string PipeName = Account.Pipe("omawin-mpv");
    readonly object gate = new();
    Process? proc;
    NamedPipeClientStream? pipe;
    StreamWriter? writer;
    int nextId = 1;
    readonly Dictionary<int, TaskCompletionSource<JsonNode?>> waiting = new();

    // Every event mpv sends (property-change, start-file, end-file, log-message, ...), raised on
    // the thread that reads the pipe.
    public event Action<JsonObject>? Event;
    // A fresh mpv is up: what it should watch and how loud it should be is set again here.
    public event Action? Started;
    // mpv went away on its own (it crashed, or was closed from outside).
    public event Action? Exited;

    public bool Running { get { lock (gate) return proc is { HasExited: false } && pipe is { IsConnected: true }; } }

    void Ensure()
    {
        lock (gate)
        {
            if (proc is { HasExited: false } && pipe is { IsConnected: true }) return;
            Start();
        }
        Started?.Invoke();
    }

    void Start()
    {
        StopStale();
        var info = new ProcessStartInfo(Exe()) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in Args()) info.ArgumentList.Add(a);
        proc = Process.Start(info) ?? throw new Exception("couldn't start mpv");
        // mpv opens its pipe a moment after it starts.
        var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        var until = DateTime.Now.AddSeconds(5);
        while (true)
        {
            try { client.Connect(200); break; }
            catch (TimeoutException)
            {
                if (proc.HasExited || DateTime.Now > until)
                {
                    client.Dispose();
                    try { proc.Kill(); } catch { }
                    throw new Exception("mpv started but never opened its pipe");
                }
            }
        }
        pipe = client;
        writer = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        var reader = new StreamReader(client, Encoding.UTF8);
        new Thread(() => Read(reader, client)) { IsBackground = true, Name = "mpv events" }.Start();
    }

    static IEnumerable<string> Args()
    {
        yield return "--no-config";            // a mpv.conf of your own stays out of it
        yield return "--idle=yes";             // stays up between playlists, waiting for the next
        yield return "--no-video";
        yield return "--force-window=no";
        yield return "--audio-display=no";     // no cover art window either
        yield return "--no-terminal";
        yield return "--media-controls=yes";   // media keys, lock screen, the bar's media provider
        yield return @"--input-ipc-server=\\.\pipe\" + PipeName;
        yield return "--ytdl-format=bestaudio/best";
        yield return "--script-opts=ytdl_hook-ytdl_path=" + YtDlp.Exe();
        // YouTube needs a JavaScript runtime: Bun (omawin's), named outright, Deno switched off.
        if (YtDlp.Bun() is { } bun)
        {
            yield return "--ytdl-raw-options-append=no-js-runtimes=";
            yield return "--ytdl-raw-options-append=js-runtimes=bun:" + bun;
        }
    }

    // An mpv left over from a player that didn't close cleanly (killed by a rebuild): asked to quit,
    // so two never play at once.
    static void StopStale()
    {
        if (!File.Exists(@"\\.\pipe\" + PipeName)) return;
        try
        {
            using var old = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
            old.Connect(500);
            using var w = new StreamWriter(old, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
            w.WriteLine("""{"command":["quit"]}""");
        }
        catch { }
        for (int i = 0; i < 20 && File.Exists(@"\\.\pipe\" + PipeName); i++) Thread.Sleep(50);
    }

    void Read(StreamReader reader, NamedPipeClientStream mine)
    {
        try
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (JsonNode.Parse(line) is not JsonObject msg) continue;
                if (msg["event"] != null)
                {
                    try { Event?.Invoke(msg); } catch (Exception ex) { Program.Log(ex); }
                    continue;
                }
                int id = msg["request_id"]?.GetValue<int>() ?? 0;
                TaskCompletionSource<JsonNode?>? answer;
                lock (waiting) waiting.Remove(id, out answer);
                if ((string?)msg["error"] == "success") answer?.TrySetResult(msg["data"]?.DeepClone());
                else answer?.TrySetException(new Exception("mpv: " + (string?)msg["error"]));
            }
        }
        catch { }
        lock (waiting)
        {
            foreach (var answer in waiting.Values) answer.TrySetException(new Exception("mpv went away"));
            waiting.Clear();
        }
        bool current;
        lock (gate) current = pipe == mine;
        if (current) Exited?.Invoke();
    }

    // One command (mpv's input commands, as JSON words). With wait, mpv's answer (its "data");
    // without, null at once. Starts mpv first if it isn't running.
    public JsonNode? Command(bool wait, params object?[] words)
    {
        Ensure();
        TaskCompletionSource<JsonNode?>? answer = null;
        lock (gate)
        {
            int id = nextId++;
            if (wait)
            {
                answer = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
                lock (waiting) waiting[id] = answer;
            }
            try { writer!.WriteLine(JsonSerializer.Serialize(new { command = words, request_id = id })); }
            catch (Exception ex) { throw new Exception("mpv went away: " + ex.Message); }
        }
        if (answer == null) return null;
        if (!answer.Task.Wait(TimeSpan.FromSeconds(5))) throw new Exception("mpv didn't answer");
        return answer.Task.GetAwaiter().GetResult();
    }

    public void Send(params object?[] words) => Command(false, words);

    // Closes mpv (the player quitting).
    public void Stop()
    {
        lock (gate)
        {
            try { writer?.WriteLine("""{"command":["quit"]}"""); } catch { }
            try { if (proc != null && !proc.WaitForExit(1500)) proc.Kill(); } catch { }
            pipe?.Dispose();
            pipe = null;
            writer = null;
            proc = null;
        }
    }

    // mpv.exe: on the PATH, where omawin's install puts it by hand (%LOCALAPPDATA%\Programs\mpv),
    // or where winget unpacked it (shinchiro.mpv; its link isn't always on the PATH).
    static string? exe;
    public static string Exe()
    {
        if (exe != null) return exe;
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var places = (Environment.GetEnvironmentVariable("Path") ?? "").Split(';')
            .Append(Path.Combine(local, "Programs", "mpv"))
            .Append(Path.Combine(local, "Microsoft", "WinGet", "Links"));
        foreach (var dir in places)
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), "mpv.exe");
                if (dir.Trim() != "" && File.Exists(candidate)) return exe = candidate;
            }
            catch { }
        }
        try
        {
            var found = Directory.GetDirectories(Path.Combine(local, "Microsoft", "WinGet", "Packages"), "shinchiro.mpv*")
                .SelectMany(d => Directory.GetFiles(d, "mpv.exe", SearchOption.AllDirectories)).FirstOrDefault();
            if (found != null) return exe = found;
        }
        catch { }
        throw new Exception("mpv isn't installed (omawin preset add music)");
    }
}
