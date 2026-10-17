using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace OmawinMusic;

// `omawin-music watch`: what the bar reads, like voice-meter. It never starts the player; it
// waits for one to appear, passes on what it says, sends the playlists and podcasts whenever
// ~/Music/omawin changes, and while music plays, the speakers' level (Windows' own peak meter, the one sound
// settings shows) so the bar's equalizer moves with the music.
static class Watch
{
    static TextWriter output = TextWriter.Null;
    static readonly object writeGate = new();
    static volatile bool playing;

    // Nobody reading any more (the bar was restarted): quit.
    static void Write(string line)
    {
        try { lock (writeGate) output.WriteLine(line); }
        catch { Environment.Exit(0); }
    }

    public static void Run(TextWriter writer)
    {
        output = writer;
        try
        {
            SendPlaylists();
            Directory.CreateDirectory(Library.Folder(Library.Podcasts));
            using var folder = new FileSystemWatcher(Library.Root, "*.json") { EnableRaisingEvents = true, IncludeSubdirectories = true };
            var debounce = new Timer(_ => { try { SendPlaylists(); } catch { } });
            FileSystemEventHandler changed = (_, _) => debounce.Change(300, Timeout.Infinite);
            folder.Created += changed;
            folder.Changed += changed;
            folder.Deleted += changed;
            folder.Renamed += (_, _) => debounce.Change(300, Timeout.Infinite);

            new Thread(Levels) { IsBackground = true }.Start();
            ListenForOpen();
            new Thread(() =>
            {
                // Quit when the Zebar that started this is gone (closed or restarted), so no copy
                // lingers behind; a new Zebar starts its own.
                var zebar = Parent();
                while (true)
                {
                    Thread.Sleep(3000);
                    if (zebar == null || zebar.HasExited) Environment.Exit(0);
                }
            }) { IsBackground = true }.Start();

            while (true)
            {
                Write("""S {"running":false}""");
                playing = false;
                while (!Program.PlayerRuns()) Thread.Sleep(1500);
                using var pipe = Program.Connect(2000);
                if (pipe == null) { Thread.Sleep(1500); continue; }
                new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true }.WriteLine("""["watch"]""");
                var reader = new StreamReader(pipe, Encoding.UTF8);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    playing = line.Contains("\"state\":\"playing\"");
                    Write("S " + line);
                }
            }
        }
        catch (IOException) { } // stdout closed: the bar is gone
    }

    static System.Diagnostics.Process? Parent()
    {
        try
        {
            using var me = System.Diagnostics.Process.GetCurrentProcess();
            var info = new ProcessBasicInformation();
            NtQueryInformationProcess(me.Handle, 0, ref info, Marshal.SizeOf(info), out _);
            return System.Diagnostics.Process.GetProcessById((int)info.ParentId);
        }
        catch { return null; }
    }
    [StructLayout(LayoutKind.Sequential)]
    struct ProcessBasicInformation { public IntPtr Reserved1, PebBase, Reserved2, Reserved3, UniqueId, ParentId; }
    [DllImport("ntdll.dll")]
    static extern int NtQueryInformationProcess(IntPtr process, int infoClass, ref ProcessBasicInformation info, int size, out int returned);

    // --- Win+M: one panel across all the bars ---------------------------------------------------
    // Each monitor's bar runs its own watch, so `omawin-music open` has to reach all of them and
    // pick one. Each watch owns four named events, keyed by its process id: two flags its bar sets
    // over stdin ("panel 1|0": its panel is open; "focus 1|0": it sits on GlazeWM's focused
    // monitor), and two auto-reset signals, open and close, that it passes to its bar as "O open"
    // / "O close". `open` finds the watches, reads the flags and signals: close to every bar with
    // a panel open if there is one, else open to the focused monitor's bar. Per-process events,
    // rather than one shared event, because an auto-reset event wakes only one waiter and nobody
    // could safely reset a manual-reset one.
    static string EventName(int pid, string what) => $@"Local\omawin-music-{pid}-{what}";

    static void ListenForOpen()
    {
        int me = Environment.ProcessId;
        var panel = new EventWaitHandle(false, EventResetMode.ManualReset, EventName(me, "panel"));
        var focus = new EventWaitHandle(false, EventResetMode.ManualReset, EventName(me, "focus"));
        var signals = new WaitHandle[]
        {
            new EventWaitHandle(false, EventResetMode.AutoReset, EventName(me, "open")),
            new EventWaitHandle(false, EventResetMode.AutoReset, EventName(me, "close")),
        };
        new Thread(() =>
        {
            while (true) Write(WaitHandle.WaitAny(signals) == 0 ? "O open" : "O close");
        }) { IsBackground = true }.Start();
        new Thread(() =>
        {
            try
            {
                string? line;
                while ((line = Console.In.ReadLine()) != null)
                {
                    var words = line.Trim().Split(' ');
                    if (words.Length != 2) continue;
                    var flag = words[0] == "panel" ? panel : words[0] == "focus" ? focus : null;
                    if (flag == null) continue;
                    if (words[1] == "1") flag.Set(); else flag.Reset();
                }
            }
            catch { }
        }) { IsBackground = true }.Start();
    }

    // `omawin-music open`: what it did ("opened" / "closed"), or null when no bar is running.
    public static string? RequestOpen()
    {
        var bars = new List<EventWaitHandle[]>(); // panel, focus, open, close
        foreach (var process in System.Diagnostics.Process.GetProcessesByName("omawin-music"))
        {
            using (process)
            {
                // This account's bars only (another signed-in account has its own).
                if (process.Id == Environment.ProcessId || process.SessionId != System.Diagnostics.Process.GetCurrentProcess().SessionId) continue;
                var handles = new List<EventWaitHandle>();
                foreach (var what in new[] { "panel", "focus", "open", "close" })
                    if (EventWaitHandle.TryOpenExisting(EventName(process.Id, what), out var h)) handles.Add(h);
                if (handles.Count == 4) bars.Add(handles.ToArray());
                else handles.ForEach(h => h.Dispose()); // the player, or a watch still starting
            }
        }
        try
        {
            if (bars.Count == 0) return null;
            var open = bars.Where(b => b[0].WaitOne(0)).ToList();
            if (open.Count > 0)
            {
                foreach (var b in open) b[3].Set();
                return "closed";
            }
            // No focus known (GlazeWM not running): the first bar.
            (bars.FirstOrDefault(b => b[1].WaitOne(0)) ?? bars[0])[2].Set();
            return "opened";
        }
        finally
        {
            foreach (var b in bars) foreach (var h in b) h.Dispose();
        }
    }

    static void SendPlaylists() => Write("P " + JsonSerializer.Serialize(Program.Summaries()));

    static void Levels()
    {
        IAudioMeterInformation? meter = null;
        while (true)
        {
            if (!playing) { meter = null; Thread.Sleep(200); continue; }
            meter ??= OpenMeter(); // again after a pause, in case the speakers changed
            float peak = 0;
            if (meter == null || meter.GetPeakValue(out peak) != 0) { meter = null; peak = 0; }
            Write("L " + peak.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture));
            Thread.Sleep(40);
        }
    }

    // --- Core Audio: the default speakers' peak meter -----------------------------------------
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioMeterInformation
    {
        int GetPeakValue(out float peak);
    }

    static IAudioMeterInformation? OpenMeter()
    {
        try
        {
            const int eRender = 0, eMultimedia = 1, CLSCTX_ALL = 23;
            var devices = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            if (devices.GetDefaultAudioEndpoint(eRender, eMultimedia, out var speakers) != 0 || speakers == null) return null;
            var iid = typeof(IAudioMeterInformation).GUID;
            if (speakers.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var meter) != 0) return null;
            return (IAudioMeterInformation)meter;
        }
        catch { return null; }
    }
}
