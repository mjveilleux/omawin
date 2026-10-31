using System.Drawing;
using System.IO.Pipes;
using System.Reflection;
using System.Windows.Forms;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace OmawinMusic;

// omawin-music --suno: Suno's own site in a WebView2 window, the engine for Suno playlists. Suno's
// audio is encrypted and only its own page plays it, so it plays here, in Suno's player, and the
// player (SunoEngine.cs) drives it over \\.\pipe\omawin-suno like it drives mpv: one JSON object a
// line each way. suno-bridge.js, injected into the page, reports what's playing and works it.
//
//   in:  {"cmd":"open","url":...}  a playlist or song page, then its own Play button
//        {"cmd":"play"|"pause"|"toggle"|"next"|"prev"}  {"cmd":"seek","to":s}  {"cmd":"gain","to":0..1}
//        {"cmd":"go","url":...}  a page to use, not play (the create page)
//        {"cmd":"show","x":..,"y":..,"w":..,"h":..} (screen pixels)  {"cmd":"hide"}  {"cmd":"quit"}
//   out: the bridge's {"ev":"state",...}, and {"ev":"opened"|"noplay","url":...}
//
// It plays out of sight (parked off screen: a page never shown doesn't start playing): the bar
// shows it beside the music panel while the panel's Suno tab is open (GlazeWM ignores
// omawin-music's windows, config/glazewm/config.yaml).
// Signing in (for private playlists) happens in the window; the profile is kept in
// %LOCALAPPDATA%\launcher\suno.
static class SunoHost
{
    public static readonly string PipeName = Account.Pipe("omawin-suno");

    public static void Run()
    {
        using var mutex = new Mutex(true, @"Local\omawin-suno", out bool first);
        if (!first) return;
        // WebView2 wants a single-threaded (STA) thread; Main isn't one (the other roles don't
        // need it), so the window gets its own.
        var ui = new Thread(() =>
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); // show's pixels are the screen's own
            Application.EnableVisualStyles();
            Application.Run(new SunoForm());
        });
        ui.SetApartmentState(ApartmentState.STA);
        ui.Start();
        ui.Join();
    }
}

sealed class SunoForm : Form
{
    readonly WebView2 web = new() { Dock = DockStyle.Fill };
    StreamWriter? client;
    readonly object clientGate = new();
    string? pendingPlay; // a page opened to play: its Play button is pressed once it's there

    public SunoForm()
    {
        Text = "omawin Suno";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Bounds = OffScreen; // until the bar places it
        BackColor = Color.FromArgb(16, 16, 16);
        Controls.Add(web);
        _ = Handle; // hidden windows aren't created on their own: this starts WebView2 (OnHandleCreated)
    }

    string? openWhenReady; // asked for before WebView2 was up
    string? goWhenReady;

    // Never really hidden: a page that has never been visible doesn't start playing (Chromium
    // defers it, like a background tab's), so "hidden" is parked off screen (Park).
    static readonly Rectangle OffScreen = new(-32000, -32000, 460, 640);
    void Park()
    {
        TopMost = false;
        Bounds = OffScreen;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int w, int h, uint flags);

