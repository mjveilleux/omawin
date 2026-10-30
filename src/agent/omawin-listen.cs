// omawin-listen: Ctrl+Win+H (omawin agent listen). Records what you say for omawin's agent.
//
//   first press    records the default microphone (16 kHz mono, what Handy's models take);
//                  nothing opens, the bar's pill says "omawin is listening" (voice-meter sees
//                  this exe on the microphone)
//   second press   a second copy finds the first (a named mutex), tells it to stop and exits;
//                  the first writes the WAV and hands it to src/agent/listen.ps1, hidden, which has
//                  Handy transcribe it and passes the words to the agent
//
// Its own recorder, not Handy's: Handy types whatever it hears into the focused window, and
// this is for the agent, not that window. Recording starts the moment the key is pressed
// (a native exe, no PowerShell start-up), so the first words aren't lost.
//
//   omawin-listen.exe            toggle, as above
//   omawin-listen.exe cancel     stop and drop the recording
//
// Built by install.ps1 into ~/.local/bin with the C# compiler that ships with Windows (C# 5).
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

static class OmawinListen
{
    const int Rate = 16000, Bits = 16, Channels = 1;
    const int MaxSeconds = 180;          // a key left on by mistake stops by itself
    const int BufferMs = 100, Buffers = 8;

    [StructLayout(LayoutKind.Sequential)]
    struct WaveFormat
    {
        public short wFormatTag, nChannels;
        public int nSamplesPerSec, nAvgBytesPerSec;
        public short nBlockAlign, wBitsPerSample, cbSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WaveHeader
    {
        public IntPtr lpData;
        public int dwBufferLength, dwBytesRecorded;
        public IntPtr dwUser;
        public int dwFlags, dwLoops;
        public IntPtr lpNext, reserved;
    }

    const int WHDR_DONE = 1, WAVE_MAPPER = -1;
    [DllImport("winmm.dll")] static extern int waveInOpen(out IntPtr h, int device, ref WaveFormat format, IntPtr callback, IntPtr instance, int flags);
    [DllImport("winmm.dll")] static extern int waveInPrepareHeader(IntPtr h, IntPtr header, int size);
    [DllImport("winmm.dll")] static extern int waveInUnprepareHeader(IntPtr h, IntPtr header, int size);
    [DllImport("winmm.dll")] static extern int waveInAddBuffer(IntPtr h, IntPtr header, int size);
    [DllImport("winmm.dll")] static extern int waveInStart(IntPtr h);
    [DllImport("winmm.dll")] static extern int waveInReset(IntPtr h);
    [DllImport("winmm.dll")] static extern int waveInClose(IntPtr h);

