using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace SystemMenu;

// GlazeWM's own connection (its IPC, a WebSocket on GLAZEWM_IPC_PORT; 6123 by default), as
// Zebar's glazewm provider and the glazewm CLI use it: subscribed to what changes the bar, the
// monitors and their workspaces asked for again after each change, and commands sent on it.
// Reconnects by itself (GlazeWM restarting, or not up yet at sign-in).
public sealed class GlazeWm
{
    public sealed record Workspace(string Name, bool Displayed, bool Focused, bool Occupied);
    public sealed record Monitor(int X, int Y, int Width, int Height, double Scale, List<Workspace> Workspaces);
    public sealed record State(List<Monitor> Monitors, bool FocusedFloats);

    public event Action<State>? Changed;
    public State? Last { get; private set; }

    ClientWebSocket? socket;
    readonly SemaphoreSlim sending = new(1, 1);

    static int Port => int.TryParse(Environment.GetEnvironmentVariable("GLAZEWM_IPC_PORT"), out var p) ? p : 6123;

    public void Start() => new Thread(Run) { IsBackground = true, Name = "glazewm" }.Start();

    // A command, as `glazewm command ...` takes it ("focus --workspace 2").
    public void Command(string command) => _ = SendAsync("command " + command);

    async void Run()
    {
        while (true)
        {
            try
            {
                using var ws = new ClientWebSocket();
                await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{Port}"), CancellationToken.None);
                socket = ws;
                await SendAsync("sub --events workspace_activated workspace_deactivated workspace_updated focus_changed " +
                    "focused_container_moved monitor_added monitor_updated monitor_removed window_managed window_unmanaged");
                await RefreshAsync();
                var buffer = new byte[64 * 1024];
                var message = new MemoryStream();
                while (ws.State == WebSocketState.Open)
                {
                    var r = await ws.ReceiveAsync(buffer, CancellationToken.None);
                    if (r.MessageType == WebSocketMessageType.Close) break;
                    message.Write(buffer, 0, r.Count);
                    if (!r.EndOfMessage) continue;
                    Handle(message.ToArray());
                    message.SetLength(0);
                }
            }
            catch (Exception) { } // not up yet, or gone: try again
            socket = null;
            Thread.Sleep(1000);
        }
    }

    async Task SendAsync(string text)
    {
        var ws = socket;
        if (ws == null || ws.State != WebSocketState.Open) return;
        await sending.WaitAsync();
        try { await ws.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, CancellationToken.None); }
        catch (Exception) { }
        finally { sending.Release(); }
    }

    Task RefreshAsync() => Task.WhenAll(SendAsync("query monitors"), SendAsync("query focused"));

    // What came back: an event (ask again), or the answer to a query.
    List<Monitor>? monitors;
    bool floats;
    int refreshQueued;

    void Handle(byte[] json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var kind = root.GetProperty("messageType").GetString();
            if (kind == "event_subscription")
            {
                // A burst of events (a window moved: several at once) is asked about once.
                if (Interlocked.Exchange(ref refreshQueued, 1) == 0)
                    Task.Delay(15).ContinueWith(_ => { refreshQueued = 0; return RefreshAsync(); });
                return;
            }
            if (kind != "client_response" || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) return;
            var asked = root.GetProperty("clientMessage").GetString();
            if (asked == "query monitors") monitors = ReadMonitors(data.GetProperty("monitors"));
            else if (asked == "query focused")
            {
                var f = data.GetProperty("focused");
                floats = f.TryGetProperty("type", out var t) && t.GetString() == "window"
                    && f.TryGetProperty("state", out var s) && s.TryGetProperty("type", out var st) && st.GetString() == "floating";
            }
            else return;
            if (monitors == null) return;
            Last = new State(monitors, floats);
            Changed?.Invoke(Last);
        }
        catch (Exception ex) { App.Log(ex); }
    }

    static List<Monitor> ReadMonitors(JsonElement list)
    {
        var found = new List<Monitor>();
        foreach (var m in list.EnumerateArray())
        {
            var spaces = new List<Workspace>();
            foreach (var w in m.GetProperty("children").EnumerateArray())
            {
                if (w.GetProperty("type").GetString() != "workspace") continue;
                spaces.Add(new Workspace(w.GetProperty("name").GetString() ?? "", w.GetProperty("isDisplayed").GetBoolean(),
                    w.GetProperty("hasFocus").GetBoolean(), w.GetProperty("children").GetArrayLength() > 0));
            }
            found.Add(new Monitor(m.GetProperty("x").GetInt32(), m.GetProperty("y").GetInt32(), m.GetProperty("width").GetInt32(),
                m.GetProperty("height").GetInt32(), m.GetProperty("scaleFactor").GetDouble(), spaces));
        }
        return found;
    }
}
