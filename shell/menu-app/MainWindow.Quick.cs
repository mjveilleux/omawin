using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace SystemMenu;

// The Quick settings page (`omawin launcher quick`): the machine's switches across the top —
// Wi-Fi, Bluetooth, brightness, power mode, night light, do not disturb, battery — and the sound
// below them (MainWindow.Audio.cs): every speaker and headset with what's playing on it, then the
// microphones. Win+A (`launcher audio`) is the same page without the switches.
//
// Up / down move, left / right (or + / −) turn a speaker's volume or a monitor's brightness,
// Space works the selected tile (a switch on or off, a speaker muted), Enter opens what a tile
// holds (the networks, the paired devices, the power modes) or does it. Esc goes back a step:
// a list, then the launcher.
//
// Nothing here reads or writes on the UI thread: the WLAN, WMI and radio calls block, so state is
// read behind a Task and the page redraws when it lands (LoadQuick), and the switches are set the
// same way (DoQuick). Windows' own change events — a radio switched, the network up or down —
// redraw it too, so nothing polls (QuickSettings.Watch).
//
// Night light and do not disturb have no public API (see the report and QuickSettings.cs): their
// tiles read what can be read and open the Settings page that owns them.
public sealed partial class MainWindow
{
    // What a switch tile is, so Space, + / − and Enter know what to work on — one of these, an
    // AudioDevice or an AudioApp (MainWindow.Audio.cs).
    sealed record QsTile(string Kind, object? Data = null);

    bool quickSwitches;                // Quick settings (`launcher quick`); false: Audio, the sound alone (Win+A)
    QuickSettings.QuickState? quick;   // null until it's been read; drawn while a fresh read is on its way
    bool readingQuick;                 // a read is in flight
    QsPart quickParts;                 // sections asked for while it was (read when it lands)
    IDisposable? quickWatch;           // Windows' radio and network change events
    string? quickList;                 // the open list under a switch: wifi | bluetooth | power
    string? quickNote;                 // what the last write answered ("Wi-Fi: on", or an error)
    List<WifiNetwork>? wifiNetworks;   // the networks nearby; null until asked for
    bool wifiDenied, readingWifi;
    bool quickFirstSound;              // select the first sound tile once the page is drawn
    // A radio waiting for a second press to go off: "<where>:<radio>", where is "tiles" or the
    // open list. Turning Wi-Fi or Bluetooth off by accident can strand you (a Bluetooth
    // keyboard, a remote session), so off takes two presses; any other key forgets it.
    string? offArmed;

    // Brightness: + / - change the level here at once (per display tile), and one worker writes
    // it, latest level wins, so presses never race each other or overlap on DDC/CI.
    readonly Dictionary<string, int> brightTarget = [];
    readonly Dictionary<string, (Display Display, int Level)> brightQueue = [];
    readonly object brightLock = new();
    bool brightBusy;                   // the worker is running (under brightLock)
    DateTime brightDoneAt;             // when the worker last ran dry

    // --- reading and writing, off the UI thread -----------------------------------------------
    // Reads the sections asked for (the rest of the snapshot stays), off the UI thread; the
    // page keeps drawing the old snapshot until the new one lands.
    void LoadQuick(QsPart parts = QsPart.All)
    {
        quickParts |= parts;
        if (readingQuick || quickParts == QsPart.None) return; // the read in flight picks them up when it lands
        readingQuick = true;
        var want = quickParts;
        var basis = quick;
        quickParts = QsPart.None;
        var started = DateTime.UtcNow;
        Task.Run(() => QuickSettings.Read(basis, want)).ContinueWith(t => DispatcherQueue.TryEnqueue(() =>
        {
            readingQuick = false;
            quick = t.IsCompletedSuccessfully ? t.Result : (quick ?? QuickSettings.QuickState.Empty) with { Error = t.Exception?.GetBaseException().Message };
            // A display read that began after the last brightness write is the truth again.
            bool writing; lock (brightLock) writing = brightBusy;
            if (want.HasFlag(QsPart.Displays) && !writing && started >= brightDoneAt) brightTarget.Clear();
            if (launching && page == "audio") RenderKeepingTile();
            // Something changed while it was being read: read that too.
            if (quickParts != QsPart.None) LoadQuick(QsPart.None);
        }));
    }

