using System.Runtime.InteropServices;
using System.Text;
using RadioKind = Windows.Devices.Radios.RadioKind;

namespace SystemMenu;

// Quick settings (Win+A, MainWindow.Quick.cs): what Windows is connected to and how it is set up —
// Wi-Fi, Bluetooth, screen brightness, the power mode and the battery, do not disturb, night
// light — beside the Audio page's speakers and microphones. This file is the data: the raw APIs
// behind each section, and the read-only self test (`SystemMenu.exe --qs-selftest`, App.xaml.cs).
//
// Every call here is safe to make off the UI thread and none of it touches the desktop; the page
// calls it with Task.Run and draws the result.

// --- what a section sees ----------------------------------------------------------------
public sealed record WifiState(bool Present, bool RadioOn, string? Ssid, int Signal, string? Error)
{
    public bool Connected => Ssid != null;
}

// Signal is 0..100. Connected/HasProfile come from the network list's flags; a secure network
// needs its password the first time (HasProfile false), and then Windows saves it. Profile is
// the saved profile's name, which WlanConnect wants; it is usually the SSID but needn't be (a
// profile renamed, or made by an admin tool).
public sealed record WifiNetwork(string Ssid, int Signal, bool Connected, bool HasProfile, bool Secure, bool Connectable, string Profile = "");

// A paired device. Le: it came from the Bluetooth LE stack, which has no connect of its own
// (Windows keeps HID devices up by itself); classic devices take the audio services below.
// Handsfree (roadmap 8.49): a headset's hands-free service (call quality, its microphone) is on or
// off; null when it has none, or Windows wouldn't say.
public sealed record BtDevice(string Name, ulong Address, bool Connected, bool Le = false, bool? Handsfree = null);

// A monitor: the built-in panel (WMI), or an external one over DDC/CI (dxva2). Writable is false
// when the monitor answers but doesn't implement the brightness control (MC_CAPS_BRIGHTNESS).
public sealed record Display(string Name, bool Internal, int Current, int Min, int Max, bool Writable, long Key = 0);

// Current is normalized (both of Windows' ways of saying Balanced read as Brightness.Balanced);
// Raw is what powrprof answered, for the self test.
public sealed record PowerModes(Guid Current, bool Known, Guid Raw = default);

// The sections of the page, so an event or a write re-reads only what it concerns.
[Flags]
public enum QsPart { None = 0, Wifi = 1, Bluetooth = 2, Displays = 4, Power = 8, Other = 16, All = 31 }

public sealed record Battery(int Percent, string Status, bool Charging, bool EnergySaver);

public static class QuickSettings
{
    public static void Open(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { App.Log(ex); }
    }

    // --- Wi-Fi ----------------------------------------------------------------------------
    // Current connection and signal: Windows.Networking.Connectivity, which needs no location
    // consent (the one path Microsoft recommends for the connected SSID). The adapter list, the
    // network list and connecting go through the Win32 WLAN API (wlanapi.dll): the WinRT
    // Windows.Devices.WiFi needs the restricted wiFiControl capability in an app manifest, which
    // an unpackaged app doesn't have, so it is unusable here (see Wlan below).

    public static WifiState Wifi()
    {
        bool present = false, radioOn = false;
        string? error = null;
        try
        {
            present = Wlan.AdapterCount() > 0;
            radioOn = Radios.IsOn(RadioKind.WiFi);
        }
        catch (Exception ex) { error = ex.Message; }
        string? ssid = null; int signal = 0;
        try
        {
            var profile = Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile();
            if (profile?.IsWlanConnectionProfile == true)
            {
                ssid = profile.WlanConnectionProfileDetails?.GetConnectedSsid();
                signal = profile.GetSignalBars() ?? 0;
                if (signal > 0) signal = (int)Math.Round(signal / 5.0 * 100);
            }
        }
        catch (Exception ex) { error ??= ex.Message; }
        return new WifiState(present, radioOn, ssid, signal, error);
    }

    // The networks nearby. Needs precise-location consent on Windows 11 22H2 and later: without
    // it Windows returns access denied, and shows its own one-time prompt on the first call.
    // Denied tells the page to say so and offer the Location settings page.
    public sealed record WifiScan(List<WifiNetwork> Networks, bool Denied, string? Error);

    public static WifiScan NearbyNetworks(bool scan = true)
    {
        try
        {
            var (networks, denied, error) = Wlan.ScanAndList(scan);
            return new WifiScan(networks, denied, error);
        }
        catch (Exception ex) { return new WifiScan([], false, ex.Message); }
    }

    // Connects: a saved profile is used as it is; a new secure network gets a WPA2/WPA3 personal
    // profile built from the password (open networks a profile with no key). Returns null on
    // success, else what went wrong.
    // profile: the saved profile's name (WifiNetwork.Profile); a profile made here from the
    // password is named after the SSID, and an empty name falls back to it too.
    public static string? WifiConnect(string ssid, bool secure, string? password, string? profile = null)
    {
        try
        {
            if (secure && string.IsNullOrEmpty(password)) return "This network needs its password";
            if (secure) { Wlan.SetProfile(ssid, password!); profile = ssid; }
            return Wlan.Connect(string.IsNullOrEmpty(profile) ? ssid : profile);
        }
        catch (Exception ex) { App.Log(ex); return ex.Message; }
    }

    public static string? WifiDisconnect()
    {
        try { return Wlan.Disconnect(); }
        catch (Exception ex) { App.Log(ex); return ex.Message; }
    }

    // Null when the radio is now in the state asked for, else why not.
    public static string? SetWifiRadio(bool on) => Radios.Set(RadioKind.WiFi, on);

    // --- Bluetooth ------------------------------------------------------------------------
    public sealed record BtState(bool Present, bool RadioOn, string? Error);

    public static BtState Bluetooth()
    {
        try { return new BtState(Bt.Present, Radios.IsOn(RadioKind.Bluetooth), null); }
        catch (Exception ex) { return new BtState(false, false, ex.Message); }
    }

    public static string? SetBluetoothRadio(bool on) => Radios.Set(RadioKind.Bluetooth, on);

    // Paired devices, connected first. Needs no consent: these are the user's own devices.
    public static List<BtDevice> BluetoothDevices()
    {
        try { return Bt.Paired(); }
        catch (Exception ex) { App.Log(ex); return []; }
    }

    // Windows has no public "connect" for a paired device. Enable and disable the two audio
    // services (A2DP sink and Handsfree) instead: with the first enabled Windows brings the
    // device up, and with all of them disabled it drops the link. That is what
    // BluetoothSetServiceState (bthprops.cpl) is for, and what the Windows control panel does.
    public static string? BluetoothConnect(BtDevice device, bool connect)
    {
        try { return Bt.SetAudioServices(device.Address, connect); }
        catch (Exception ex) { App.Log(ex); return ex.Message; }
    }

    // A headset's hands-free service on or off (roadmap 8.49): off, it stays in stereo when an
    // app opens a microphone (another one is used); on again for calls through it.
    public static string? BluetoothHandsfree(BtDevice device, bool on)
    {
        try { return Bt.SetHandsfree(device.Address, on); }
        catch (Exception ex) { App.Log(ex); return ex.Message; }
    }

