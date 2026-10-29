using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace OmawinMusic;

// omawin-music: the music player in the bar. One exe, three roles:
//   omawin-music --serve           the player: plays in the background, no window; started by
//                                  the first command that needs it, quits after 20 idle minutes
//   omawin-music watch             the bar's feed (zebar/tokyo-islands/bar.html): one line each
//                                    S {json}   what's playing (S {"running":false} when nothing runs)
//                                    P [json]   the playlists, again whenever they change
//                                    L 0.42     the speakers' level, ~25 a second while playing
//                                    O open     open the panel / O close: close it (Win+M)
//   omawin-music <command>         everything else, one JSON line back:
//   omawin-music --version         the release this exe is from ((dev) built off a checkout)
//     add <url>                    music: a playlist, a YouTube mix or one long video, into ~/Music/omawin
//     add podcasts <url>           a podcast: its RSS feed, Apple Podcasts link or YouTube playlist
//     add radio <url> [name]       a station: one live stream, played from where it is (radio/)
//     add [suno] <suno link>       a Suno playlist or song (suno/): plays in Suno's own page
//     suno-show <x> <y> <w> <h>    the Suno window there (screen pixels; the bar's Suno tab) | suno-hide
//     suno-create                  Suno's create page in that window (make a song there)
//     list | tracks <slug> | refresh <slug> | rename <slug> <name> | remove <slug>
//     play <slug> [n]              play it from track n (0-based; none: the start, or random on shuffle)
//     station [songs|jams]         your station: endless, built from your ratings (Station.cs)
//     rate <1-5>                   stars for what's playing (0 takes them away; 1 skips it, for good)
//     open                         the bar's panel on the focused monitor, or closed again if
//                                  one is open (Win+M); told to the bars' feeds, not the player
//     toggle | pause | resume | next | prev | seek <seconds> | shuffle | volume <0..1> | state | quit
// The player listens on the pipe \\.\pipe\omawin-music, one JSON array of words per line.
public static class Program
{
    static readonly string PipeName = Account.Pipe("omawin-music");
    static readonly string LogFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "launcher", "music.log");
    static readonly TextWriter Out = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };

    // The release this exe is from (omawin-music --version): CI passes it to the build
    // (-p:OmawinRelease=<tag>, Music.csproj); built off a checkout it says (dev).
    public static readonly string Version =
        typeof(Program).Assembly.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "(dev)";

    public static void Log(Exception ex)
    {
        try { File.AppendAllText(LogFile, $"{DateTime.Now:s} {ex.Message}\n"); } catch { }
    }

    static int Main(string[] args)
    {
        YtDlp.FreshPath();
        var cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "state";
        try
        {
            switch (cmd)
            {
                case "--version":
                    // Before --serve and the default: --version must never start the player.
                    Out.WriteLine("omawin-music " + Version);
                    return 0;
                case "--serve": Serve(); return 0;
                case "--suno": SunoHost.Run(); return 0; // the Suno window (suno/SunoHost.cs), started by the player
                case "watch": Watch.Run(Out); return 0;
                case "add":
                case "refresh":
                {
                    bool podcast = args.ElementAtOrDefault(1) is "podcast" or "podcasts";
                    bool radio = args.ElementAtOrDefault(1) is "radio";
                    bool sunoKind = args.ElementAtOrDefault(1) is "suno";
                    var at = podcast || radio || sunoKind ? 2 : 1;
                    var target = args.ElementAtOrDefault(at) ?? throw new Exception(cmd + (radio ? " radio <url> [name]" : " <url>"));
                    // A Suno link is a Suno playlist, whichever way it's added.
                    if (!podcast && !radio && SunoLinks.Handles(target)) sunoKind = true;
                    var p = cmd == "refresh" ? Library.Refresh(target)
                        : radio ? Library.AddRadio(target, string.Join(' ', args.Skip(at + 1)))
                        : Library.Add(podcast ? Library.Podcasts : sunoKind ? Library.Suno : Library.Music, target);
                    var did = cmd == "add" ? "Added" : "Refreshed";
                    Reply(new { ok = true, slug = p.Slug, kind = p.Kind, title = p.Title, count = p.Tracks.Count },
                        radio ? $"{did} {p.Title} (a live stream)"
                              : $"{did} {p.Title}: {p.Tracks.Count} track{(p.Tracks.Count == 1 ? "" : "s")}");
                    return 0;
                }
                case "list":
                    Reply(Summaries(), string.Join('\n', Library.All().Select(p => $"{p.Kind,-8}  {p.Slug}  ({p.Tracks.Count})  {p.Title}")));
                    return 0;
                case "tracks":
                {
                    var p = Library.Get(args.ElementAtOrDefault(1) ?? "") ?? throw new Exception("no playlist " + args.ElementAtOrDefault(1));
                    Reply(p.Tracks, string.Join('\n', p.Tracks.Select((t, i) => $"{i,4}  {t.Title}")));
                    return 0;
                }
                case "rename":
                {
                    var p = Library.Rename(args.ElementAtOrDefault(1) ?? "", string.Join(' ', args.Skip(2)));
                    Reply(new { ok = true, slug = p.Slug, title = p.Title }, "Renamed to " + p.Title);
                    return 0;
                }
                case "remove":
                    Library.Remove(args.ElementAtOrDefault(1) ?? "");
                    Reply(new { ok = true }, "Removed");
                    return 0;
                case "open":
                {
                    // Never starts the player: opening the panel shouldn't leave one idling.
                    var done = Watch.RequestOpen();
                    // The native bar (the default, docs/native-bar.md) has no player panel yet: say so
                    // where you'll see it (Win+M runs this hidden), through the menu app's note.
                    if (done == null) Say("The music player is in Zebar's bar for now: omawin bar zebar");
                    Reply(new { ok = done != null, did = done }, done ?? "No bar is running");
                    return 0;
                }
                default:
                    var answer = Send(args, start: cmd is not ("state" or "quit" or "pause"));
                    Out.WriteLine(answer ?? """{"running":false}""");
                    return 0;
            }
        }
        catch (Exception ex)
        {
            Reply(new { ok = false, error = ex.Message }, ex.Message);
            return 1;
        }
    }

    // The menu app's note at the bottom of the screen (SystemMenu --say): handed to its running copy.
    static void Say(string text)
    {
        try
        {
            var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "system-menu", "SystemMenu.exe");
            if (File.Exists(exe)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { ArgumentList = { "--say", text }, UseShellExecute = false });
        }
        catch { }
    }

    // JSON for the bar; a plain sentence when you typed it in a terminal.
    static void Reply(object json, string text) =>
        Out.WriteLine(Console.IsOutputRedirected ? JsonSerializer.Serialize(json, Library.Json).ReplaceLineEndings("") : text);

    public static object Summaries() => Library.All().Select(p => new
    {
        kind = p.Kind,
        slug = p.Slug,
        title = p.Title,
        count = p.Tracks.Count,
        thumb = p.Image,
        updated = p.Updated,
        latest = p.Tracks.Max(t => t.Published),
    }).ToList();

    public static bool PlayerRuns() => File.Exists(@"\\.\pipe\" + PipeName);

    // --- the client ------------------------------------------------------------------------
    static string? Send(string[] words, bool start)
    {
        if (!PlayerRuns())
        {
            if (!start) return null;
            StartPlayer();
            for (int i = 0; i < 100 && !PlayerRuns(); i++) Thread.Sleep(50);
        }
        using var pipe = Connect(3000);
        if (pipe == null) return null;
        var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
        writer.WriteLine(JsonSerializer.Serialize(words));
        return new StreamReader(pipe, Encoding.UTF8).ReadLine();
    }

    public static NamedPipeClientStream? Connect(int timeout)
    {
        var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
        try { pipe.Connect(timeout); return pipe; }
        catch { pipe.Dispose(); return null; }
    }

    // The player is started out of whatever job its starter runs in (Zebar's commands, a
    // terminal), so it keeps playing when that closes.
    [StructLayout(LayoutKind.Sequential)]
    struct StartupInfo { public int cb; IntPtr a, b, c; int d, e, f, g, h, i, j, k; short l, m; IntPtr n, o, p, q; }
    [StructLayout(LayoutKind.Sequential)]
    struct ProcessInfo { public IntPtr Process, Thread; public int ProcessId, ThreadId; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateProcess(string? app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags,
        IntPtr env, string? dir, ref StartupInfo si, out ProcessInfo pi);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    static void StartPlayer()
    {
        const uint NoWindow = 0x08000000, Breakaway = 0x01000000;
        var exe = Environment.ProcessPath!;
        var si = new StartupInfo { cb = Marshal.SizeOf<StartupInfo>() };
        foreach (var flags in new[] { NoWindow | Breakaway, NoWindow })
        {
            if (CreateProcess(exe, new StringBuilder($"\"{exe}\" --serve"), IntPtr.Zero, IntPtr.Zero, false, flags,
                    IntPtr.Zero, Path.GetDirectoryName(exe), ref si, out var pi))
            {
                CloseHandle(pi.Process);
                CloseHandle(pi.Thread);
                return;
            }
        }
        throw new Exception("couldn't start the player");
    }

    // --- the player --------------------------------------------------------------------------
    static void Serve()
    {
        using var mutex = new Mutex(true, @"Local\omawin-music", out bool first);
        if (!first) return;
        var player = new Player();
        var watchers = new List<StreamWriter>();
        var quit = new ManualResetEventSlim();

        void Push()
        {
            var line = JsonSerializer.Serialize(player.State());
            lock (watchers)
                foreach (var w in watchers.ToList())
                    try { w.WriteLine(line); } catch { watchers.Remove(w); }
        }
        player.Changed += Push;
        new Thread(player.DuckLoop) { IsBackground = true }.Start();

        // While playing, the position once a second; quit when nothing has played for a while.
        new Thread(() =>
        {
            while (!quit.IsSet)
            {
                Thread.Sleep(1000);
                if (player.Playing) Push();
                if (player.Idle > TimeSpan.FromMinutes(20)) quit.Set();
            }
        }) { IsBackground = true }.Start();

        new Thread(() =>
        {
            while (!quit.IsSet)
            {
                var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                try { pipe.WaitForConnection(); }
                catch { pipe.Dispose(); continue; }
                new Thread(() => Handle(pipe, player, watchers, quit, Push)) { IsBackground = true }.Start();
            }
        }) { IsBackground = true }.Start();

        quit.Wait();
        player.Shutdown();
    }

    static void Handle(NamedPipeServerStream pipe, Player player, List<StreamWriter> watchers, ManualResetEventSlim quit, Action push)
    {
        var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
        try
        {
            var line = new StreamReader(pipe, Encoding.UTF8).ReadLine();
            var words = line == null ? Array.Empty<string>() : JsonSerializer.Deserialize<string[]>(line) ?? Array.Empty<string>();
            var cmd = words.ElementAtOrDefault(0)?.ToLowerInvariant() ?? "state";
            string Arg(int i) => words.ElementAtOrDefault(i) ?? "";
            double Num(int i) => double.TryParse(Arg(i), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
            switch (cmd)
            {
                case "watch":
                    // Stays open: every change is written to it until the bar goes away.
                    writer.WriteLine(JsonSerializer.Serialize(player.State()));
                    lock (watchers) watchers.Add(writer);
                    return;
                case "play":
                {
                    var p = Library.Get(Arg(1)) ?? throw new Exception("no playlist " + Arg(1));
                    player.Play(p, words.Length > 2 ? (int)Num(2) : -1);
                    break;
                }
                case "station": player.PlayStation(Arg(1) is "" ? null : Arg(1).ToLowerInvariant()); break;
                case "suno-show": player.ShowSuno((int)Num(1), (int)Num(2), (int)Num(3), (int)Num(4)); break;
                case "suno-hide": player.HideSuno(); break;
                case "suno-create": player.SunoCreate(); break;
                case "rate": player.Rate((int)Num(1)); break;
                case "toggle": player.Toggle(); break;
                case "pause": player.Pause(); break;
                case "resume": player.Resume(); break;
                case "next": player.Next(); break;
                case "prev": player.Previous(); break;
                case "seek": player.Seek(Num(1)); break;
                case "shuffle": player.Shuffle(); break;
                case "volume": player.SetVolume(Num(1)); break;
                case "quit": quit.Set(); break;
                case "state": break;
                default: throw new Exception("unknown command " + cmd);
            }
            writer.WriteLine(JsonSerializer.Serialize(player.State()));
            pipe.Dispose();
        }
        catch (Exception ex)
        {
            try { writer.WriteLine(JsonSerializer.Serialize(new { ok = false, error = ex.Message })); } catch { }
            pipe.Dispose();
        }
    }
}