    // Redrawn, still on the same tile: the number of tiles above it changes as state lands.
    void RenderKeepingTile()
    {
        if (tileMode && quickList == null && routing == null && selected < tiles.Count) selectKey ??= tiles[selected].Spec.Key;
        Render();
    }

    // A write: the answer is the note in the footer ("Wi-Fi: on", or what went wrong), and the
    // section is read again, so the tiles show what Windows says rather than what we hoped.
    void DoQuick(Func<string?> action, QsPart parts)
    {
        Task.Run(action).ContinueWith(t => DispatcherQueue.TryEnqueue(() =>
        {
            quickNote = t.IsCompletedSuccessfully ? t.Result : t.Exception?.GetBaseException().Message;
            LoadQuick(parts);
        }));
    }

    void QuickCommand(string section, params string[] args) => DoQuick(() => QuickSettings.Command(section, args), section switch
    {
        "wifi" => QsPart.Wifi,
        "bluetooth" => QsPart.Bluetooth,
        "power" => QsPart.Power,
        "brightness" => QsPart.Displays,
        _ => QsPart.All,
    });

    // Windows' radio and network change events (Radio.StateChanged, NetworkStatusChanged): the
    // page redraws from them, so nothing here polls. Stopped when the page is left.
    void WatchQuick()
    {
        if (quickWatch != null) return;
        // Subscribing asks the radios (WinRT), so it happens off the UI thread too.
        Task.Run(() => QuickSettings.Watch(QuickChanged)).ContinueWith(t => DispatcherQueue.TryEnqueue(() =>
        {
            if (!t.IsCompletedSuccessfully) return;
            if (launching && page == "audio") { quickWatch?.Dispose(); quickWatch = t.Result; }
            else t.Result.Dispose(); // the page was left while the subscription was being made
        }));
    }

    void StopWatchingQuick()
    {
        quickWatch?.Dispose();
        quickWatch = null;
    }

    void QuickChanged(QsPart part) => DispatcherQueue.TryEnqueue(() => { if (launching && page == "audio") LoadQuick(part); });

    // --- the page -----------------------------------------------------------------------------
    TileLayout? QuickPage(string query)
    {
        if (routing != null) { RouteList(query); return null; }
        if (quickList == "wifi") { WifiList(query); return null; }
        if (quickList == "bluetooth") { BtList(query); return null; }
        if (quickList == "power") { PowerList(query); return null; }
        return QuickTiles(query);
    }

    TileLayout QuickTiles(string query)
    {
        Placeholder.Text = quickSwitches ? "Search settings and sound…" : "Search sound…";
        var layout = new TileLayout();
        bool Match(string name) => query.Length == 0 || Fuzzy.Match(name, query) != Fuzzy.None;

        var switches = new List<TileSpec>();
        if (quickSwitches && quick is { } state)
        {
            if (Match("wifi")) switches.Add(WifiTile(state));
            if (Match("bluetooth")) switches.Add(BluetoothTile(state));
            foreach (var display in state.Displays.Where(d => Match("brightness") || Match(d.Name))) switches.Add(BrightnessTile(display));
            if (Match("power")) switches.Add(PowerTile(state));
            if (Match("night light")) switches.Add(NightLightTile(state));
            if (Match("do not disturb notifications quiet")) switches.Add(QuietTile(state));
            if (state.Battery is { } battery && Match("battery")) switches.Add(BatteryTile(battery));
        }
        else if (quickSwitches)
            switches.Add(new TileSpec { Key = "reading", Style = "audio", ColSpan = 2, Icon = Glyph(0xF0493), Label = "Reading the machine…", Sub = "Wi-Fi, Bluetooth, brightness, power", Hint = "", Color = Hex(theme.Comment) });
        if (switches.Count > 0) layout.Flow(switches, "Switches", AudioTileDip);

        var (outputs, inputs, mics) = AudioSection(layout, query);
        // The page opens on the first speaker, as it did before the switches were added above
        // the sound, so a habitual Space still mutes rather than turning Wi-Fi off.
        if (quickFirstSound && layout.Cells.FirstOrDefault(c => c.Item is AudioDevice or AudioApp) is { } firstSound)
        {
            selectKey = firstSound.Key;
            quickFirstSound = false;
        }
        audioTimer ??= MakeAudioTimer();
        audioTimer.Start();
        // An app that starts to play while the page is open shows at once (Audio.cs, 8.10).
        Audio.WatchSessions(() => DispatcherQueue.TryEnqueue(() => { if (launching && page == "audio" && routing == null && appWindow.IsVisible) Render(); }));
        QuickFooter($"{outputs} outputs · {inputs} inputs · {mics} microphones");
        // Short: the footer's left side carries the device counts, and longer hints ran into them.
        if (quickSwitches) SetKeys(("space", "toggle"), ("← →", "level"), ("↵", "open"), ("esc", "back"));
        else SetKeys(("← →", "volume"), ("space", "mute"), ("↵", "use"), ("esc", "back"));
        return layout;
    }