    // --- brightness -----------------------------------------------------------------------
    // The built-in panel answers WMI (root\wmi: WmiMonitorBrightness / WmiMonitorBrightnessMethods,
    // in tenths of a percent of nothing: 0..100 straight). External monitors answer DDC/CI over
    // dxva2 (GetMonitorBrightness / SetMonitorBrightness, VCP 0x10) when they implement it.
    // The EDID names and the active outputs are read once and handed to both halves. The
    // external half (DDC/CI: slow, a few hundred ms a monitor) is cached between reads and read
    // again when the displays change or after 30 s (Brightness.ExternalCached).
    public static List<Display> Displays()
    {
        var list = new List<Display>();
        var friendly = Brightness.FriendlyNames();
        var panels = new List<(Display Display, string Code)>();
        try { panels = Brightness.Internal(Brightness.ActiveCodes(), friendly); } catch (Exception ex) { App.Log(ex); }
        list.AddRange(panels.Select(p => p.Display));
        try { list.AddRange(Brightness.ExternalCached([.. panels.Select(p => p.Code)], friendly)); } catch (Exception ex) { App.Log(ex); }
        return list;
    }

    public static string? SetBrightness(Display display, int level)
    {
        level = Math.Clamp(level, 0, 100);
        try { return Brightness.Set(display, level); }
        catch (Exception ex) { App.Log(ex); return ex.Message; }
    }

    // --- power ----------------------------------------------------------------------------
    public static PowerModes PowerMode()
    {
        try { return Brightness.Power(); }
        catch (Exception ex) { App.Log(ex); return new PowerModes(Guid.Empty, false); }
    }

    public static string? SetPowerMode(Guid id)
    {
        try
        {
            uint result = Brightness.SetPower(id);
            return result == 0 ? null : $"PowerSetActiveOverlayScheme: 0x{result:X8}";
        }
        catch (Exception ex) { App.Log(ex); return ex.Message; }
    }

    public static Battery? BatteryState()
    {
        try
        {
            var status = Windows.System.Power.PowerManager.BatteryStatus;
            if (status == Windows.System.Power.BatteryStatus.NotPresent) return null;
            string name = status switch
            {
                Windows.System.Power.BatteryStatus.Charging => "charging",
                Windows.System.Power.BatteryStatus.Discharging => "on battery",
                Windows.System.Power.BatteryStatus.Idle => "plugged in",
                _ => status.ToString().ToLowerInvariant(),
            };
            return new Battery(
                Windows.System.Power.PowerManager.RemainingChargePercent, name,
                status == Windows.System.Power.BatteryStatus.Charging,
                Windows.System.Power.PowerManager.EnergySaverStatus == Windows.System.Power.EnergySaverStatus.On);
        }
        catch (Exception ex) { App.Log(ex); return null; }
    }

    // --- night light and do not disturb ---------------------------------------------------
    // Neither has a public API. Night light lives only in an undocumented binary blob in
    // HKCU\...\CloudStore (reverse engineered, so read only); do not disturb is a Focus session
    // (FocusSessionManager, Windows 11 22H2+, where starting one is a Limited Access Feature
    // needing a Microsoft token). So both read what they can and deep-link the rest.

    const string NightLightKey = @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\default$windows.data.bluelightreduction.bluelightreductionstate\windows.data.bluelightreduction.bluelightreductionstate";

