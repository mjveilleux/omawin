using System.Runtime.InteropServices;
using System.Text;

namespace SystemMenu;

// `SystemMenu.exe --qs-selftest`: what each Quick settings section can see, printed. Read only,
// and safe to run while the menu app is up (Program.Main handles it before the single-copy
// mutex, so it never forwards to the running copy). Its point is to be compared, line by line,
// with Windows' own tools, so the wiring is proven without changing a single setting:
//
//   netsh wlan show interfaces            Windows' view of the adapters and the current network
//   netsh wlan show networks              the networks nearby
//   Get-PnpDevice -Class Bluetooth        paired Bluetooth devices
//   Get-CimInstance -Namespace root/wmi WmiMonitorBrightness   the built-in panel's brightness
//   Get-CimInstance -Namespace root/wmi WmiMonitorBrightnessMethods  what can set it
//   powercfg /getactivescheme             the power plan (the Power mode is the overlay on top)
//
// The network list is skipped on purpose: reading it needs precise-location consent and would
// raise Windows' own prompt on a PC that hasn't answered it, which a self test must not do.
// `omawin launcher quick` (or the page itself) reads it, and handles the refusal.
public static class QuickSettingsSelfTest
{
    [DllImport("kernel32.dll")] static extern bool AttachConsole(int processId);
    [DllImport("kernel32.dll")] static extern bool AllocConsole();

    public static int Run()
    {
        // A WinExe has no console of its own: borrow the one it was started from, or make one
        // when it was double-clicked, so the output can be read either way.
        if (!AttachConsole(-1)) AllocConsole();
        var output = new StreamWriter(Console.OpenStandardOutput(), Encoding.UTF8) { AutoFlush = true };
        Console.SetOut(output);
        try { Report(output); return 0; }
        catch (Exception ex) { output.WriteLine($"self test failed: {ex}"); return 1; }
    }

    // `SystemMenu.exe --snip-selftest`: the screenshot tool's own (Snip.SelfTest), printed the same way.
    public static int RunSnip()
    {
        if (!AttachConsole(-1)) AllocConsole();
        var output = new StreamWriter(Console.OpenStandardOutput(), Encoding.UTF8) { AutoFlush = true };
        try { output.Write(Snip.SelfTest().GetAwaiter().GetResult()); return 0; }
        catch (Exception ex) { output.WriteLine($"self test failed: {ex}"); return 1; }
    }

    static void Report(TextWriter w)
    {
        w.WriteLine("omawin quick settings — self test (read only)");
        w.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        w.WriteLine();

        Wifi(w);
        Bluetooth(w);
        Displays(w);
        Power(w);
        Notifications(w);
        AudioDevices(w);
        MenuFiles(w);
        w.Flush();
    }

    // The menu app's own files, read as it reads them (its JSON, the Apps folder): to compare the
    // normal build with the native one (build.ps1 -Aot).
    static void MenuFiles(TextWriter w)
    {
        var theme = MenuModel.LoadTheme(MenuModel.CurrentThemeName());
        w.WriteLine("Menu app");
        w.WriteLine($"  theme: {MenuModel.CurrentThemeName()} = {theme.Name}, {theme.Mode}, background {theme.Background}, accent {theme.Accent}");
        w.WriteLine($"  launch history: {History.Load().Items.Count} entries");
        w.WriteLine($"  Apps folder: {AppsFolder.Items().Count} items");
        w.WriteLine();
    }

    // Compare with Sound settings and its volume mixer (Get-PnpDevice -Class AudioEndpoint).
    static void AudioDevices(TextWriter w)
    {
        w.WriteLine("Audio  (Core Audio)");
        foreach (var d in Audio.List())
            w.WriteLine($"  {(d.Input ? "mic    " : "speaker")} {d.Name}{(d.Default ? "  [default]" : "")}  {d.Volume}%{(d.Muted ? " muted" : "")}");
        foreach (var a in Audio.Apps())
            w.WriteLine($"  app     {a.Name}  {a.Volume}%{(a.Muted ? " muted" : "")}{(a.Playing ? " playing" : "")}{(a.Output != null ? "  -> " + a.Output : "")}");
        w.WriteLine();
    }

    static void Wifi(TextWriter w)
    {
        w.WriteLine("Wi-Fi");
        var state = QuickSettings.Wifi();
        var adapters = QuickSettings.WifiAdapters();
        w.WriteLine($"  adapters: {adapters.Count}");
        foreach (var (description, ifaceState) in adapters) w.WriteLine($"    {description}  [{ifaceState}]");
        w.WriteLine($"  radio: {(state.Present ? state.RadioOn ? "on" : "off" : "no adapter")}  (Windows.Devices.Radios)");
        w.WriteLine(state.Connected
            ? $"  connected: {state.Ssid}  (signal {state.Signal}%, NetworkInformation)"
            : "  connected: nothing  (NetworkInformation)");
        w.WriteLine("  network list: skipped in selftest (needs location consent)");
        if (state.Error != null) w.WriteLine($"  error: {state.Error}");
        w.WriteLine();
    }

    static void Bluetooth(TextWriter w)
    {
        w.WriteLine("Bluetooth");
        var state = QuickSettings.Bluetooth();
        w.WriteLine($"  radio: {(state.Error != null ? "error" : state.Present ? state.RadioOn ? "on" : "off" : "no adapter")}  (Windows.Devices.Radios)");
        if (state.Error != null) w.WriteLine($"  error: {state.Error}");
        var devices = QuickSettings.BluetoothDevices();
        w.WriteLine($"  paired devices: {devices.Count}");
        foreach (var device in devices) w.WriteLine($"    {device.Name}  [{(device.Connected ? "connected" : "disconnected")}]  {device.Address:X12}");
        w.WriteLine();
    }

    static void Displays(TextWriter w)
    {
        w.WriteLine("Brightness");
        var displays = QuickSettings.Displays();
        if (displays.Count == 0) w.WriteLine("  no monitor reports brightness");
        foreach (var display in displays)
            w.WriteLine($"  {(display.Internal ? "built-in" : "external")}: {display.Name}  {display.Current}%  {(display.Writable ? "writable" : "read only")}");
        // The monitors dxva2 answered for, including the ones the page leaves out.
        foreach (var (name, min, current, max, capabilities) in Brightness.Raw())
            w.WriteLine($"  dxva2: {name}  {current} of {min}..{max}  caps {capabilities}");
        w.WriteLine();
    }

    static void Power(TextWriter w)
    {
        w.WriteLine("Power");
        var mode = QuickSettings.PowerMode();
        w.WriteLine($"  mode: {(mode.Known ? Brightness.PowerName(mode.Current) : "unknown")}  (effective overlay {mode.Raw})");
        var battery = QuickSettings.BatteryState();
        w.WriteLine(battery == null
            ? "  battery: none (a desktop)"
            : $"  battery: {battery.Percent}%  {battery.Status}  (energy saver {(battery.EnergySaver ? "on" : "off")})");
        w.WriteLine();
    }

    static void Notifications(TextWriter w)
    {
        w.WriteLine("Do not disturb / night light");
        w.WriteLine($"  shell notification state: {QuickSettings.NotificationState() ?? "unknown"}  (SHQueryUserNotificationState)");
        w.WriteLine("  do not disturb: no public API to read or set; opens Settings (ms-settings:quiethours)");
        w.WriteLine($"  night light: {QuickSettings.NightLight() switch { true => "on", false => "off", null => "unknown" }}  (HKCU CloudStore, read only; opens Settings)");
        w.WriteLine();
    }
}