    // The last write's answer while there is one, else the usual summary.
    void QuickFooter(string summary) => FooterLeft.Text = quickNote ?? summary;

    // One switch tile: its icon, name, what it is, the state on the right, and a bar that is full
    // when it's on (or at the monitor's level). Same shape as a speaker's tile.
    TileSpec Switch(string key, int glyph, string label, string sub, string hint, int level, Color color, bool on, QsTile? item) => new()
    {
        // Two to a row: a switch's name, what it's doing and its value need the width a speaker's
        // tile has; four to a row cut every label to its first letter.
        Key = key, Style = "audio", ColSpan = 2, Icon = Glyph(glyph), Label = label, Sub = sub, Hint = hint,
        Color = color, Current = on, Volume = level, Muted = false, Item = item, Run = () => TileUse(),
    };

    static int WifiGlyph(WifiState wifi) => wifi.RadioOn && wifi.Present ? 0xF05A9 : 0xF05AA;

    TileSpec WifiTile(QuickSettings.QuickState state)
    {
        var wifi = state.Wifi;
        string sub = !wifi.Present ? "no adapter"
            : !wifi.RadioOn ? "off"
            : wifi.Connected ? $"{wifi.Ssid} · {wifi.Signal}%" : "not connected";
        return Switch("wifi", WifiGlyph(wifi), "Wi-Fi", sub, wifi.RadioOn && wifi.Present ? "on" : "off", wifi.RadioOn && wifi.Present ? 100 : 0,
            Hex(!wifi.Present || !wifi.RadioOn ? theme.Muted : wifi.Connected ? theme.Green : theme.Yellow), wifi.RadioOn && wifi.Present, new QsTile("wifi"));
    }

    TileSpec BluetoothTile(QuickSettings.QuickState state)
    {
        var bt = state.Bluetooth;
        bool on = bt.Present && bt.RadioOn;
        int connected = state.Devices.Count(d => d.Connected);
        string sub = !bt.Present ? "no adapter"
            : !bt.RadioOn ? "off"
            : state.Devices.Count == 0 ? "nothing paired"
            : connected > 0 ? $"{connected} of {state.Devices.Count} connected"
            : $"{state.Devices.Count} paired";
        return Switch("bluetooth", on ? 0xF00AF : 0xF00B2, "Bluetooth", sub, on ? "on" : "off", on ? 100 : 0, Hex(on ? theme.Accent : theme.Muted), on, new QsTile("bluetooth"));
    }

    static int BrightnessGlyph(int level) => level < 26 ? 0xF00DA : level < 51 ? 0xF00DC : level < 76 ? 0xF00DE : 0xF00E0;

    // A monitor: its own tile, its level on the bar, + / − to set it. Enter opens Windows' display
    // settings (a monitor has no other page of its own).
    static string BrightKey(Display display) => display.Internal ? "bright:panel" : $"bright:{display.Key}";

    // The level shown is where + / − are heading, while that differs from the last read.
    TileSpec BrightnessTile(Display display)
    {
        int level = brightTarget.TryGetValue(BrightKey(display), out var target) ? target : display.Current;
        return Switch(BrightKey(display), BrightnessGlyph(level), display.Name, display.Internal ? "built-in panel" : "brightness",
            $"{level}%", level, Hex(theme.Cyan), true, new QsTile("brightness", display));
    }

    static int PowerGlyph(Guid mode) =>
        mode == Brightness.PowerEfficiency ? 0xF032A : mode == Brightness.Performance ? 0xF04C5 : Brightness.IsBalanced(mode) ? 0xF0F86 : 0xF0425;