    public static bool? NightLight()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(NightLightKey);
            if (key?.GetValue("Data") is byte[] data && data.Length > 18) return data[18] == 0x15;
            return null; // never turned on: nothing to read
        }
        catch { return null; }
    }

    // Windows' own view of whether it should stay quiet (shellapi's SHQueryUserNotificationState).
    // Not the same as do not disturb, but the closest thing a program may ask. Null: the call
    // failed.
    public static string? NotificationState()
    {
        try { return Shell.QueryUserNotificationState() switch { 0 => "not present", 1 => "busy", 2 => "running d3d full screen", 3 => "presentation mode", 4 => "accepts notifications", 5 => "quiet time", 6 => "app (Windows Store) full screen", _ => "unknown" }; }
        catch (Exception ex) { App.Log(ex); return null; }
    }

    // --- the read-only self test's raw material (SystemMenu.exe --qs-selftest) --------------
    // Every radio Windows reports, and every Wi-Fi adapter with its state. Neither call needs
    // consent or writes anything.
    // Everything the page shows, read in one go. The WLAN, WMI and radio calls block, so the page
    // reads it off the UI thread and redraws when it lands (MainWindow.Quick.cs).
    public sealed record QuickState(
        WifiState Wifi, BtState Bluetooth, List<BtDevice> Devices, List<Display> Displays,
        PowerModes Power, Battery? Battery, bool? NightLight, string? Notification, string? Error = null)
    {
        public static readonly QuickState Empty = new(
            new WifiState(false, false, null, 0, null), new BtState(false, false, null), [], [],
            new PowerModes(Guid.Empty, false), null, null, null);
    }

    // Only the sections asked for are read; the rest are kept from `old` (everything is read when
    // there is no old snapshot yet).
    public static QuickState Read(QuickState? old = null, QsPart parts = QsPart.All)
    {
        try
        {
            if (old == null) { old = QuickState.Empty; parts = QsPart.All; }
            var state = old with { Error = null };
            if (parts.HasFlag(QsPart.Wifi)) state = state with { Wifi = Wifi() };
            if (parts.HasFlag(QsPart.Bluetooth)) state = state with { Bluetooth = Bluetooth(), Devices = BluetoothDevices() };
            if (parts.HasFlag(QsPart.Displays)) state = state with { Displays = Displays() };
            if (parts.HasFlag(QsPart.Power)) state = state with { Power = PowerMode(), Battery = BatteryState() };
            if (parts.HasFlag(QsPart.Other)) state = state with { NightLight = NightLight(), Notification = NotificationState() };
            return state;
        }
        catch (Exception ex) { App.Log(ex); return (old ?? QuickState.Empty) with { Error = ex.Message }; }
    }

    // Windows' own notifications — a radio switched on or off (Radio.StateChanged) and the network
    // going up or down (NetworkStatusChanged) — so the page redraws from real state changes and
    // never polls. Each says which section it concerns. Dispose to stop listening.
    public static IDisposable Watch(Action<QsPart> changed)
    {
        var radios = new List<(Windows.Devices.Radios.Radio Radio, Windows.Foundation.TypedEventHandler<Windows.Devices.Radios.Radio, object> Handler)>();
        try
        {
            foreach (var radio in Windows.Devices.Radios.Radio.GetRadiosAsync().GetAwaiter().GetResult())
            {
                var part = radio.Kind == RadioKind.Bluetooth ? QsPart.Bluetooth : radio.Kind == RadioKind.WiFi ? QsPart.Wifi : QsPart.None;
                if (part == QsPart.None) continue;
                Windows.Foundation.TypedEventHandler<Windows.Devices.Radios.Radio, object> handler = (_, _) => changed(part);
                radio.StateChanged += handler;
                radios.Add((radio, handler));
            }
        }
        catch (Exception ex) { App.Log(ex); }
        Windows.Networking.Connectivity.NetworkStatusChangedEventHandler network = _ => changed(QsPart.Wifi);
        Windows.Networking.Connectivity.NetworkInformation.NetworkStatusChanged += network;
        return new Unwatcher(() =>
        {
            foreach (var (radio, handler) in radios) radio.StateChanged -= handler;
            Windows.Networking.Connectivity.NetworkInformation.NetworkStatusChanged -= network;
        });
    }

    sealed class Unwatcher(Action stop) : IDisposable
    {
        public void Dispose() => stop();
    }

    public static List<(string Kind, string State, string Name)> RadioList()
    {
        var list = new List<(string, string, string)>();
        try
        {
            foreach (var radio in Windows.Devices.Radios.Radio.GetRadiosAsync().GetAwaiter().GetResult())
                list.Add((radio.Kind.ToString(), radio.State.ToString(), radio.Name));
        }
        catch (Exception ex) { App.Log(ex); }
        return list;
    }

    public static List<(string Description, string State)> WifiAdapters()
    {
        try { return Wlan.Adapters(); }
        catch (Exception ex) { App.Log(ex); return []; }
    }

    // --- `omawin <command>` (bin/commands.txt: the `app --qs ...` rows) --------------------
    // Each toggle in one call, for a key or the menu. Returns what happened, for the note the
    // app shows; the app runs this off the UI thread.
    public static string Command(string section, string[] rest)
    {
        try
        {
            string action = rest.Length > 0 ? rest[0] : "";
            string argument = rest.Length > 1 ? string.Join(' ', rest[1..]) : "";
            switch (section)
            {
                case "wifi":
                    if (action == "on" || action == "off" || action == "toggle") return WifiRadio(action);
                    if (action == "disconnect") return "Wi-Fi: " + (WifiDisconnect() ?? "disconnected");
                    if (action == "connect") return WifiConnectSaved(argument);
                    return "wifi: on | off | toggle | connect <network> | disconnect";
                case "bluetooth":
                    if (action == "on" || action == "off" || action == "toggle") return BluetoothRadio(action);
                    if (action is "connect" or "disconnect") return BluetoothDevice(argument, action == "connect");
                    return "bluetooth: on | off | toggle | connect <device> | disconnect <device>";
                case "brightness":
                    return BrightnessCommand(action, argument);
                case "power":
                    return PowerCommand(action);
                case "night-light" or "nightlight":
                    Open("ms-settings:nightlight");
                    return "Night light: no public API — opened Settings";
                case "dnd" or "focus":
                    Open("ms-settings:quiethours");
                    return "Do not disturb: no public API — opened Settings";
                case "quick" or "settings":
                    return "Quick settings: Win+A (omawin launcher quick)";
                default:
                    return $"quick settings: no section '{section}'";
            }
        }
        catch (Exception ex) { App.Log(ex); return $"quick settings: {ex.Message}"; }
    }

    static string WifiRadio(string action)
    {
        var before = Wifi();
        if (!before.Present) return "Wi-Fi: no adapter";
        bool wanted = action == "toggle" ? !before.RadioOn : action == "on";
        if (SetWifiRadio(wanted) is { } error) return $"Wi-Fi: {error}";
        return $"Wi-Fi: {(wanted ? "on" : "off")}";
    }

    static string BluetoothRadio(string action)
    {
        var before = Bluetooth();
        if (!before.Present) return "Bluetooth: no adapter";
        bool wanted = action == "toggle" ? !before.RadioOn : action == "on";
        if (SetBluetoothRadio(wanted) is { } error) return $"Bluetooth: {error}";
        return $"Bluetooth: {(wanted ? "on" : "off")}";
    }

    // A command line is visible in process lists, so no password here: a network Windows has
    // already saved connects, anything else has to go through the page (which asks).
    static string WifiConnectSaved(string ssid)
    {
        if (ssid.Length == 0) return "wifi connect: which network?";
        var scan = NearbyNetworks();
        if (scan.Denied) return "Wi-Fi: needs location permission — open Settings › Privacy › Location";
        var network = scan.Networks.FirstOrDefault(n => string.Equals(n.Ssid, ssid, StringComparison.OrdinalIgnoreCase));
        if (network == null) return $"Wi-Fi: '{ssid}' isn't in range";
        if (!network.HasProfile) return $"Wi-Fi: '{ssid}' isn't saved — open Settings › Wi-Fi to join it";
        return "Wi-Fi: " + (WifiConnect(network.Ssid, false, null, network.Profile) ?? $"connecting to {network.Ssid}");
    }

    static string BluetoothDevice(string name, bool connect)
    {
        if (name.Length == 0) return "bluetooth: which device?";
        var device = BluetoothDevices().FirstOrDefault(d => d.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
        if (device == null) return $"Bluetooth: no paired device matching '{name}'";
        var error = BluetoothConnect(device, connect);
        return error == null ? $"Bluetooth: {device.Name} {(connect ? "connected" : "disconnected")}" : $"Bluetooth: {error}";
    }

    static string BrightnessCommand(string action, string argument)
    {
        var displays = Displays().Where(d => d.Writable).ToList();
        if (displays.Count == 0) return "Brightness: no monitor can be set";
        // Up and down never reach 0 (a black panel is hard to find your way back from); only an
        // explicit `set 0` does.
        int lowest = displays.Min(d => d.Current);
        int? level = action switch
        {
            "up" => Math.Min(100, Math.Max(5, displays.Max(d => d.Current) + 10)),
            "down" => Math.Max(Math.Min(5, lowest), lowest - 10),
            "set" => int.TryParse(argument, out int value) && value is >= 0 and <= 100 ? value : null,
            _ => null,
        };
        if (level == null) return "brightness: up | down | set <0-100>";
        var results = new List<string>();
        foreach (var display in displays)
        {
            var error = SetBrightness(display, level.Value);
            results.Add(error == null ? $"{display.Name} {level}%" : $"{display.Name}: {error}");
        }
        return "Brightness: " + string.Join(", ", results);
    }

    static string PowerCommand(string action)
    {
        var mode = PowerMode();
        var id = action switch
        {
            "efficiency" or "saver" or "best-power-efficiency" => Brightness.PowerEfficiency,
            "balanced" or "auto" => Brightness.Balanced,
            "performance" or "best-performance" => Brightness.Performance,
            "cycle" => mode.Current == Brightness.Performance ? Brightness.Balanced : Brightness.IsBalanced(mode.Current) ? Brightness.PowerEfficiency : Brightness.Performance,
            _ => Guid.Empty,
        };
        if (action.Length == 0 || (id == Guid.Empty && action != "balanced" && action != "auto"))
            return "power: efficiency | balanced | performance | cycle";
        var error = SetPowerMode(id);
        return error == null ? $"Power mode: {Brightness.PowerName(id)}" : $"Power mode: {error}";
    }
}

// --- Radios: Windows.Devices.Radios, the supported way to turn Wi-Fi and Bluetooth on and off
// (the Settings toggles use it). Async; call from a background thread.
static class Radios
{
    static async Task<Windows.Devices.Radios.Radio?> Find(Windows.Devices.Radios.RadioKind kind)
    {
        foreach (var radio in await Windows.Devices.Radios.Radio.GetRadiosAsync())
            if (radio.Kind == kind) return radio;
        return null;
    }

    public static bool IsOn(Windows.Devices.Radios.RadioKind kind)
    {
        var radio = Find(kind).GetAwaiter().GetResult();
        return radio?.State == Windows.Devices.Radios.RadioState.On;
    }

