using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace SystemMenu;

// The Audio page's data (Win+A, MainWindow.Audio.cs): every speaker / headset and every
// microphone, with its volume, its mute, and which one Windows uses; and every app making sound
// (the music player, Chrome, Teams, ...) with its own volume, its mute and the speaker it plays
// on, like Windows' volume mixer. Windows' Core Audio (what the volume flyout and Sound settings
// use), so it works whatever keyboard you have: some compact ones have no volume
// keys Windows understands.
public sealed record AudioDevice(string Id, string Name, bool Input, bool Default, int Volume, bool Muted);

// An app's sound (all its sessions, on every speaker, as one): Key is its program's path, or
// "system" for system sounds. Output: the speaker it's sent to, or null for Windows' default.
public sealed record AudioApp(string Key, string Name, string? Exe, bool Playing, int Volume, bool Muted, uint[] Pids, string? Output);

public static partial class Audio
{
    // How loud each device and app is right now (0..1), for the page's live meters: by device
    // id or app key. List() starts it afresh, Apps() adds the apps.
    static readonly Dictionary<string, List<IAudioMeterInformation>> meters = [];

    public static float Peak(string key)
    {
        float loudest = 0;
        if (meters.TryGetValue(key, out var list))
            foreach (var meter in list)
                try { meter.GetPeakValue(out float peak); loudest = Math.Max(loudest, peak); } catch { }
        return loudest;
    }

    static void AddMeter(string key, object source)
    {
        try
        {
            if (!meters.TryGetValue(key, out var list)) meters[key] = list = [];
            list.Add((IAudioMeterInformation)source);
        }
        catch { } // no meter: it just doesn't move
    }