    TileSpec PowerTile(QuickSettings.QuickState state)
    {
        var mode = state.Power;
        string name = mode.Known ? Brightness.PowerName(mode.Current) : "unknown";
        return Switch("power", PowerGlyph(mode.Current), "Power mode", name, "", 0, Hex(mode.Known ? theme.Purple : theme.Muted), mode.Known, new QsTile("power"));
    }

    // Night light: Windows keeps it to itself (a CloudStore value, not an API), so the tile shows
    // what can be read and opens the Settings page that owns it.
    TileSpec NightLightTile(QuickSettings.QuickState state)
    {
        bool on = state.NightLight == true;
        string sub = state.NightLight == null ? "no API · opens Settings" : on ? "on · no API" : "off · no API";
        return Switch("nightlight", 0xF0594, "Night light", sub, state.NightLight == null ? "?" : on ? "on" : "off", on ? 100 : 0,
            Hex(on ? theme.Yellow : theme.Muted), on, new QsTile("nightlight"));
    }

    // Do not disturb: no API to read or set either. SHQueryUserNotificationState is the closest a
    // program may ask (it answers the shell's own quiet state, not Microsoft's Focus sessions).
    TileSpec QuietTile(QuickSettings.QuickState state)
    {
        bool quiet = state.Notification is "quiet time" or "presentation mode";
        return Switch("dnd", 0xF009B, "Do not disturb", "no API · opens Settings", quiet ? "on" : "off", quiet ? 100 : 0,
            Hex(quiet ? theme.Accent : theme.Muted), quiet, new QsTile("dnd"));
    }

    // Charging and a full battery have their own glyphs; otherwise the level, in tens, as the
    // battery icons are numbered.
    static int BatteryGlyph(Battery battery) =>
        battery.Charging ? 0xF0084 : battery.Percent >= 95 ? 0xF0079 : 0xF0079 + Math.Clamp(battery.Percent / 10, 1, 9);

    TileSpec BatteryTile(Battery battery)
    {
        string sub = battery.Status + (battery.EnergySaver ? " · energy saver" : "");
        Color color = Hex(battery.Charging ? theme.Cyan : battery.Percent <= 20 ? theme.Red : theme.Green);
        return Switch("battery", BatteryGlyph(battery), "Battery", sub, $"{battery.Percent}%", battery.Percent, color, battery.Charging, new QsTile("battery"));
    }

    // --- what the keys do on a switch ----------------------------------------------------------
    // Space: on or off, or — with no API behind it — the Settings page that owns it.
    void QuickAction(QsTile tile)
    {
        switch (tile.Kind)
        {
            case "wifi": RadioSwitch("wifi", "Wi-Fi", quick?.Wifi.RadioOn == true, "tiles"); return;
            case "bluetooth": RadioSwitch("bluetooth", "Bluetooth", quick?.Bluetooth.RadioOn == true, "tiles"); return;
            case "power": QuickCommand("power", "cycle"); return;
            case "nightlight": QuickSettings.Open("ms-settings:nightlight"); quickNote = "Night light: no API — opened Settings"; break;
            case "dnd": QuickSettings.Open("ms-settings:quiethours"); quickNote = "Do not disturb: no API — opened Settings"; break;
            case "battery": QuickSettings.Open("ms-settings:batterysaver"); quickNote = "Battery: opened Settings"; break;
            case "brightness": QuickSettings.Open("ms-settings:display"); quickNote = "Displays: opened Settings"; break;
            default: return;
        }
        Render();
    }

    // Enter: the list a switch holds, or its Settings page.
    void QuickOpen(QsTile tile)
    {
        switch (tile.Kind)
        {
            case "wifi": quickList = "wifi"; selected = 0; LoadWifiNetworks(); Render(); Slide(1); break;
            case "bluetooth": quickList = "bluetooth"; selected = 0; Render(); Slide(1); break;
            case "power": quickList = "power"; selected = 0; Render(); Slide(1); break;
            default: QuickAction(tile); break;
        }
    }

    // Wi-Fi or Bluetooth on or off. On is one press; off asks first ("Press again to turn Wi-Fi
    // off" in the footer) and happens on a second press of the same thing (any other key forgets
    // it: ForgetOff, from OnKey).
    void RadioSwitch(string section, string name, bool on, string where)
    {
        if (!on) { offArmed = null; QuickCommand(section, "on"); return; }
        string id = $"{where}:{section}";
        if (offArmed != id)
        {
            offArmed = id;
            quickNote = $"Press again to turn {name} off";
            FooterLeft.Text = quickNote;
            return;
        }
        offArmed = null;
        quickNote = null;
        QuickCommand(section, "off");
    }