    // Null when the radio reached the state asked for; else why not. Allowed only means Windows
    // accepted the request: the driver can take a moment to get there, so the state is read
    // again for up to a second before calling it a failure.
    public static string? Set(Windows.Devices.Radios.RadioKind kind, bool on)
    {
        try
        {
            var radio = Find(kind).GetAwaiter().GetResult();
            if (radio == null) return "no radio";
            var wanted = on ? Windows.Devices.Radios.RadioState.On : Windows.Devices.Radios.RadioState.Off;
            var status = radio.SetStateAsync(wanted).GetAwaiter().GetResult();
            if (status is Windows.Devices.Radios.RadioAccessStatus.DeniedByUser or Windows.Devices.Radios.RadioAccessStatus.DeniedBySystem)
                return "Windows denied the change: check Settings › Privacy › Radios";
            if (status != Windows.Devices.Radios.RadioAccessStatus.Allowed) return $"Windows didn't allow it ({status})";
            for (int i = 0; i < 10; i++)
            {
                if (Find(kind).GetAwaiter().GetResult()?.State == wanted) return null;
                Thread.Sleep(100);
            }
            return "the adapter didn't take it";
        }
        catch (Exception ex) { App.Log(ex); return ex.Message; }
    }
}

// --- Bluetooth: paired devices through Windows.Devices.Bluetooth (no consent needed), and the
// audio services through bthprops.cpl for connect / disconnect.
static class Bt
{
    public static bool Present { get; private set; } = true;

    // Classic Bluetooth and Bluetooth LE are two separate device stacks, with two separate
    // selectors: a Bluetooth LE mouse is paired but appears in neither the classic list nor
    // BluetoothDevice at all (Get-PnpDevice shows it as BTHLE\DEV_… next to BTHENUM\DEV_…).
    // Ask for both and merge on the address. Needs no capability and no admin.
    public static List<BtDevice> Paired()
    {
        var found = new Dictionary<ulong, BtDevice>();
        try
        {
            var classic = Windows.Devices.Bluetooth.BluetoothDevice.GetDeviceSelectorFromPairingState(true);
            foreach (var info in Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(classic).GetAwaiter().GetResult())
                Add(found, info.Id, info.Name, le: false);
            var le = Windows.Devices.Bluetooth.BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
            foreach (var info in Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(le).GetAwaiter().GetResult())
                Add(found, info.Id, info.Name, le: true);
            Present = true;
        }
        catch (Exception ex) { App.Log(ex); Present = false; } // no Bluetooth radio at all
        return [.. found.Values.OrderByDescending(d => d.Connected).ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)];
    }

    static void Add(Dictionary<ulong, BtDevice> found, string id, string fallbackName, bool le)
    {
        try
        {
            ulong address; string name; bool connected;
            if (le)
            {
                using var device = Windows.Devices.Bluetooth.BluetoothLEDevice.FromIdAsync(id).GetAwaiter().GetResult();
                if (device == null) return;
                address = device.BluetoothAddress; name = device.Name;
                connected = device.ConnectionStatus == Windows.Devices.Bluetooth.BluetoothConnectionStatus.Connected;
            }
            else
            {
                using var device = Windows.Devices.Bluetooth.BluetoothDevice.FromIdAsync(id).GetAwaiter().GetResult();
                if (device == null) return;
                address = device.BluetoothAddress; name = device.Name;
                connected = device.ConnectionStatus == Windows.Devices.Bluetooth.BluetoothConnectionStatus.Connected;
            }
            if (string.IsNullOrEmpty(name)) name = fallbackName;
            if (name.Length == 0) return;
            // Classic wins over LE when a dual-mode device is paired on both stacks.
            if (found.TryGetValue(address, out var already) && (!already.Le || !le)) return;
            found[address] = new BtDevice(name, address, connected, le, le ? null : HandsfreeState(address));
        }
        catch (Exception ex) { App.Log(ex); }
    }

    // A2DP audio sink, and Handsfree: the two services a headset or speaker uses.
    static readonly Guid AudioSink = new("0000110B-0000-1000-8000-00805F9B34FB");
    static readonly Guid Handsfree = new("0000111E-0000-1000-8000-00805F9B34FB");
    const uint ServiceEnable = 0x00000001, ServiceDisable = 0x00000000;
    const uint ErrorInvalidParameter = 87, ErrorServiceDoesNotExist = 1060, EInvalidArg = 0x80070057;

    [StructLayout(LayoutKind.Sequential)] struct FindRadioParams { public uint dwSize; }
    [StructLayout(LayoutKind.Sequential)] struct SystemTime { public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DeviceInfo
    {
        public uint dwSize; public ulong Address; public uint ClassOfDevice;
        [MarshalAs(UnmanagedType.Bool)] public bool Connected, Remembered, Authenticated;
        public SystemTime LastSeen, LastUsed;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)] public string Name;
    }

    [DllImport("bthprops.cpl", SetLastError = true)] static extern IntPtr BluetoothFindFirstRadio(ref FindRadioParams parameters, out IntPtr radio);
    [DllImport("bthprops.cpl")] static extern bool BluetoothFindRadioClose(IntPtr find);
    [DllImport("bthprops.cpl")] static extern uint BluetoothSetServiceState(IntPtr radio, ref DeviceInfo device, ref Guid service, uint flags);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("bthprops.cpl")] static extern uint BluetoothEnumerateInstalledServices(IntPtr radio, ref DeviceInfo device, ref uint count, [Out] Guid[] services);

    // --- hands-free (roadmap 8.49) ------------------------------------------------------------
    // When any app opens a Bluetooth headset's microphone, Windows switches the headset to its
    // hands-free profile: mono, call quality, for everything. With hands-free off the headset
    // stays in stereo and another microphone is used. Windows can't tell "turned off" from "never
    // had it", so the headsets omawin turned it off for are remembered, to offer it back.
    static string HandsfreeOffFile => Path.Combine(MenuModel.StateDir, "bt-handsfree-off");

    static HashSet<ulong> TurnedOff()
    {
        try
        {
            return File.Exists(HandsfreeOffFile)
                ? File.ReadAllLines(HandsfreeOffFile).Select(l => ulong.TryParse(l.Trim(), System.Globalization.NumberStyles.HexNumber, null, out var a) ? a : 0).Where(a => a != 0).ToHashSet()
                : [];
        }
        catch (IOException) { return []; }
    }

    // On: the headset has hands-free running; off: omawin turned it off; null: it has none.
    static bool? HandsfreeState(ulong address)
    {
        var services = InstalledServices(address);
        if (services == null) return null;
        return services.Contains(Handsfree) ? true : TurnedOff().Contains(address) ? false : null;
    }

    static List<Guid>? InstalledServices(ulong address)
    {
        var parameters = new FindRadioParams { dwSize = (uint)Marshal.SizeOf<FindRadioParams>() };
        var find = BluetoothFindFirstRadio(ref parameters, out var radio);
        if (find == IntPtr.Zero || radio == IntPtr.Zero) return null;
        try
        {
            var device = new DeviceInfo { dwSize = (uint)Marshal.SizeOf<DeviceInfo>(), Address = address, Name = "" };
            var services = new Guid[32];
            uint count = (uint)services.Length;
            return BluetoothEnumerateInstalledServices(radio, ref device, ref count, services) == 0 ? [.. services.Take((int)Math.Min(count, (uint)services.Length))] : null;
        }
        finally
        {
            BluetoothFindRadioClose(find);
            CloseHandle(radio);
        }
    }

    public static string? SetHandsfree(ulong address, bool on)
    {
        var parameters = new FindRadioParams { dwSize = (uint)Marshal.SizeOf<FindRadioParams>() };
        var find = BluetoothFindFirstRadio(ref parameters, out var radio);
        if (find == IntPtr.Zero || radio == IntPtr.Zero) return "No Bluetooth radio";
        try
        {
            var device = new DeviceInfo { dwSize = (uint)Marshal.SizeOf<DeviceInfo>(), Address = address, Name = "" };
            var guid = Handsfree;
            uint result = BluetoothSetServiceState(radio, ref device, ref guid, on ? ServiceEnable : ServiceDisable);
            if (result == ErrorServiceDoesNotExist) return "this device has no hands-free service";
            if (result is not (0 or ErrorInvalidParameter or EInvalidArg)) return $"BluetoothSetServiceState: error {result}";
        }
        finally
        {
            BluetoothFindRadioClose(find);
            CloseHandle(radio);
        }
        var off = TurnedOff();
        if (on ? off.Remove(address) : off.Add(address))
        {
            Directory.CreateDirectory(MenuModel.StateDir);
            File.WriteAllLines(HandsfreeOffFile, off.Select(a => a.ToString("X12")));
        }
        return null;
    }

    public static string? SetAudioServices(ulong address, bool connect)
    {
        var parameters = new FindRadioParams { dwSize = (uint)Marshal.SizeOf<FindRadioParams>() };
        var find = BluetoothFindFirstRadio(ref parameters, out var radio);
        if (find == IntPtr.Zero || radio == IntPtr.Zero) return "No Bluetooth radio";
        try
        {
            var device = new DeviceInfo { dwSize = (uint)Marshal.SizeOf<DeviceInfo>(), Address = address, Name = "" };
            uint flags = connect ? ServiceEnable : ServiceDisable;
            var errors = new List<string>();
            // Enabling the sink first brings the device up; disabling it first (Handsfree may be
            // in use) drops it. The return is a Win32 code (a DWORD), not an HRESULT: the docs
            // give E_INVALIDARG for "already in that state", but in practice it arrives as its
            // Win32 form, ERROR_INVALID_PARAMETER (87; our flags are always valid, which is the
            // other thing 87 means), so both count as done. ERROR_SERVICE_DOES_NOT_EXIST (1060)
            // is a device without that service, fine as long as the other one works.
            int done = 0;
            foreach (var service in connect ? new[] { AudioSink, Handsfree } : new[] { Handsfree, AudioSink })
            {
                var guid = service;
                uint result = BluetoothSetServiceState(radio, ref device, ref guid, flags);
                if (result is 0 or ErrorInvalidParameter or EInvalidArg) done++;
                else if (result != ErrorServiceDoesNotExist) errors.Add($"error {result}");
            }
            if (done > 0) return null;
            return errors.Count > 0 ? $"BluetoothSetServiceState failed ({string.Join(", ", errors)})" : "the device has no audio service to connect";
        }
        finally
        {
            BluetoothFindRadioClose(find);
            CloseHandle(radio);
        }
    }
}