    // Shown beside the panel without taking the keyboard from it.
    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var p = base.CreateParams;
            p.ExStyle |= 0x80; // WS_EX_TOOLWINDOW: not in Alt+Tab
            return p;
        }
    }

    protected override async void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            var profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "launcher", "suno");
            // Plays when it's told to, without a click in the page first, and isn't slowed down
            // for being off screen or covered (it's a player, not a background tab).
            var env = await CoreWebView2Environment.CreateAsync(null, profile, new CoreWebView2EnvironmentOptions(
                "--autoplay-policy=no-user-gesture-required --disable-backgrounding-occluded-windows " +
                "--disable-renderer-backgrounding --disable-background-timer-throttling --disable-features=CalculateNativeWinOcclusion"));
            await web.EnsureCoreWebView2Async(env);
            var core = web.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.IsStatusBarEnabled = false;
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("suno-bridge.js")!)
            using (var r = new StreamReader(s))
                await core.AddScriptToExecuteOnDocumentCreatedAsync(r.ReadToEnd());
            core.WebMessageReceived += (_, m) => Tell(m.WebMessageAsJson);
            core.NavigationCompleted += (_, _) => { if (pendingPlay != null) _ = PressPlay(pendingPlay); };
            // Links Suno opens in a new window stay in this one.
            core.NewWindowRequested += (_, n) => { n.Handled = true; core.Navigate(n.Uri); };
            if (openWhenReady != null) Open(openWhenReady);
            else core.Navigate(goWhenReady ?? "https://suno.com/");
        }
        catch (Exception ex) { Program.Log(new Exception("suno: WebView2 didn't start: " + ex.Message)); }
        new Thread(Listen) { IsBackground = true, Name = "suno pipe" }.Start();
    }

    void Open(string url)
    {
        openWhenReady = null;
        pendingPlay = url;
        if (web.CoreWebView2.Source == url) _ = PressPlay(url); // already there: just play it
        else web.CoreWebView2.Navigate(url);
    }

    // The page's Play button appears a moment after the page does: looked for for a while.
    async Task PressPlay(string url)
    {
        for (int i = 0; i < 40 && pendingPlay == url; i++)
        {
            await Task.Delay(500);
            if (web.CoreWebView2 == null) return;
            var pressed = await web.CoreWebView2.ExecuteScriptAsync("window.__omawin ? window.__omawin.playPage() : false");
            if (pressed == "true")
            {
                pendingPlay = null;
                Tell(JsonSerializer.Serialize(new { ev = "opened", url }));
                return;
            }
        }
        if (pendingPlay == url)
        {
            pendingPlay = null;
            Tell(JsonSerializer.Serialize(new { ev = "noplay", url }));
        }
    }

    async Task Go(CoreWebView2 core, string url, string pathJson)
    {
        if (core.Source.StartsWith(url)) return;
        var clicked = await core.ExecuteScriptAsync(
            $"(() => {{ const a = document.querySelector('a[href=' + JSON.stringify({pathJson}) + ']'); if (a) a.click(); return !!a; }})()");
        if (clicked != "true") core.Navigate(url);
    }

    void Tell(string json)
    {
        lock (clientGate)
        {
            try { client?.WriteLine(json); } catch { client = null; }
        }
    }

    // One client at a time (the player); a new one replaces the old.
    void Listen()
    {
        while (true)
        {
            var pipe = new NamedPipeServerStream(SunoHost.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            try { pipe.WaitForConnection(); }
            catch { pipe.Dispose(); Thread.Sleep(200); continue; }
            lock (clientGate) client = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
            try
            {
                var reader = new StreamReader(pipe, Encoding.UTF8);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (JsonNode.Parse(line) is not JsonObject msg) continue;
                    BeginInvoke(() => Do(msg));
                }
            }
            catch { }
            lock (clientGate) client = null;
            pipe.Dispose();
        }
    }

    void Do(JsonObject msg)
    {
        var core = web.CoreWebView2;
        string cmd = (string?)msg["cmd"] ?? "";
        double Num(string name) => msg[name]?.GetValue<double>() ?? 0;
        switch (cmd)
        {
            case "open":
                var url = (string?)msg["url"] ?? "";
                if (!url.StartsWith("https://suno.com/")) return;
                if (core == null) { goWhenReady = null; openWhenReady = url; }
                else Open(url);
                break;
            case "play" or "pause" or "toggle" or "next" or "prev":
                _ = core?.ExecuteScriptAsync($"window.__omawin && window.__omawin.cmd('{cmd}')");
                break;
            case "go":
                // A page of Suno's to use, not to play (its create page). Through the page's own
                // link when it has one, so what's playing plays on; else loaded.
                var page = (string?)msg["url"] ?? "";
                if (!page.StartsWith("https://suno.com/")) return;
                if (core == null) { openWhenReady = null; goWhenReady = page; return; }
                pendingPlay = null;
                var path = JsonSerializer.Serialize(new Uri(page).AbsolutePath);
                _ = Go(core, page, path);
                break;
            case "seek" or "gain":
                var to = Num("to").ToString(System.Globalization.CultureInfo.InvariantCulture);
                _ = core?.ExecuteScriptAsync($"window.__omawin && window.__omawin.cmd('{cmd}', {to})");
                break;
            case "show":
                // Placed and raised above every window, with the bar's panel (which is on top while
                // it's open), in one call each time: WinForms' TopMost alone left it under windows
                // that came forward after it.
                TopMost = true;
                SetWindowPos(Handle, new IntPtr(-1), (int)Num("x"), (int)Num("y"), Math.Max(320, (int)Num("w")), Math.Max(320, (int)Num("h")),
                    0x0010 | 0x0040); // HWND_TOPMOST; SWP_NOACTIVATE | SWP_SHOWWINDOW
                break;
            case "hide":
                Park(); // it plays on
                break;
            case "quit":
                Application.Exit();
                break;
        }
    }
}