    // A key other than the one that confirms: the pending "off" is dropped, and its note with it.
    void ForgetOff()
    {
        if (offArmed == null) return;
        offArmed = null;
        if (quickNote?.StartsWith("Press again", StringComparison.Ordinal) == true) quickNote = null;
        if (launching && page == "audio") RenderKeepingTile();
    }

    // + / −: 5% a press on a monitor (1% with Shift). The level moves here at once and the write
    // follows; + / − never go below 5% (`omawin brightness set 0` still can).
    void QuickAdjust(QsTile tile, int by)
    {
        if (tile is not { Kind: "brightness", Data: Display display }) return;
        string key = BrightKey(display);
        int from = brightTarget.TryGetValue(key, out var target) ? target : display.Current;
        int level = Math.Clamp(from + by, Math.Min(5, from), 100);
        if (level == from) return;
        brightTarget[key] = level;
        if (selected < tiles.Count && tiles[selected].Spec.Key == key)
        {
            var v = tiles[selected];
            v.Spec.Volume = level;
            v.Spec.Hint = $"{level}%";
            SetLevel(v);
            v.Glyph.Text = Glyph(BrightnessGlyph(level));
        }
        WriteBrightness(key, display, level);
    }

    // One worker at a time writes the levels asked for; a newer level for a display replaces one
    // not yet written, so only the latest is sent. When it runs dry the displays are read again.
    void WriteBrightness(string key, Display display, int level)
    {
        lock (brightLock)
        {
            brightQueue[key] = (display, level);
            if (brightBusy) return;
            brightBusy = true;
        }
        Task.Run(() =>
        {
            while (true)
            {
                (Display Display, int Level) job;
                lock (brightLock)
                {
                    if (brightQueue.Count == 0) { brightBusy = false; break; }
                    var next = brightQueue.First();
                    brightQueue.Remove(next.Key);
                    job = next.Value;
                }
                string? error;
                try { error = QuickSettings.SetBrightness(job.Display, job.Level); }
                catch (Exception ex) { App.Log(ex); error = ex.Message; }
                DispatcherQueue.TryEnqueue(() =>
                {
                    quickNote = error ?? $"{job.Display.Name}: {job.Level}%";
                    if (launching && page == "audio" && quickList == null) FooterLeft.Text = quickNote;
                });
            }
            DispatcherQueue.TryEnqueue(() =>
            {
                brightDoneAt = DateTime.UtcNow;
                if (launching && page == "audio") LoadQuick(QsPart.Displays);
            });
        });
    }

    // --- the lists under a switch (rows, like the rest of the launcher) ------------------------
    // Esc closes one of these first, then the page.
    bool LeaveQuickList()
    {
        if (quickList == null) return false;
        selectKey = quickList; // back on the tile the list came from (its key is the list's name)
        quickList = null;
        Render();
        Slide(-1);
        return true;
    }

    // The networks nearby need precise-location consent on Windows 11 22H2 and later; without it
    // Windows refuses the list (and asks its own one-time question the first time). Denied then
    // means the Location settings page, said plainly.
    void LoadWifiNetworks()
    {
        if (readingWifi) return;
        readingWifi = true;
        wifiNetworks = null;
        Task.Run(() => QuickSettings.NearbyNetworks()).ContinueWith(t => DispatcherQueue.TryEnqueue(() =>
        {
            readingWifi = false;
            if (t.IsCompletedSuccessfully)
            {
                wifiNetworks = t.Result.Networks;
                wifiDenied = t.Result.Denied;
                quickNote = t.Result.Error;
            }
            if (launching && page == "audio" && quickList == "wifi") Render();
        }));
    }