// --- the Win32 WLAN API (wlanapi.dll), for the adapter list, the nearby networks and connect.
// All of it is documented in wlanapi.h; the ones that need precise-location consent return
// ERROR_ACCESS_DENIED (5) on Windows 11 22H2 and later, which callers turn into "denied".
static class Wlan
{
    [StructLayout(LayoutKind.Sequential)] struct Ssid { public uint Length; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Bytes; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct AvailableNetwork
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string ProfileName;
        public Ssid Ssid;
        public uint BssType, NumberOfBssids;
        [MarshalAs(UnmanagedType.Bool)] public bool Connectable;
        public uint NotConnectableReason, NumberOfPhyTypes;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public uint[] PhyTypes;
        [MarshalAs(UnmanagedType.Bool)] public bool MorePhyTypes;
        public uint SignalQuality;
        [MarshalAs(UnmanagedType.Bool)] public bool SecurityEnabled;
        public uint DefaultAuthAlgorithm, DefaultCipherAlgorithm, Flags, Reserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct InterfaceInfo
    {
        public Guid InterfaceGuid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Description;
        public uint State;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ConnectionParameters
    {
        public int Mode;          // wlan_connection_mode_profile
        public IntPtr Profile;    // LPCWSTR
        public IntPtr Ssid;       // PDOT11_SSID (null: whichever BSS the profile allows)
        public IntPtr BssidList;
        public int BssType;       // dot11_BSS_type_any
        public uint Flags;
    }

    const uint AccessDenied = 5;
    const uint ProfileUser = 0x00000002; // a per-user profile: no admin needed
    const int OpcodeCurrentConnection = 7; // wlan_intf_opcode_current_connection

    [DllImport("wlanapi.dll")] static extern uint WlanOpenHandle(uint version, IntPtr reserved, out uint negotiated, out IntPtr handle);
    [DllImport("wlanapi.dll")] static extern uint WlanCloseHandle(IntPtr handle, IntPtr reserved);
    [DllImport("wlanapi.dll")] static extern uint WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr list);
    [DllImport("wlanapi.dll")] static extern uint WlanGetAvailableNetworkList(IntPtr handle, ref Guid iface, uint flags, IntPtr reserved, out IntPtr list);
    [DllImport("wlanapi.dll")] static extern uint WlanScan(IntPtr handle, ref Guid iface, IntPtr ssid, IntPtr ies, IntPtr reserved);
    [DllImport("wlanapi.dll")] static extern uint WlanConnect(IntPtr handle, ref Guid iface, ref ConnectionParameters parameters, IntPtr reserved);
    [DllImport("wlanapi.dll")] static extern uint WlanDisconnect(IntPtr handle, ref Guid iface, IntPtr reserved);
    [DllImport("wlanapi.dll")] static extern uint WlanSetProfile(IntPtr handle, ref Guid iface, uint flags, [MarshalAs(UnmanagedType.LPWStr)] string xml, IntPtr security, bool overwrite, IntPtr reserved, out uint reason);
    [DllImport("wlanapi.dll")] static extern void WlanFreeMemory(IntPtr memory);

    // A handle to the WLAN service, and the interfaces it reports. `using` closes the handle.
    sealed class Session : IDisposable
    {
        public IntPtr Handle;
        public List<InterfaceInfo> Interfaces = [];
        public uint OpenError;

        public Session()
        {
            OpenError = WlanOpenHandle(2, IntPtr.Zero, out _, out Handle);
            if (OpenError != 0) return;
            if (WlanEnumInterfaces(Handle, IntPtr.Zero, out var list) != 0 || list == IntPtr.Zero) return;
            try
            {
                int count = Marshal.ReadInt32(list);
                // The header is two DWORDs; the entries follow, each a fixed 532 bytes.
                int size = Marshal.SizeOf<InterfaceInfo>();
                for (int i = 0; i < count; i++)
                    Interfaces.Add(Marshal.PtrToStructure<InterfaceInfo>(list + 8 + i * size));
            }
            finally { WlanFreeMemory(list); }
        }

        public Guid? First => Interfaces.Count > 0 ? Interfaces[0].InterfaceGuid : null;
        public void Dispose() { if (Handle != IntPtr.Zero) WlanCloseHandle(Handle, IntPtr.Zero); }
    }

    public static int AdapterCount()
    {
        using var session = new Session();
        return session.Interfaces.Count;
    }

    // the adapter's name and its state, as netsh wlan show interfaces has them.
    public static List<(string Description, string State)> Adapters()
    {
        using var session = new Session();
        return [.. session.Interfaces.Select(i => (i.Description, InterfaceState(i.State)))];
    }

    static string InterfaceState(uint state) => state switch
    {
        0 => "not ready", 1 => "connected", 2 => "ad hoc network formed", 3 => "disconnecting",
        4 => "disconnected", 5 => "associating", 6 => "discovering", 7 => "authenticating", _ => $"unknown ({state})",
    };

    public static (List<WifiNetwork> Networks, bool Denied, string? Error) ScanAndList(bool scan)
    {
        using var session = new Session();
        if (session.OpenError != 0) return ([], false, $"WlanOpenHandle: {session.OpenError}");
        if (session.First is not { } iface) return ([], false, "No Wi-Fi adapter");
        if (scan)
        {
            // The scan runs in the driver: wait for its scan-complete (or scan-failed)
            // notification rather than list what Windows had cached. Drivers must finish a scan
            // within 4 seconds, so that is the longest wait; after it, the list is what it is.
            using var finished = new ManualResetEventSlim(false);
            var target = iface;
            NotificationCallback callback = (data, _) =>
            {
                if (data == IntPtr.Zero) return;
                var n = Marshal.PtrToStructure<NotificationData>(data);
                if (n.Source == SourceAcm && n.Code is AcmScanComplete or AcmScanFail && n.InterfaceGuid == target) finished.Set();
            };
            bool listening = WlanRegisterNotification(session.Handle, SourceAcm, true, callback, IntPtr.Zero, IntPtr.Zero, out _) == 0;
            try
            {
                uint started = WlanScan(session.Handle, ref iface, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                if (started == AccessDenied) return ([], true, null);
                if (started == 0) finished.Wait(listening ? 4000 : 2000);
            }
            finally
            {
                // Unregister before the callback and the event go away (WLAN waits for a callback
                // in progress to return).
                if (listening) WlanRegisterNotification(session.Handle, SourceNone, true, null, IntPtr.Zero, IntPtr.Zero, out _);
                GC.KeepAlive(callback);
            }
        }
        return List(session, iface);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct NotificationData { public uint Source, Code; public Guid InterfaceGuid; public uint DataSize; public IntPtr Data; }

    delegate void NotificationCallback(IntPtr data, IntPtr context);

    const uint SourceNone = 0, SourceAcm = 0x8;
    const uint AcmScanComplete = 7, AcmScanFail = 8; // wlan_notification_acm_scan_complete / _fail

    [DllImport("wlanapi.dll")] static extern uint WlanRegisterNotification(IntPtr handle, uint source, bool ignoreDuplicate, NotificationCallback? callback, IntPtr context, IntPtr reserved, out uint previous);

    static (List<WifiNetwork> Networks, bool Denied, string? Error) List(Session session, Guid iface)
    {
        uint result = WlanGetAvailableNetworkList(session.Handle, ref iface, 0, IntPtr.Zero, out var list);
        if (result == AccessDenied) return ([], true, null);
        if (result != 0 || list == IntPtr.Zero) return ([], false, $"WlanGetAvailableNetworkList: {result}");
        try
        {
            int count = Marshal.ReadInt32(list);
            int size = Marshal.SizeOf<AvailableNetwork>();
            var networks = new List<WifiNetwork>();
            for (int i = 0; i < count; i++)
            {
                var network = Marshal.PtrToStructure<AvailableNetwork>(list + 8 + i * size);
                string ssid = Encoding.UTF8.GetString(network.Ssid.Bytes, 0, (int)Math.Min(network.Ssid.Length, 32));
                if (ssid.Length == 0) continue; // a hidden network: nothing to show
                networks.Add(new WifiNetwork(ssid, (int)network.SignalQuality,
                    (network.Flags & 0x1) != 0, network.ProfileName.Length > 0, network.SecurityEnabled, network.Connectable, network.ProfileName));
            }
            // A network with a saved profile is listed twice (once per profile, once bare): keep
            // one, the connected or saved entry first, so its profile name is the one used.
            return ([.. networks
                .GroupBy(n => n.Ssid)
                .Select(g => g.OrderByDescending(n => n.Connected).ThenByDescending(n => n.HasProfile).ThenByDescending(n => n.Signal).First())
                .OrderByDescending(n => n.Connected).ThenByDescending(n => n.Signal).ThenBy(n => n.Ssid, StringComparer.OrdinalIgnoreCase)], false, null);
        }
        finally { WlanFreeMemory(list); }
    }

    // profileName: the saved profile to connect with (wlan_connection_mode_profile takes a
    // profile's name, not an SSID).
    public static string? Connect(string profileName)
    {
        using var session = new Session();
        if (session.First is not { } iface) return "No Wi-Fi adapter";
        var profile = Marshal.StringToHGlobalUni(profileName);
        try
        {
            var parameters = new ConnectionParameters { Mode = 0, Profile = profile, BssType = 3 /* any: whichever band the profile allows */ };
            uint result = WlanConnect(session.Handle, ref iface, ref parameters, IntPtr.Zero);
            return result == 0 ? null : $"WlanConnect: {result}";
        }
        finally { Marshal.FreeHGlobal(profile); }
    }

    public static string? Disconnect()
    {
        using var session = new Session();
        if (session.First is not { } iface) return "No Wi-Fi adapter";
        uint result = WlanDisconnect(session.Handle, ref iface, IntPtr.Zero);
        return result == 0 ? null : $"WlanDisconnect: {result}";
    }

    // The profile a new network needs, as a per-user profile. WPA2/WPA3 personal only: an
    // enterprise (802.1X) network has to be set up in Windows' own Wi-Fi settings.
    public static void SetProfile(string ssid, string password)
    {
        using var session = new Session();
        if (session.First is not { } iface) throw new InvalidOperationException("No Wi-Fi adapter");
        string xml = $"""
            <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
            	<name>{Escape(ssid)}</name>
            	<SSIDConfig><SSID><name>{Escape(ssid)}</name></SSID></SSIDConfig>
            	<connectionType>ESS</connectionType>
            	<connectionMode>auto</connectionMode>
            	<MSM><security>
            		<authEncryption><authentication>WPA2PSK</authentication><encryption>AES</encryption><useOneX>false</useOneX></authEncryption>
            		<sharedKey><keyType>passPhrase</keyType><protected>false</protected><keyMaterial>{Escape(password)}</keyMaterial></sharedKey>
            	</security></MSM>
            </WLANProfile>
            """;
        uint result = WlanSetProfile(session.Handle, ref iface, ProfileUser, xml, IntPtr.Zero, true, IntPtr.Zero, out uint reason);
        if (result != 0) throw new InvalidOperationException($"WlanSetProfile: {result} (reason {reason})");
    }

    static string Escape(string text) => System.Security.SecurityElement.Escape(text) ?? "";
}

// --- brightness: WMI for the built-in panel, dxva2's DDC/CI for external monitors.
static class Brightness
{
    // The built-in panel. WmiMonitorBrightness's CurrentBrightness is 0..100. WMI reports the
    // panel whether or not it is a screen anyone can see (with the lid shut it still answers,
    // at whatever level it was left), so only the panels that are actually active are listed.
    // Each comes with its EDID code, which External() uses to skip the same panel over dxva2.
    public static List<(Display Display, string Code)> Internal(HashSet<string> active, Dictionary<string, string> friendly)
    {
        var list = new List<(Display, string)>();
        using var wmi = Wmi.Connect(@"root\wmi");
        foreach (var instance in wmi.Query("SELECT CurrentBrightness, InstanceName FROM WmiMonitorBrightness"))
            using (instance)
                try
                {
                    int current = Convert.ToInt32(instance["CurrentBrightness"]);
                    string code = Short(instance["InstanceName"] as string ?? "");
                    if (active.Count > 0 && !active.Contains(code)) continue;
                    list.Add((new Display(Label(code, friendly), true, current, 0, 100, true), code));
                }
                catch (Exception ex) { App.Log(ex); }
        return list;
    }

    // "ABC1234" -> "Example 27", from the panel's own EDID. dxva2 usually answers only
    // "Generic PnP Monitor" for a monitor's description, so this gives the screen its real name.
    public static Dictionary<string, string> FriendlyNames()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var wmi = Wmi.Connect(@"root\wmi");
            foreach (var instance in wmi.Query("SELECT InstanceName, UserFriendlyName FROM WmiMonitorID"))
                using (instance)
                    try
                    {
                        string code = Short(instance["InstanceName"] as string ?? "");
                        if (code.Length == 0 || instance["UserFriendlyName"] is not int[] letters) continue;
                        string text = new([.. letters.TakeWhile(c => c != 0).Select(c => (char)c)]);
                        if (text.Trim().Length > 0) names[code] = text.Trim();
                    }
                    catch (Exception ex) { App.Log(ex); }
        }
        catch (Exception ex) { App.Log(ex); }
        return names;
    }

    // What to call a screen: its EDID name when there is one, else the code, else what dxva2 said.
    static string Label(string? code, Dictionary<string, string> friendly)
    {
        if (code != null && friendly.TryGetValue(code, out var name)) return name;
        return code is { Length: > 0 } ? code : "Display";
    }

    // The EDID codes ("LEN1234", "ABC1234") of the displays attached and active right now, from
    // each output's MONITOR\… device. What WMI calls the panel and what the display output calls
    // it is the same code, which is how a built-in panel is told from a real external monitor.
    public static HashSet<string> ActiveCodes()
    {
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            if (DisplayCode(monitor) is { } code) codes.Add(code);
            return true;
        }, IntPtr.Zero);
        return codes;
    }

    public static string? Set(Display display, int level) => display.Internal ? SetInternal(level) : SetExternal(display, level);

    static string? SetInternal(int level)
    {
        using var wmi = Wmi.Connect(@"root\wmi");
        foreach (var instance in wmi.Query("SELECT __PATH FROM WmiMonitorBrightnessMethods"))
            using (instance)
                try
                {
                    wmi.Call(instance["__PATH"] as string ?? "", "WmiMonitorBrightnessMethods", "WmiSetBrightness",
                        ("Timeout", Variant.FromInt(0)), // wait forever: the panel answers at once
                        ("Brightness", Variant.FromByte((byte)Math.Clamp(level, 0, 100))));
                }
                catch (Exception ex) { App.Log(ex); return ex.Message; }
        return null;
    }

    // --- dxva2 ---------------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PhysicalMonitor
    {
        public IntPtr Handle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
    }

    delegate bool MonitorEnum(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnum callback, IntPtr data);
    [DllImport("dxva2.dll")] static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);
    [DllImport("dxva2.dll", CharSet = CharSet.Unicode)] static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, [Out] PhysicalMonitor[] monitors);
    [DllImport("dxva2.dll")] static extern bool GetMonitorCapabilities(IntPtr monitor, out uint capabilities, out uint temperatures);
    [DllImport("dxva2.dll")] static extern bool GetMonitorBrightness(IntPtr monitor, out uint min, out uint current, out uint max);
    [DllImport("dxva2.dll")] static extern bool SetMonitorBrightness(IntPtr monitor, uint level);
    [DllImport("dxva2.dll")] static extern bool SetVCPFeature(IntPtr monitor, byte code, uint value);
    [DllImport("dxva2.dll")] static extern bool DestroyPhysicalMonitors(uint count, [In] PhysicalMonitor[] monitors);

    const uint MC_CapsBrightness = 0x1;

    // Every physical monitor behind every display output. The handles dxva2 hands out are
    // Windows resources that stay open until DestroyPhysicalMonitors, so this is `using`d and
    // gives them back when the read or the write is done.
    sealed class PhysicalMonitors : IDisposable
    {
        readonly List<PhysicalMonitor[]> arrays = [];
        public readonly List<(IntPtr Monitor, IntPtr Handle, string Name)> All = [];

        public PhysicalMonitors()
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
            {
                if (!GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out uint count) || count == 0) return true;
                var monitors = new PhysicalMonitor[count];
                if (!GetPhysicalMonitorsFromHMONITOR(monitor, count, monitors)) return true;
                arrays.Add(monitors);
                foreach (var m in monitors) All.Add((monitor, m.Handle, m.Description));
                return true;
            }, IntPtr.Zero);
        }

        public void Dispose()
        {
            foreach (var monitors in arrays)
                try { DestroyPhysicalMonitors((uint)monitors.Length, monitors); } catch (Exception ex) { App.Log(ex); }
            arrays.Clear();
            All.Clear();
        }
    }

    // DDC/CI is one slow serial line per monitor: a read and a write at the same time can garble
    // each other, so every dxva2 conversation holds this.
    static readonly object Ddc = new();

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplayDevices(string device, uint index, ref DisplayDevice info, uint flags);

    // "\\.\DISPLAY1" -> "LEN1234", the EDID code the built-in panel's WMI instance also carries
    // ("DISPLAY\LEN1234\…"). Null when the output has no monitor attached.
    static string? DisplayCode(IntPtr monitor)
    {
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return null;
        var device = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
        if (!EnumDisplayDevices(info.Device, 0, ref device, 0)) return null;
        var parts = device.DeviceID.Split('\\');
        return parts.Length >= 2 ? parts[1] : null;
    }

    // The external monitors as last read. Asking a monitor over DDC/CI for its capabilities and
    // level takes a few hundred ms each, too slow for every redraw, so the answer is kept until
    // the displays change (WM_DISPLAYCHANGE, via SystemEvents.DisplaySettingsChanged: a screen
    // plugged in or out, a resolution changed) or it is 30 s old; a write updates it in place.
    static List<Display>? externalCache;
    static DateTime externalAt;
    static bool watchingDisplays;

    public static List<Display> ExternalCached(HashSet<string> builtInCodes, Dictionary<string, string> friendly)
    {
        lock (Ddc)
        {
            if (!watchingDisplays)
            {
                watchingDisplays = true;
                // Subscribed from a worker thread, so SystemEvents keeps its own hidden window
                // and message loop for it.
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) => Forget();
            }
            if (externalCache != null && DateTime.UtcNow - externalAt < TimeSpan.FromSeconds(30)) return externalCache;
            externalCache = External(builtInCodes, friendly);
            externalAt = DateTime.UtcNow;
            return externalCache;
        }
    }

    public static void Forget()
    {
        lock (Ddc) externalCache = null;
    }

    // builtInCodes: the EDID codes of the panels the WMI path already covers. A laptop panel
    // answers dxva2 too (as a second physical monitor, usually with nothing behind it), so a
    // screen whose code is one of these is skipped instead of shown twice.
    static List<Display> External(HashSet<string> builtInCodes, Dictionary<string, string> friendly)
    {
        var builtIn = new HashSet<string>(builtInCodes, StringComparer.OrdinalIgnoreCase);
        var list = new List<Display>();
        lock (Ddc)
        {
            using var monitors = new PhysicalMonitors();
            foreach (var (monitor, handle, name) in monitors.All)
            {
                try
                {
                    var code = DisplayCode(monitor);
                    if (code != null && builtIn.Contains(code)) continue;
                    if (!GetMonitorBrightness(handle, out uint min, out uint current, out uint max)) continue;
                    if (max <= min) continue;
                    // A monitor is only worth listing when it says it has the brightness control
                    // (MC_CAPS_BRIGHTNESS), or — some monitors don't answer that call — the DDC/CI
                    // brightness feature itself replies.
                    bool capable = GetMonitorCapabilities(handle, out uint capabilities, out _)
                        ? (capabilities & MC_CapsBrightness) != 0
                        : GetVCPFeatureAndVCPFeatureReply(handle, 0x10, out _, out _, out _);
                    if (!capable) continue;
                    string label = string.IsNullOrWhiteSpace(name) || name.StartsWith("Generic", StringComparison.OrdinalIgnoreCase)
                        ? Label(code, friendly) : name;
                    list.Add(new Display(label, false, Percent(current, min, max), 0, 100, true, monitor.ToInt64()));
                }
                catch (Exception ex) { App.Log(ex); }
            }
        }
        return list;
    }

    [DllImport("dxva2.dll")] static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr monitor, byte code, out uint type, out uint current, out uint max);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MonitorInfo
    {
        public uint Size; public Rect Monitor, Work; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    // Every physical monitor, as dxva2 answers, for the self test: shows the ones External()
    // leaves out and why (no brightness capability, or an empty range), and where each one is.
    public static List<(string Name, uint Min, uint Current, uint Max, string Capabilities)> Raw()
    {
        var list = new List<(string, uint, uint, uint, string)>();
        lock (Ddc)
        {
            using var monitors = new PhysicalMonitors();
            foreach (var (monitor, handle, name) in monitors.All)
            {
                try
                {
                    var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
                    string code = DisplayCode(monitor) ?? "no monitor attached";
                    string where = "where unknown";
                    if (GetMonitorInfo(monitor, ref info))
                    {
                        string primary = (info.Flags & 1) != 0 ? " primary" : "";
                        where = $"{info.Device}{primary} {info.Monitor.Right - info.Monitor.Left}x{info.Monitor.Bottom - info.Monitor.Top}+{info.Monitor.Left}+{info.Monitor.Top} code {code}";
                    }
                    string capabilities = "capabilities unreadable";
                    if (GetMonitorCapabilities(handle, out uint caps, out _))
                    {
                        string brightness = (caps & MC_CapsBrightness) != 0 ? "brightness" : "no brightness";
                        capabilities = $"0x{caps:X8} ({brightness})";
                    }
                    // The same question asked the other way: a DDC/CI brightness feature (VCP 0x10).
                    capabilities += GetVCPFeatureAndVCPFeatureReply(handle, 0x10, out _, out uint vcp, out uint vcpMax)
                        ? $", vcp 0x10 {vcp}..{vcpMax}" : ", vcp 0x10 no reply";
                    list.Add(GetMonitorBrightness(handle, out uint min, out uint current, out uint max)
                        ? ($"{name} @ {where}", min, current, max, capabilities)
                        : ($"{name} @ {where}", 0, 0, 0, capabilities + ", brightness unreadable"));
                }
                catch (Exception ex) { App.Log(ex); }
            }
        }
        return list;
    }

    static string? SetExternal(Display display, int level)
    {
        lock (Ddc)
        {
            using var monitors = new PhysicalMonitors();
            foreach (var (monitor, handle, _) in monitors.All)
            {
                // Match on the monitor's HMONITOR, which stays put while a screen is plugged in:
                // the physical-monitor handle is re-made on every call, and two screens can share
                // one description ("Generic PnP Monitor"), so the name can't tell them apart.
                if (monitor.ToInt64() != display.Key) continue;
                if (!GetMonitorBrightness(handle, out uint min, out _, out uint max)) continue;
                uint raw = (uint)Math.Round(min + (max - min) * (level / 100.0));
                // Some monitors only take the raw VCP write.
                if (!SetMonitorBrightness(handle, raw) && !SetVCPFeature(handle, 0x10, raw)) return "The monitor refused the brightness change";
                // The cached read now knows the new level, so a redraw doesn't show the old one.
                if (externalCache != null)
                    externalCache = [.. externalCache.Select(d => d.Key == display.Key ? d with { Current = level } : d)];
                return null;
            }
        }
        return "Monitor not found";
    }

    static int Percent(uint value, uint min, uint max) => max <= min ? 100 : (int)Math.Round((value - min) * 100.0 / (max - min));

    // "DISPLAY\ABC1234\4&0000000&0&UID0000_0" -> "ABC1234"
    static string Short(string instance)
    {
        var parts = instance.Split('\\');
        return parts.Length >= 2 ? parts[1] : instance;
    }

    // --- power mode (undocumented powrprof) ------------------------------------------------
    // The Settings "Power mode" slider: an overlay scheme on top of the power plan. Documented
    // only as PowerGet/SetUserConfigured{AC,DC}PowerMode (the configured value); the effective
    // one is these two, which every power-mode tool calls.
    [DllImport("powrprof.dll")] static extern uint PowerGetEffectiveOverlayScheme(out Guid scheme);
    [DllImport("powrprof.dll")] static extern uint PowerSetActiveOverlayScheme(Guid scheme);

    // The overlay GUIDs, from Microsoft's "Customize the Windows performance power slider"
    // (Better battery / Better performance / Best performance). Windows 11's "Balanced" is the
    // slider's Better performance position, and the effective overlay reads either as that
    // GUID or as no overlay at all (Guid.Empty: the plain Balanced plan), depending on the PC
    // and on how it was last set. Both mean Balanced here.
    public static readonly Guid PowerEfficiency = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
    public static readonly Guid Balanced = Guid.Empty;
    public static readonly Guid BetterPerformance = new("3af9b8d9-7c97-431d-ad78-34a8bfea439f");
    public static readonly Guid Performance = new("ded574b5-45a0-4f42-8737-46345c09c238");

    public static bool IsBalanced(Guid id) => id == Balanced || id == BetterPerformance;

    public static PowerModes Power()
    {
        uint result = PowerGetEffectiveOverlayScheme(out var scheme);
        return new PowerModes(IsBalanced(scheme) ? Balanced : scheme, result == 0, scheme);
    }

    // Setting Balanced keeps writing Guid.Empty (no overlay), as this code always has and as the
    // open-source power-mode tools do: it clears whichever overlay is on, which is the Balanced
    // plan itself, and needs no OEM overlay to exist (an OEM may leave Better performance
    // undefined). It reads back as Balanced either way (IsBalanced).
    public static uint SetPower(Guid scheme) => PowerSetActiveOverlayScheme(IsBalanced(scheme) ? Balanced : scheme);

    public static string PowerName(Guid id) => id == PowerEfficiency ? "Power efficiency" : id == Performance ? "Best performance" : IsBalanced(id) ? "Balanced" : id.ToString();
}

// --- the shell's own notification state (shellapi's SHQueryUserNotificationState).
static class Shell
{
    [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
    public static int QueryUserNotificationState()
    {
        int result = SHQueryUserNotificationState(out int state);
        return result == 0 ? state : -1;
    }
}
