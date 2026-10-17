using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace SystemMenu;

// Omarchy's window placement (Hyprland's dwindle layout, as a spiral): each new window takes half
// of the focused one, and each split turns 90 degrees from the last, so opening windows one after
// another goes A | B, then B over C, then C | D, ... GlazeWM puts a new window next to the
// focused one in the direction of the split it's in, so whenever a tiled window gets focus it's
// told the direction its next split takes: across the split it sits in (side by side for the
// first window on a workspace). Over GlazeWM's IPC socket (the same messages as `glazewm ...`);
// a GlazeWM that isn't up yet, or restarts, is connected to again.
public static class Tiling
{
    public static void Start() => new Thread(Run) { IsBackground = true, Name = "tiling" }.Start();

    static void Run()
    {
        while (true)
        {
            try { Watch().GetAwaiter().GetResult(); }
            catch (Exception) { } // no GlazeWM yet, or it went away
            Thread.Sleep(3000);
        }
    }

    static async Task Watch()
    {
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri("ws://localhost:6123"), CancellationToken.None);
        await Send(socket, "sub -e focus_changed");
        var buffer = new byte[64 * 1024];
        while (socket.State == WebSocketState.Open)
        {
            var message = await Receive(socket, buffer);
            if (message == null) return;
            // Answered once GlazeWM has laid the change out (the event itself can come before).
            if (IsFocusEvent(message)) { await Send(socket, "query workspaces"); continue; }
            var (id, direction) = NextSplit(message);
            if (id != null) await Send(socket, $"command --id {id} set-tiling-direction {direction}");
        }
    }

    static bool IsFocusEvent(string message)
    {
        using var json = JsonDocument.Parse(message);
        return json.RootElement.TryGetProperty("messageType", out var kind) && kind.GetString() == "event_subscription";
    }

    // From the reply to "query workspaces": the focused tiled window and the direction its next
    // split takes, across the nearest split around it that holds more than it (GlazeWM wraps a
    // window in a split of its own when its direction changes, so a one-child split doesn't
    // count). Nothing when a workspace or a floating window has focus.
    static (string? Id, string Direction) NextSplit(string message)
    {
        using var json = JsonDocument.Parse(message);
        var root = json.RootElement;
        if (!root.TryGetProperty("clientMessage", out var asked) || asked.GetString() != "query workspaces") return (null, "");
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("workspaces", out var workspaces)) return (null, "");
        foreach (var workspace in workspaces.EnumerateArray())
        {
            var path = new List<JsonElement>();
            if (FindFocused(workspace, path) is not { } window) continue;
            if (!window.TryGetProperty("state", out var state) || state.GetProperty("type").GetString() != "tiling") return (null, "");
            var split = path.LastOrDefault(c => c.GetProperty("children").GetArrayLength() > 1);
            var direction = split.ValueKind == JsonValueKind.Undefined
                ? "horizontal" // alone on the workspace: the first split is side by side
                : split.GetProperty("tilingDirection").GetString() == "horizontal" ? "vertical" : "horizontal";
            return (window.GetProperty("id").GetString(), direction);
        }
        return (null, "");
    }

    // The focused window under `container`, with the containers on the way down in `path`.
    static JsonElement? FindFocused(JsonElement container, List<JsonElement> path)
    {
        if (!container.TryGetProperty("children", out var children)) return null;
        path.Add(container);
        foreach (var child in children.EnumerateArray())
        {
            if (child.GetProperty("type").GetString() == "window")
            {
                if (child.TryGetProperty("hasFocus", out var focus) && focus.GetBoolean()) return child;
            }
            else if (FindFocused(child, path) is { } found) return found;
        }
        path.RemoveAt(path.Count - 1);
        return null;
    }

    static Task Send(ClientWebSocket socket, string text) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, CancellationToken.None);

    static async Task<string?> Receive(ClientWebSocket socket, byte[] buffer)
    {
        using var message = new MemoryStream();
        while (true)
        {
            var part = await socket.ReceiveAsync(buffer, CancellationToken.None);
            if (part.MessageType == WebSocketMessageType.Close) return null;
            message.Write(buffer, 0, part.Count);
            if (part.EndOfMessage) return Encoding.UTF8.GetString(message.ToArray());
        }
    }
}