    static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "launcher", "agent"); } }

    static int Main(string[] args)
    {
        bool cancel = args.Length > 0 && args[0] == "cancel";
        bool created;
        using (var mutex = new Mutex(true, @"Local\omawin-listen", out created))
        using (var stop = new EventWaitHandle(false, EventResetMode.ManualReset, @"Local\omawin-listen-stop"))
        using (var drop = new EventWaitHandle(false, EventResetMode.ManualReset, @"Local\omawin-listen-cancel"))
        {
            if (!created)
            {
                // One is recording: this press stops it (or drops it).
                if (cancel) drop.Set();
                stop.Set();
                return 0;
            }
            if (cancel) return 0;
            stop.Reset(); drop.Reset();
            Directory.CreateDirectory(Dir);
            WriteState("listening");
            byte[] audio;
            try { audio = Record(stop); }
            catch (Exception e) { WriteState("failed", "the microphone: " + e.Message); return 1; }
            if (drop.WaitOne(0) || audio.Length < Rate * 2 / 4) { WriteState("idle"); return 0; } // under 0.25 s: a double tap
            var wav = Path.Combine(Dir, "request.wav");
            WriteWav(wav, audio);
            WriteState("working", "Listening to you");
            return HandOver(wav);
        }
    }

    static byte[] Record(WaitHandle stop)
    {
        var format = new WaveFormat
        {
            wFormatTag = 1, nChannels = Channels, nSamplesPerSec = Rate, wBitsPerSample = Bits,
            nBlockAlign = Channels * Bits / 8, nAvgBytesPerSec = Rate * Channels * Bits / 8,
        };
        IntPtr h;
        int err = waveInOpen(out h, WAVE_MAPPER, ref format, IntPtr.Zero, IntPtr.Zero, 0);
        if (err != 0) throw new Exception("waveInOpen " + err);

        int size = Marshal.SizeOf(typeof(WaveHeader));
        int bytes = format.nAvgBytesPerSec * BufferMs / 1000;
        var headers = new IntPtr[Buffers];
        for (int i = 0; i < Buffers; i++)
        {
            var header = new WaveHeader { lpData = Marshal.AllocHGlobal(bytes), dwBufferLength = bytes };
            headers[i] = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(header, headers[i], false);
            waveInPrepareHeader(h, headers[i], size);
            waveInAddBuffer(h, headers[i], size);
        }
        var audio = new MemoryStream();
        var chunk = new byte[bytes];
        var started = Stopwatch.StartNew();
        waveInStart(h);
        int next = 0;
        while (true)
        {
            if (stop.WaitOne(20) || started.Elapsed.TotalSeconds > MaxSeconds) break;
            // The buffers fill in order; take each one that's done and give it back.
            while (true)
            {
                var header = (WaveHeader)Marshal.PtrToStructure(headers[next], typeof(WaveHeader));
                if ((header.dwFlags & WHDR_DONE) == 0) break;
                Marshal.Copy(header.lpData, chunk, 0, header.dwBytesRecorded);
                audio.Write(chunk, 0, header.dwBytesRecorded);
                waveInUnprepareHeader(h, headers[next], size);
                header.dwFlags = 0; header.dwBytesRecorded = 0;
                Marshal.StructureToPtr(header, headers[next], false);
                waveInPrepareHeader(h, headers[next], size);
                waveInAddBuffer(h, headers[next], size);
                next = (next + 1) % Buffers;
            }
        }
        // What's left, in order from the next one due: the done buffers, and (after the reset
        // returns them) the one that was filling.
        waveInReset(h);
        for (int i = 0; i < Buffers; i++)
        {
            var header = (WaveHeader)Marshal.PtrToStructure(headers[(next + i) % Buffers], typeof(WaveHeader));
            if ((header.dwFlags & WHDR_DONE) != 0 && header.dwBytesRecorded > 0)
            {
                Marshal.Copy(header.lpData, chunk, 0, header.dwBytesRecorded);
                audio.Write(chunk, 0, header.dwBytesRecorded);
            }
        }
        for (int i = 0; i < Buffers; i++)
        {
            var header = (WaveHeader)Marshal.PtrToStructure(headers[i], typeof(WaveHeader));
            waveInUnprepareHeader(h, headers[i], size);
            Marshal.FreeHGlobal(header.lpData);
            Marshal.FreeHGlobal(headers[i]);
        }
        waveInClose(h);
        return audio.ToArray();
    }

    static void WriteWav(string path, byte[] data)
    {
        using (var w = new BinaryWriter(File.Create(path)))
        {
            w.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' }); w.Write(36 + data.Length);
            w.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
            w.Write(16); w.Write((short)1); w.Write((short)Channels); w.Write(Rate);
            w.Write(Rate * Channels * Bits / 8); w.Write((short)(Channels * Bits / 8)); w.Write((short)Bits);
            w.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' }); w.Write(data.Length);
            w.Write(data);
        }
    }

    // The state the bar reads (src/agent/ask.ps1 writes the same file): one JSON line.
    static void WriteState(string state, string step = "")
    {
        var line = "{\"state\":\"" + state + "\",\"step\":\"" + step.Replace("\\", "\\\\").Replace("\"", "\\\"") +
            "\",\"time\":\"" + DateTime.Now.ToString("s") + "\"}";
        var file = Path.Combine(Dir, "state");
        var tmp = file + "." + Process.GetCurrentProcess().Id + ".tmp";
        File.WriteAllText(tmp, line);
        if (File.Exists(file)) File.Delete(file);
        File.Move(tmp, file);
    }

    // src/agent/listen.ps1 does the rest (Handy, then the agent): this repo is %DOTFILES%, else ~/omawin.
    static int HandOver(string wav)
    {
        var repo = Environment.GetEnvironmentVariable("DOTFILES");
        if (string.IsNullOrEmpty(repo)) repo = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "omawin");
        var info = new ProcessStartInfo("pwsh", "-NoLogo -NoProfile -ExecutionPolicy Bypass -File \"" +
            Path.Combine(repo, "src", "agent", "listen.ps1") + "\" -Wav \"" + wav + "\"")
        { UseShellExecute = false, CreateNoWindow = true };
        try { Process.Start(info); return 0; }
        catch (Exception e) { WriteState("failed", "src/agent/listen.ps1: " + e.Message); return 1; }
    }
}
