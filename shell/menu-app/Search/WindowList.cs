using System.Text.Json;

namespace SystemMenu.Search;

// One Search's Windows: what GlazeWM manages, from its answer to `query windows` (Bar/GlazeWm.cs
// asks on the same connection the bar uses). Pure: parsed from the answer's JSON, tested in
// menu-app.Tests.

// Id: GlazeWM's container id, which `focus --container-id` and `command --id <id> close` take.
// Handle: the window's own (HWND), for its live picture in the preview (DWM's thumbnail); 0 when
// GlazeWM didn't say.
public sealed record OpenWindow(string Id, string Title, string Process, bool Focused, long Handle = 0)
{
    // The row's name: the title, or the program when the window has none.
    public string Name => Title.Trim().Length > 0 ? Title.Trim() : Process;
}

public static class WindowList
{
    // `data` of the answer: {"windows":[{"type":"window","id":"…","title":"…","processName":"…",
    // "hasFocus":false,"handle":1234,…}]}. Anything not shaped like a window is skipped; a missing list is none.
    public static List<OpenWindow> Read(JsonElement data)
    {
        var found = new List<OpenWindow>();
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("windows", out var list) || list.ValueKind != JsonValueKind.Array)
            return found;
        foreach (var w in list.EnumerateArray())
        {
            if (w.ValueKind != JsonValueKind.Object) continue;
            if (w.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() != "window") continue;
            var id = Text(w, "id");
            if (id.Length == 0) continue;
            bool focused = w.TryGetProperty("hasFocus", out var f) && f.ValueKind == JsonValueKind.True;
            long handle = w.TryGetProperty("handle", out var h) && h.ValueKind == JsonValueKind.Number && h.TryGetInt64(out var n) ? n : 0;
            found.Add(new OpenWindow(id, Text(w, "title"), Text(w, "processName"), focused, handle));
        }
        // The one you're in last: you're looking for another.
        return [.. found.OrderBy(w => w.Focused ? 1 : 0)];
    }

    static string Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
