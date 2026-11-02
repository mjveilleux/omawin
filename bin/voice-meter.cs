// voice-meter: tells the bar (the menu app's, shell/menu-app/Bar/Bar.cs) when dictation (Handy on Win+H,
// or Windows voice typing) is listening, and how loud the microphone is, so the bar can animate
// along with your voice. It quits when whoever reads it is gone: its next line fails to write.
//
// Prints one line at a time on stdout:
//   1 0.425   listening, mic peak level 0..1   (every ~40 ms while listening)
//   2 0.425   omawin's agent is listening (Ctrl+Win+H), the same way
//   0         not listening                     (on change, then every 2 s)
//
// Listening = Handy or omawin-listen (src/agent/omawin-listen.cs) has an active recording session
// on a microphone (Core Audio's session list, what the sound mixer shows), or Handy, the voice
// typing app (package MicrosoftWindows.Client.CBS) or omawin-listen is using the mic per Windows'
// privacy log in the registry (LastUsedTimeStop is 0 while in use). The sessions come first: some
// Windows builds stop writing that log (26300 did from 2026-10-20, for every app). The level
// comes from the default microphone's peak meter, the same meter Windows' sound settings
// show; this never opens the microphone itself.
//
// Built by install.ps1 into ~/.local/bin with the C# compiler that ships with Windows.
using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

static class VoiceMeter
{
    const string MicLog = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";
    // Who counts as "dictating": Windows voice typing (a packaged app) and Handy (Win+H, a
    // regular program, logged under NonPackaged with its path, '\' written as '#').
    const string VoiceTyping = "MicrosoftWindows.Client.CBS_";
    const string Handy = "#handy.exe";
    // omawin's agent (Ctrl+Win+H) records with its own program, so the bar can say who's listening.
    const string Omawin = "#omawin-listen.exe";

    static bool InUse(RegistryKey app)
    {
        object start = app.GetValue("LastUsedTimeStart"), stop = app.GetValue("LastUsedTimeStop");
        return start != null && stop != null && Convert.ToInt64(stop) == 0;
    }

    // 0: nobody; 1: dictation; 2: omawin's agent.
    static int Listening()
    {
        int recording = Recording();
        if (recording != 0) return recording;
        using (var log = Registry.CurrentUser.OpenSubKey(MicLog))
        {
            if (log == null) return 0;
            using (var programs = log.OpenSubKey("NonPackaged"))
            {
                if (programs != null)
                {
                    foreach (var name in programs.GetSubKeyNames())
                    {
                        int who = name.EndsWith(Omawin, StringComparison.OrdinalIgnoreCase) ? 2
                            : name.EndsWith(Handy, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                        if (who == 0) continue;
                        using (var app = programs.OpenSubKey(name)) { if (InUse(app)) return who; }
                    }
                }
            }
            foreach (var name in log.GetSubKeyNames())
            {
                if (!name.StartsWith(VoiceTyping, StringComparison.OrdinalIgnoreCase)) continue;
                using (var app = log.OpenSubKey(name)) { if (InUse(app)) return 1; }
            }
        }
        return 0;
    }

    // --- Core Audio: the default capture device's peak meter ------------------------------
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
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

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        int GetCount(out int count);
        int Item(int index, out IMMDevice device);
    }