    void WifiList(string query)
    {
        SetCrumbs("", "Wi-Fi");
        Placeholder.Text = "Search networks…";
        var wifi = quick?.Wifi;
        bool on = wifi is { Present: true, RadioOn: true };
        // The networks come first (the list opens on the first one) and "Turn Wi-Fi off" last, so
        // Enter, Enter connects rather than switching Wi-Fi off; when it's off, "on" leads.
        void Toggle() => AddRow(Glyph(on ? 0xF05AA : 0xF05A9), on ? "Turn Wi-Fi off" : "Turn Wi-Fi on", Hex(on ? theme.Muted : theme.Green),
            () => RadioSwitch("wifi", "Wi-Fi", on, "wifi"), Tag.Text, on ? "on" : "off");
        if (wifi is not { Present: false } && !on) Toggle();

        if (wifi is { Present: false })
            AddRow(Glyph(0xF05AA), "No Wi-Fi adapter", Hex(theme.Comment), () => { }, dim: true);
        else if (!on)
            AddRow(Glyph(0xF05AA), "Wi-Fi is off", Hex(theme.Comment), () => { }, dim: true);
        else if (wifiNetworks == null)
            AddRow(Glyph(0xF02FD), "Looking for networks…", Hex(theme.Comment), () => { }, dim: true);
        else if (wifiDenied)
            AddRow(Glyph(0xF0351), "Location is off, so Windows won't list networks", Hex(theme.Yellow),
                () => QuickSettings.Open("ms-settings:privacy-location"), sub: "open Settings › Privacy › Location");
        else
        {
            if (wifiNetworks.Count == 0)
                AddRow(Glyph(0xF02FD), "No networks in range", Hex(theme.Comment), () => { }, dim: true);
            foreach (var (network, hits) in Filter(wifiNetworks, query, n => n.Ssid))
            {
                var target = network;
                string state = target.Connected ? "connected" : target.HasProfile ? "saved" : target.Secure ? "needs its password" : "open";
                AddRow(Glyph(SignalGlyph(target.Signal)), target.Ssid, Hex(target.Connected ? theme.Green : theme.Accent),
                    () => Join(target), target.Connected ? Tag.Current : Tag.Text, target.Connected ? "" : $"{target.Signal}%", sub: state, highlight: hits);
            }
        }
        AddRow(Glyph(0xF0493), "Wi-Fi settings", Hex(theme.Comment), () => QuickSettings.Open("ms-settings:network-wifi"), sub: "networks Windows saved, VPN, proxies");
        if (on) Toggle();
        QuickFooter(wifiNetworks == null ? "looking…" : $"{wifiNetworks.Count} networks");
        SetKeys(("↑↓", "move"), ("↵", "connect · disconnect"), ("esc", "back"));
    }

    static int SignalGlyph(int signal) => signal < 25 ? 0xF091F : signal < 50 ? 0xF0922 : signal < 75 ? 0xF0925 : 0xF0928;

    // Join: a network Windows already has connects straight away. One it hasn't saved can't: the
    // profile needs the password, and typing it on screen would show it (the launcher's search box
    // is not a password field), so that row opens Windows' own Wi-Fi page, which asks for it.
    void Join(WifiNetwork network)
    {
        if (network.Connected) { DoQuick(() => "Wi-Fi: " + (QuickSettings.WifiDisconnect() ?? "disconnected"), QsPart.Wifi); return; }
        if (!network.HasProfile)
        {
            QuickSettings.Open("ms-settings:network-wifi");
            quickNote = network.Secure ? $"{network.Ssid} needs its password — opened Settings" : $"{network.Ssid} isn't saved — opened Settings";
            Render();
            return;
        }
        DoQuick(() => QuickSettings.WifiConnect(network.Ssid, false, null, network.Profile) ?? $"Wi-Fi: connecting to {network.Ssid}", QsPart.Wifi);
    }

