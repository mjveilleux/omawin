using System.Text.Json;

namespace SystemMenu;

// Keys GlazeWM sends straight to the menu app (roadmap 1.2): our GlazeWM fork's `emit omawin
// <command words>` reaches every IPC subscriber as a `custom` event, and the words are omawin
// command words, resolved through bin/commands.txt as omawin.exe resolves them (bin/omawin.cs: the
// longest command the words spell, then the words after it). An `app` row becomes the request
// omawin.exe would have written to the request file, without a process or a file; any other row
// is handed to omawin.exe, so any command can be bound to `emit omawin ...`.
//
// This half is pure (no files, no WinUI) and tested in test/menu-app.Tests; Emit.Live.cs reads
// commands.txt and acts.
public static partial class Emit
{
    // What a custom event's words come to.
    public enum Kind { Request, Omawin, Unknown }
    public sealed record Resolution(Kind Kind, string[] Words);

    // A custom event from GlazeWM's subscription message (`data` of an `event_subscription`):
    // {"eventType":"custom","name":"omawin","args":["launcher","clipboard"]}. False for any other
    // event, or one not shaped like that.
    public static bool TryRead(JsonElement data, out string name, out string[] words)
    {
        name = "";
        words = [];
        if (data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("eventType", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != "custom"
            || !data.TryGetProperty("name", out var n) || n.ValueKind != JsonValueKind.String)
            return false;
        name = n.GetString() ?? "";
        if (data.TryGetProperty("args", out var a) && a.ValueKind == JsonValueKind.Array)
            words = [.. a.EnumerateArray().Where(w => w.ValueKind == JsonValueKind.String).Select(w => w.GetString() ?? "")];
        return true;
    }

    // commands.txt's rows: name -> action (`app --launcher`, `pwsh bin/x.ps1`, ...). Comments,
    // blank lines and rows without three parts are skipped, as omawin.exe skips them.
    public static Dictionary<string, string> Table(IEnumerable<string> lines)
    {
        var table = new Dictionary<string, string>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var parts = line.Split('|', 3);
            if (parts.Length == 3) table[parts[0].Trim()] = parts[2].Trim();
        }
        return table;
    }

    // The longest command (up to three words) the words spell. An `app` row's request plus the
    // words after the command, split on spaces as the signal loop splits the request file.
    public static Resolution Resolve(IReadOnlyDictionary<string, string> table, string[] words)
    {
        for (int n = Math.Min(words.Length, 3); n > 0; n--)
        {
            if (!table.TryGetValue(string.Join(' ', words[..n]), out var action)) continue;
            if (action != "app" && !action.StartsWith("app ")) return new(Kind.Omawin, words);
            var request = (action[3..].Trim() + " " + string.Join(' ', words[n..])).Trim();
            return new(Kind.Request, request.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
        return new(Kind.Unknown, words);
    }
}