    // --- Core Audio: who is recording -------------------------------------------------------
    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionManager2
    {
        int GetAudioSessionControl(IntPtr groupingParam, int flags, out IntPtr control);
        int GetSimpleAudioVolume(IntPtr groupingParam, int flags, out IntPtr volume);
        int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionEnumerator
    {
        int GetCount(out int count);
        int GetSession(int index, out IAudioSessionControl2 session);
    }

    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl2
    {
        int GetState(out int state);
        int GetDisplayName(out IntPtr name);
        int SetDisplayName(string name, IntPtr context);
        int GetIconPath(out IntPtr path);
        int SetIconPath(string path, IntPtr context);
        int GetGroupingParam(out Guid param);
        int SetGroupingParam(ref Guid param, IntPtr context);
        int RegisterAudioSessionNotification(IntPtr client);
        int UnregisterAudioSessionNotification(IntPtr client);
        int GetSessionIdentifier(out IntPtr id);
        int GetSessionInstanceIdentifier(out IntPtr id);
        int GetProcessId(out int pid);
    }

    const int eCapture = 1, eConsole = 0, CLSCTX_ALL = 23, DEVICE_STATE_ACTIVE = 1, AudioSessionStateActive = 1;

    // Each microphone's session manager, looked up again every few seconds (a mic plugged in).
    static IAudioSessionManager2[] managers = new IAudioSessionManager2[0];
    static DateTime managersAt = DateTime.MinValue;

    static int Recording()
    {
        try
        {
            if ((DateTime.Now - managersAt).TotalSeconds >= 5) { managers = OpenManagers(); managersAt = DateTime.Now; }
            foreach (var manager in managers)
            {
                IAudioSessionEnumerator sessions;
                if (manager.GetSessionEnumerator(out sessions) != 0 || sessions == null) continue;
                int count;
                sessions.GetCount(out count);
                for (int i = 0; i < count; i++)
                {
                    IAudioSessionControl2 session;
                    int state, pid;
                    if (sessions.GetSession(i, out session) != 0 || session == null) continue;
                    if (session.GetState(out state) != 0 || state != AudioSessionStateActive) continue;
                    if (session.GetProcessId(out pid) != 0 || pid == 0) continue;
                    string name;
                    try { name = System.Diagnostics.Process.GetProcessById(pid).ProcessName; }
                    catch (ArgumentException) { continue; } // it just ended
                    if (name.Equals("omawin-listen", StringComparison.OrdinalIgnoreCase)) return 2;
                    if (name.Equals("handy", StringComparison.OrdinalIgnoreCase)) return 1;
                }
            }
        }
        catch (Exception) { managersAt = DateTime.MinValue; } // a device went away: look again
        return 0;
    }

    static IAudioSessionManager2[] OpenManagers()
    {
        var found = new System.Collections.Generic.List<IAudioSessionManager2>();
        try
        {
            var devices = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            IMMDeviceCollection mics;
            if (devices.EnumAudioEndpoints(eCapture, DEVICE_STATE_ACTIVE, out mics) != 0 || mics == null) return found.ToArray();
            int count;
            mics.GetCount(out count);
            var iid = typeof(IAudioSessionManager2).GUID;
            for (int i = 0; i < count; i++)
            {
                IMMDevice mic;
                object manager;
                if (mics.Item(i, out mic) != 0 || mic == null) continue;
                if (mic.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out manager) == 0) found.Add((IAudioSessionManager2)manager);
            }
        }
        catch (Exception) { }
        return found.ToArray();
    }

    static IAudioMeterInformation OpenMeter()
    {
        try
        {
            var devices = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            IMMDevice mic;
            if (devices.GetDefaultAudioEndpoint(eCapture, eConsole, out mic) != 0 || mic == null) return null;
            var iid = typeof(IAudioMeterInformation).GUID;
            object meter;
            if (mic.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out meter) != 0) return null;
            return (IAudioMeterInformation)meter;
        }
        catch { return null; }
    }

    [STAThread]
    static void Main()
    {
        try { Run(); }
        catch (System.IO.IOException) { } // stdout closed: whoever was reading is gone
    }

    static void Run()
    {
        var output = Console.Out;
        IAudioMeterInformation meter = null;
        bool wasListening = false;
        var idleSince = DateTime.MinValue;

        while (true)
        {
            int who = Listening();
            bool listening = who != 0;
            if (listening)
            {
                // (Re)open the meter when listening starts, in case the default mic changed.
                if (!wasListening || meter == null) meter = OpenMeter();
                float peak = 0;
                if (meter == null || meter.GetPeakValue(out peak) != 0) { meter = null; peak = 0; }
                output.WriteLine(who + " " + peak.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture));
                output.Flush();
                Thread.Sleep(40);
            }
            else
            {
                if (wasListening || (DateTime.Now - idleSince).TotalSeconds >= 2)
                {
                    output.WriteLine("0");
                    output.Flush();
                    idleSince = DateTime.Now;
                }
                Thread.Sleep(150);
            }
            wasListening = listening;
        }
    }
}