    void BtList(string query)
    {
        SetCrumbs("", "Bluetooth");
        Placeholder.Text = "Search devices…";
        var bt = quick?.Bluetooth;
        bool on = bt is { Present: true, RadioOn: true };
        // Devices first, "Turn Bluetooth off" last (see WifiList).
        void Toggle() => AddRow(Glyph(on ? 0xF00B2 : 0xF00AF), on ? "Turn Bluetooth off" : "Turn Bluetooth on", Hex(on ? theme.Muted : theme.Green),
            () => RadioSwitch("bluetooth", "Bluetooth", on, "bluetooth"), Tag.Text, on ? "on" : "off");
        if (bt is not { Present: false } && !on) Toggle();

        var devices = quick?.Devices ?? [];
        if (bt is { Present: false })
            AddRow(Glyph(0xF00B2), "No Bluetooth adapter", Hex(theme.Comment), () => { }, dim: true);
        else if (!on)
            AddRow(Glyph(0xF00B2), "Bluetooth is off", Hex(theme.Comment), () => { }, dim: true);
        else if (devices.Count == 0)
            AddRow(Glyph(0xF02FD), "Nothing paired yet", Hex(theme.Comment), () => { }, dim: true);
        foreach (var (device, hits) in Filter(devices, query, d => d.Name))
        {
            var target = device;
            // Bluetooth LE devices (a mouse, a keyboard) have no connect of their own: Windows
            // keeps HID devices up by itself, and there is no API to ask. Classic audio devices
            // (a headset, a speaker) are brought up by their audio services.
            string state = target.Connected ? "connected" : target.Le ? "Bluetooth LE · Windows connects these itself" : "disconnected";
            AddRow(Glyph(target.Connected ? 0xF00B1 : 0xF00AF), target.Name, Hex(target.Connected ? theme.Green : theme.Accent),
                () => ToggleDevice(target), target.Connected ? Tag.Current : Tag.Text, target.Le ? "LE" : "", sub: state, highlight: hits);
            // A headset's hands-free (8.49): on, any app opening its microphone drops it to call
            // quality, mono, for everything; off, it stays in stereo and another microphone is used.
            if (target.Handsfree is { } hf)
                AddRow(Glyph(0xF02CB), hf ? $"{target.Name}: hands-free (call quality) on" : $"{target.Name}: stays in stereo (hands-free off)", Hex(theme.Muted),
                    () => DoQuick(() => QuickSettings.BluetoothHandsfree(target, !hf)
                        ?? $"{target.Name}: {(hf ? "stays in stereo; calls use another microphone" : "hands-free on again, for calls through it")}", QsPart.Bluetooth),
                    Tag.Text, hf ? "on" : "off",
                    sub: hf ? "Enter: stay in stereo when an app opens a microphone" : "Enter: hands-free back on, for calls through this headset");
        }
        AddRow(Glyph(0xF0493), "Bluetooth settings", Hex(theme.Comment), () => QuickSettings.Open("ms-settings:bluetooth"), sub: "pair a new device");
        if (on) Toggle();
        QuickFooter(devices.Count == 0 ? "no devices" : $"{devices.Count(d => d.Connected)} of {devices.Count} connected");
        SetKeys(("↑↓", "move"), ("↵", "connect · disconnect"), ("esc", "back"));
    }

    void ToggleDevice(BtDevice device)
    {
        if (device.Le)
        {
            // No public connect for a Bluetooth LE device: say so rather than fail silently.
            QuickSettings.Open("ms-settings:bluetooth");
            quickNote = $"{device.Name}: Windows connects LE devices itself — opened Settings";
            Render();
            return;
        }
        bool connect = !device.Connected;
        DoQuick(() => QuickSettings.BluetoothConnect(device, connect) ?? $"Bluetooth: {device.Name} {(connect ? "connected" : "disconnected")}", QsPart.Bluetooth);
    }

    void PowerList(string query)
    {
        SetCrumbs("", "Power mode");
        Placeholder.Text = "Search power modes…";
        var mode = quick?.Power;
        (Guid Id, string Name, int Glyph, string Sub)[] modes =
        [
            (Brightness.PowerEfficiency, "Power efficiency", 0xF032A, "least power, quietest"),
            (Brightness.Balanced, "Balanced", 0xF0F86, "performance and power in step"),
            (Brightness.Performance, "Best performance", 0xF04C5, "most power, fastest"),
        ];
        foreach (var (m, hits) in Filter(modes, query, m => m.Name))
        {
            var picked = m;
            AddRow(Glyph(picked.Glyph), picked.Name, Hex(mode?.Current == picked.Id ? theme.Green : theme.Purple),
                () => QuickCommand("power", picked.Name switch { "Power efficiency" => "efficiency", "Balanced" => "balanced", _ => "performance" }),
                mode?.Current == picked.Id ? Tag.Current : Tag.None, sub: picked.Sub, highlight: hits);
        }
        QuickFooter(mode?.Known == true ? $"now {Brightness.PowerName(mode.Current)}" : "unknown");
        SetKeys(("↑↓", "move"), ("↵", "set"), ("esc", "back"));
    }
}