    // Speakers first, then microphones; the default of each first.
    public static List<AudioDevice> List()
    {
        var list = new List<AudioDevice>();
        meters.Clear();
        try
        {
            var devices = Enumerator();
            foreach (var flow in new[] { DataFlow.Render, DataFlow.Capture })
            {
                string? defaultId = null;
                try { devices.GetDefaultAudioEndpoint(flow, Role.Multimedia, out var d); d.GetId(out defaultId); } catch { } // none plugged in
                devices.EnumAudioEndpoints(flow, ActiveOnly, out var all);
                all.GetCount(out uint count);
                var found = new List<AudioDevice>();
                for (uint i = 0; i < count; i++)
                {
                    try
                    {
                        all.Item(i, out var device);
                        device.GetId(out var id);
                        var volume = Volume(device);
                        volume.GetMasterVolumeLevelScalar(out float level);
                        volume.GetMute(out bool muted);
                        found.Add(new AudioDevice(id, Name(device), flow == DataFlow.Capture, id == defaultId, (int)Math.Round(level * 100), muted));
                        AddMeter(id, Activate(device, MeterIid));
                    }
                    catch (Exception ex) { App.Log(ex); }
                }
                list.AddRange(found.OrderByDescending(d => d.Default).ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase));
            }
        }
        catch (Exception ex) { App.Log(ex); }
        return list;
    }

    // The apps with sound open (playing now first), each once however many sessions it has.
    public static List<AudioApp> Apps()
    {
        var apps = new List<AudioApp>();
        foreach (var group in Sessions().GroupBy(x => x.Key))
        {
            try
            {
                // Several (tabs, speakers): the level of one that's playing, playing if any is,
                // muted only if all are.
                var all = group.ToList();
                var lead = all.FirstOrDefault(x => x.Playing).Volume ?? all[0].Volume;
                lead.GetMasterVolume(out float level);
                bool muted = all.All(x => { x.Volume.GetMute(out bool m); return m; });
                var pids = all.Select(x => x.Pid).Where(pid => pid != 0).Distinct().ToArray();
                apps.Add(new AudioApp(group.Key, AppName(group.Key, all[0].Exe), all[0].Exe, all.Any(x => x.Playing), (int)Math.Round(level * 100), muted, pids, OutputOf(pids)));
                foreach (var x in all) AddMeter(group.Key, x.Volume);
            }
            catch (Exception ex) { App.Log(ex); }
        }
        return [.. apps.OrderByDescending(a => a.Playing).ThenBy(a => a.Key == "system").ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)];
    }

    // Every session of the app goes to the same level. The new level, or null if it's gone.
    public static int? ChangeApp(string key, int by)
    {
        int? to = null;
        var none = Guid.Empty;
        foreach (var (k, _, _, volume, _) in Sessions())
        {
            if (k != key) continue;
            try
            {
                if (to == null) { volume.GetMasterVolume(out float level); to = Math.Clamp((int)Math.Round(level * 100) + by, 0, 100); }
                volume.SetMasterVolume(to.Value / 100f, ref none);
                if (by > 0) volume.SetMute(false, ref none);
            }
            catch (Exception ex) { App.Log(ex); }
        }
        return to;
    }

    public static void ToggleAppMute(string key)
    {
        bool? to = null;
        var none = Guid.Empty;
        foreach (var (k, _, _, volume, _) in Sessions())
        {
            if (k != key) continue;
            try
            {
                if (to == null) { volume.GetMute(out bool muted); to = !muted; }
                volume.SetMute(to.Value, ref none);
            }
            catch (Exception ex) { App.Log(ex); }
        }
    }

    // Every app's sound session on every speaker (not ones that have ended).
    static List<(string Key, string? Exe, bool Playing, ISimpleAudioVolume Volume, uint Pid)> Sessions()
    {
        var found = new List<(string, string?, bool, ISimpleAudioVolume, uint)>();
        try
        {
            var devices = Enumerator();
            devices.EnumAudioEndpoints(DataFlow.Render, ActiveOnly, out var all);
            all.GetCount(out uint count);
            for (uint i = 0; i < count; i++)
            {
                try
                {
                    all.Item(i, out var device);
                    ((IAudioSessionManager2)Activate(device, SessionManagerIid)).GetSessionEnumerator(out var sessions);
                    sessions.GetCount(out int n);
                    for (int j = 0; j < n; j++)
                    {
                        sessions.GetSession(j, out var session);
                        session.GetState(out int state);
                        if (state == 2) continue; // expired
                        string? exe = null;
                        string key;
                        uint pid = 0;
                        if (session.IsSystemSoundsSession() == 0) key = "system";
                        else
                        {
                            session.GetProcessId(out pid);
                            exe = ExePath(pid);
                            if (exe == null) continue; // gone
                            key = exe.ToLowerInvariant();
                        }
                        found.Add((key, exe, state == 1, (ISimpleAudioVolume)session, pid));
                    }
                }
                catch (Exception ex) { App.Log(ex); }
            }
        }
        catch (Exception ex) { App.Log(ex); }
        return found;
    }

    // omawin's music player (omawin-music.exe), which has no name or icon of its own.
    public const string MusicName = "Music and podcasts";

    // What the program calls itself ("Google Chrome"), else its file name. Kept by path: reading
    // a program's version info costs a file open, and the page redraws on every key.
    static readonly Dictionary<string, string> names = new(StringComparer.OrdinalIgnoreCase);

    static string AppName(string key, string? exe)
    {
        if (key == "system" || exe == null) return "System sounds";
        lock (names)
            if (names.TryGetValue(exe, out var known)) return known;
        var file = Path.GetFileNameWithoutExtension(exe);
        string name = file;
        if (file.Equals("omawin-music", StringComparison.OrdinalIgnoreCase)) name = MusicName;
        else
            try
            {
                var description = System.Diagnostics.FileVersionInfo.GetVersionInfo(exe).FileDescription?.Trim();
                if (!string.IsNullOrEmpty(description)) name = description;
            }
            catch { }
        lock (names) names[exe] = name;
        return name;
    }

    // Even an elevated program's path can be read with this little access.
    static string? ExePath(uint pid)
    {
        var process = OpenProcess(0x1000 /* query limited information */, false, pid);
        if (process == IntPtr.Zero) return null;
        try
        {
            var path = new System.Text.StringBuilder(1024);
            int size = path.Capacity;
            return QueryFullProcessImageName(process, 0, path, ref size) ? path.ToString() : null;
        }
        finally { CloseHandle(process); }
    }

    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr process, int flags, System.Text.StringBuilder path, ref int size);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);

    // Up or down by `by` percent: the new level, or null if it's gone.
    public static int? Change(string id, int by)
    {
        int? to = null;
        With(id, v =>
        {
            v.GetMasterVolumeLevelScalar(out float level);
            var none = Guid.Empty;
            to = Math.Clamp((int)Math.Round(level * 100) + by, 0, 100);
            v.SetMasterVolumeLevelScalar(to.Value / 100f, ref none);
            // Turning it up means you want to hear it.
            if (by > 0) v.SetMute(false, ref none);
        });
        return to;
    }

    // --- which speaker an app plays on ------------------------------------------------------
    // What Settings' "App volume and device preferences" (and EarTrumpet) use: Windows remembers
    // it for the app, and it moves at once. Not a public API: on a Windows without it, apps just
    // can't be sent anywhere (Policy() is null).
    const string DeviceStart = @"\\?\SWD#MMDEVAPI#", RenderEnd = "#{e6327cad-dcec-4949-ae8a-991e976a79d2}";
    static IAudioPolicyConfig? policy;
    static bool policyAsked;

    static IAudioPolicyConfig? Policy()
    {
        if (policyAsked) return policy;
        policyAsked = true;
        try
        {
            const string name = "Windows.Media.Internal.AudioPolicyConfig";
            WindowsCreateString(name, name.Length, out var className);
            var iid = PolicyConfigIid;
            RoGetActivationFactory(className, ref iid, out var factory);
            WindowsDeleteString(className);
            policy = (IAudioPolicyConfig)Wrap(factory);
            Marshal.Release(factory);
        }
        catch (Exception ex) { App.Log(ex); }
        return policy;
    }

    // The speaker the app's sent to (a device id), or null: Windows' default.
    static string? OutputOf(uint[] pids)
    {
        if (pids.Length == 0 || Policy() is not { } p) return null;
        try
        {
            if (p.GetPersistedDefaultAudioEndpoint(pids[0], DataFlow.Render, Role.Multimedia, out var h) != 0 || h == IntPtr.Zero) return null;
            var id = Marshal.PtrToStringUni(WindowsGetStringRawBuffer(h, out _));
            WindowsDeleteString(h);
            if (id == null || !id.StartsWith(DeviceStart)) return null;
            id = id[DeviceStart.Length..];
            return id.EndsWith(RenderEnd) ? id[..^RenderEnd.Length] : id;
        }
        catch (Exception ex) { App.Log(ex); return null; }
    }

    // Sends the app to that speaker; null: back to Windows' default.
    public static void SendTo(AudioApp app, string? deviceId)
    {
        if (Policy() is not { } p) return;
        IntPtr h = IntPtr.Zero;
        try
        {
            if (deviceId != null)
            {
                var full = DeviceStart + deviceId + RenderEnd;
                WindowsCreateString(full, full.Length, out h);
            }
            foreach (var pid in app.Pids)
                foreach (var role in new[] { Role.Multimedia, Role.Console })
                {
                    int hr = p.SetPersistedDefaultAudioEndpoint(pid, DataFlow.Render, role, h);
                    if (hr != 0) App.Log(new Exception($"Sending {app.Name} ({pid}) to {deviceId ?? "default"}: 0x{hr:X8}"));
                }
        }
        catch (Exception ex) { App.Log(ex); }
        finally { if (h != IntPtr.Zero) WindowsDeleteString(h); }
    }

    [DllImport("combase.dll", PreserveSig = false)] static extern void RoGetActivationFactory(IntPtr classId, ref Guid iid, out IntPtr factory);
    [DllImport("combase.dll", PreserveSig = false)] static extern void WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string text, int length, out IntPtr hstring);
    [DllImport("combase.dll")] static extern int WindowsDeleteString(IntPtr hstring);
    [DllImport("combase.dll")] static extern IntPtr WindowsGetStringRawBuffer(IntPtr hstring, out uint length);

    // Windows.Media.Internal.AudioPolicyConfig's factory (Windows 10 21H2 and later), as
    // EarTrumpet declares it: IInspectable's three, then what comes before the two used here.
    [GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16), Guid("ab3d4648-e242-459f-b02f-541c70306324")]
    internal partial interface IAudioPolicyConfig
    {
        void GetIids(); void GetRuntimeClassName(); void GetTrustLevel();
        void AddCtxVolumeChange(); void RemoveCtxVolumeChanged(); void AddRingerVibrateStateChanged(); void RemoveRingerVibrateStateChanged();
        void SetVolumeGroupGainForId(); void GetVolumeGroupGainForId(); void GetActiveVolumeGroupForEndpointId(); void GetVolumeGroupsForEndpoint();
        void GetCurrentVolumeContext(); void SetVolumeGroupMuteForId(); void GetVolumeGroupMuteForId(); void SetRingerVibrateState();
        void GetRingerVibrateState(); void SetPreferredChatApplication(); void ResetPreferredChatApplication(); void GetPreferredChatApplication();
        void GetCurrentChatApplications(); void AddChatContextChanged(); void RemoveChatContextChanged();
        [PreserveSig] int SetPersistedDefaultAudioEndpoint(uint pid, DataFlow flow, Role role, IntPtr deviceId);
        [PreserveSig] int GetPersistedDefaultAudioEndpoint(uint pid, DataFlow flow, Role role, out IntPtr deviceId);
    }

    public static void ToggleMute(string id) => With(id, v =>
    {
        v.GetMute(out bool muted);
        var none = Guid.Empty;
        v.SetMute(!muted, ref none);
    });

    // The one Windows uses, for everything: apps, calls and system sounds (Sound settings'
    // "Set as default" sets all three as well).
    public static void MakeDefault(string id)
    {
        try
        {
            var policy = (IPolicyConfig)Create(PolicyConfigClientClsid, PolicyConfigInterfaceIid);
            foreach (var role in new[] { Role.Console, Role.Multimedia, Role.Communications }) policy.SetDefaultEndpoint(id, role);
        }
        catch (Exception ex) { App.Log(ex); }
    }

    static void With(string id, Action<IAudioEndpointVolume> act)
    {
        try
        {
            var devices = Enumerator();
            devices.GetDevice(id, out var device);
            act(Volume(device));
        }
        catch (Exception ex) { App.Log(ex); }
    }

    static IAudioEndpointVolume Volume(IMMDevice device)
    {
        return (IAudioEndpointVolume)Activate(device, EndpointVolumeIid);
    }

    // "Speakers (Realtek(R) Audio)", as Sound settings shows it.
    static string Name(IMMDevice device)
    {
        device.OpenPropertyStore(0 /* read */, out var store);
        var key = new PropertyKey { Format = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), Id = 14 }; // PKEY_Device_FriendlyName
        store.GetValue(ref key, out var value);
        try { return value.Type == 31 /* VT_LPWSTR */ ? Marshal.PtrToStringUni(value.Pointer) ?? "Audio device" : "Audio device"; }
        finally { PropVariantClear(ref value); }
    }

    // --- Core Audio (mmdeviceapi.h, endpointvolume.h) ---------------------------------------
    // Source-generated COM ([GeneratedComInterface] and these wrappers), not [ComImport]: the
    // same code in the normal build and the native one (Native AOT has no built-in COM).
    static readonly StrategyBasedComWrappers Com = new();
    static readonly Guid MMDeviceEnumeratorClsid = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    static readonly Guid EnumeratorIid = new("A95664D2-9614-4F35-A746-DE8DB63617E6");
    static readonly Guid EndpointVolumeIid = new("5CDF2C82-841E-4546-9722-0CF74078229A");
    static readonly Guid MeterIid = new("C02216F6-8C67-4B5B-9D00-D008E73E0064");
    static readonly Guid SessionManagerIid = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
    static readonly Guid PolicyConfigIid = new("ab3d4648-e242-459f-b02f-541c70306324");
    static readonly Guid PolicyConfigInterfaceIid = new("f8679f50-850a-41cf-9c72-430f290290c8");

    // A COM object for this pointer; the wrapper holds its own reference, so ours is let go.
    static object Wrap(IntPtr unknown)
    {
        try { return Com.GetOrCreateObjectForComInstance(unknown, CreateObjectFlags.None); }
        finally { Marshal.Release(unknown); }
    }

    static object Create(Guid clsid, Guid iid)
    {
        Marshal.ThrowExceptionForHR(CoCreateInstance(clsid, IntPtr.Zero, 1 /* in-process */, iid, out var unknown));
        return Wrap(unknown);
    }

    static IMMDeviceEnumerator Enumerator() => (IMMDeviceEnumerator)Create(MMDeviceEnumeratorClsid, EnumeratorIid);

    static object Activate(IMMDevice device, Guid iid)
    {
        device.Activate(ref iid, 1 /* in-process */, IntPtr.Zero, out var unknown);
        return Wrap(unknown);
    }

    [DllImport("ole32.dll")] static extern int CoCreateInstance(in Guid clsid, IntPtr outer, uint context, in Guid iid, out IntPtr unknown);

    const uint ActiveOnly = 1; // DEVICE_STATE_ACTIVE
    internal enum DataFlow { Render, Capture }
    internal enum Role { Console, Multimedia, Communications }

    [StructLayout(LayoutKind.Sequential)] internal struct PropertyKey { public Guid Format; public int Id; }
    [StructLayout(LayoutKind.Sequential)] internal struct PropVariant { public ushort Type; ushort r1, r2, r3; public IntPtr Pointer; IntPtr more; }
    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVariant value);

    [GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16), Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    internal partial interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints(DataFlow flow, uint states, out IMMDeviceCollection devices);
        void GetDefaultAudioEndpoint(DataFlow flow, Role role, out IMMDevice device);
        void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    }

    [GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16), Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    internal partial interface IMMDeviceCollection
    {
        void GetCount(out uint count);
        void Item(uint index, out IMMDevice device);
    }

    [GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16), Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    internal partial interface IMMDevice
    {
        void Activate(ref Guid iid, uint context, IntPtr parameters, out IntPtr o);
        void OpenPropertyStore(uint access, out IPropertyStore store);
        void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16), Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
    internal partial interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
    }

    [GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16), Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    internal partial interface IAudioEndpointVolume
    {
        void RegisterControlChangeNotify(IntPtr notify);
        void UnregisterControlChangeNotify(IntPtr notify);
        void GetChannelCount(out uint count);
        void SetMasterVolumeLevel(float db, ref Guid context);
        void SetMasterVolumeLevelScalar(float level, ref Guid context);
        void GetMasterVolumeLevel(out float db);
        void GetMasterVolumeLevelScalar(out float level);
        void SetChannelVolumeLevel(uint channel, float db, ref Guid context);
        void SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        void GetChannelVolumeLevel(uint channel, out float db);
        void GetChannelVolumeLevelScalar(uint channel, out float level);
        void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16), Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
    internal partial interface IAudioMeterInformation { void GetPeakValue(out float peak); }

    [GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16), Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
    internal partial interface IAudioSessionManager2
    {
        void GetAudioSessionControl(IntPtr group, uint flags, out IntPtr control);
        void GetSimpleAudioVolume(IntPtr group, uint flags, out IntPtr volume);
        void GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }

    [GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16), Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
    internal partial interface IAudioSessionEnumerator
    {
        void GetCount(out int count);
        void GetSession(int index, out IAudioSessionControl2 session);
    }

    [GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16), Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d")]
    internal partial interface IAudioSessionControl2
    {
        void GetState(out int state);
        void GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        void SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid context);
        void GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        void SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid context);
        void GetGroupingParam(out Guid group);
        void SetGroupingParam(ref Guid group, ref Guid context);
        void RegisterAudioSessionNotification(IntPtr events);
        void UnregisterAudioSessionNotification(IntPtr events);
        void GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        void GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        void GetProcessId(out uint pid);
        [PreserveSig] int IsSystemSoundsSession(); // 0 (S_OK): it is
    }

    [GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16), Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
    internal partial interface ISimpleAudioVolume
    {
        void SetMasterVolume(float level, ref Guid context);
        void GetMasterVolume(out float level);
        void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    // Setting the default device has no public API; this is the one Sound settings itself uses
    // (undocumented, but unchanged since Windows 7, and what every audio switcher calls).
    static readonly Guid PolicyConfigClientClsid = new("870af99c-171d-4f9e-af0d-e63df40c2bc9");

    [GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16), Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
    internal partial interface IPolicyConfig
    {
        void GetMixFormat(IntPtr a, IntPtr b);
        void GetDeviceFormat(IntPtr a, int b, IntPtr c);
        void ResetDeviceFormat(IntPtr a);
        void SetDeviceFormat(IntPtr a, IntPtr b, IntPtr c);
        void GetProcessingPeriod(IntPtr a, int b, IntPtr c, IntPtr d);
        void SetProcessingPeriod(IntPtr a, IntPtr b);
        void GetShareMode(IntPtr a, IntPtr b);
        void SetShareMode(IntPtr a, IntPtr b);
        void GetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
        void SetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
        void SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, Role role);
    }
}
