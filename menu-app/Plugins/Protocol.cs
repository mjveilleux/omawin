using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SystemMenu.Plugins;

// The plugin protocol (docs/plan/plugins.md, 05-plugins.md §3.1): one JSON object per line.
// A plugin's stdout lines become these messages; the shell's are written by Protocol's methods.
// Inbound lines are read with JsonNode and a switch on "type" (no reflection, no serializer:
// the menu app is Native AOT). Pure, so menu-app.Tests runs it anywhere.

public abstract record PluginMessage;
// Waybar's bar line (what Omarchy's command modules print): no "type".
public sealed record BarLine(string? Text, string? Alt, string? Tooltip, string[] Classes, double? Percentage) : PluginMessage;
public sealed record StateMessage(JsonNode Data, bool Merge) : PluginMessage;
public sealed record NotifyMessage(string Title, string? Body, string? Icon, string? Open, string Urgency) : PluginMessage;
public sealed record PanelRequest(bool Open, string? Name) : PluginMessage;
public sealed record RunMessage(string[]? Command, string? Open) : PluginMessage;
public sealed record LogMessage(string Level, string Message) : PluginMessage;
public sealed record SetMessage(string Key, JsonNode? Value) : PluginMessage;
public sealed record ResultsMessage(int Id, JsonArray Items, bool Done) : PluginMessage;
public sealed record FoundMessage(int Id, JsonArray Items, bool Done) : PluginMessage;
public sealed record CalendarMessage(string Type, JsonArray Items, string? From, string? To) : PluginMessage;
public sealed record ReplyMessage(int Req, bool Ok, string? Message, JsonNode? Data) : PluginMessage;
public sealed record AdminRequest(string Verb, string[] Args, string Why) : PluginMessage;
public sealed record SlotMessage(double Width) : PluginMessage;
// Anything else: logged; for an interval script, plain text is the bar's text.
public sealed record NotJson(string Line) : PluginMessage;

// What a plugin is told when it starts.
public sealed record InitInfo(
    string Id, string Instance, JsonObject Settings, string Theme, JsonNode? Palette,
    string OmawinVersion, string PluginDir, string DataDir, string Side);

public static class Protocol
{
    // One stdout line. Never throws: a bad line is reported as NotJson and the bar keeps its
    // last state.
    public static PluginMessage Parse(string line)
    {
        line = line.Trim();
        if (line.Length == 0 || line[0] != '{') return new NotJson(line);
        JsonNode? n;
        try { n = JsonNode.Parse(line); } catch (JsonException) { return new NotJson(line); }
        if (n is not JsonObject o) return new NotJson(line);

        string? S(string k) => o[k] switch
        {
            null => null,
            JsonValue v when v.TryGetValue<string>(out var s) => s,
            var other => other.ToJsonString(),
        };
        bool B(string k) => o[k] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
        int I(string k) => o[k] is JsonValue v && v.TryGetValue<int>(out var i) ? i : 0;
        double? D(string k) => o[k] is JsonValue v && v.TryGetValue<double>(out var d) ? d : null;
        string[] L(string k) => o[k] switch
        {
            JsonArray a => a.Select(x => x is JsonValue xv && xv.TryGetValue<string>(out var s) ? s : x?.ToJsonString() ?? "").ToArray(),
            JsonValue v when v.TryGetValue<string>(out var s) => s.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            _ => [],
        };
        JsonArray A(string k) => o[k] is JsonArray a ? (JsonArray)a.DeepClone() : [];

        switch (S("type"))
        {
            case null: return new BarLine(S("text"), S("alt"), S("tooltip"), L("class"), D("percentage"));
            case "state": return new StateMessage(o["data"]?.DeepClone() ?? new JsonObject(), B("merge"));
            case "notify": return new NotifyMessage(S("title") ?? "", S("body"), S("icon"), S("open"), S("urgency") ?? "normal");
            case "open-panel": return new PanelRequest(true, S("name"));
            case "close-panel": return new PanelRequest(false, S("name"));
            case "run": return new RunMessage(o["command"] is JsonArray ? L("command") : null, S("open"));
            case "log": return new LogMessage(S("level") ?? "info", S("message") ?? "");
            case "set": return new SetMessage(S("key") ?? "", o["value"]?.DeepClone());
            case "results": return new ResultsMessage(I("id"), A("items"), B("done"));
            case "found": return new FoundMessage(I("id"), A("items"), B("done"));
            case "calendars" or "events": return new CalendarMessage(S("type")!, A("items"), S("from"), S("to"));
            case "reply": return new ReplyMessage(I("req"), B("ok"), S("message"), o["data"]?.DeepClone());
            case "admin": return new AdminRequest(S("verb") ?? "", L("args"), S("why") ?? "");
            case "slot": return new SlotMessage(D("width") ?? 0);
            default: return new NotJson(line); // a newer plugin's message: logged, ignored
        }
    }

    // A run message may only start omawin's own commands: anything else would run outside the
    // plugin's Job Object, through the shell. "open" is checked separately (https or a file).
    public static bool RunAllowed(RunMessage run) =>
        run.Command is [var first, ..] && first is "omawin" or "omawin.exe";

    public static bool OpenAllowed(string target) =>
        target.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    // --- shell -> plugin ---------------------------------------------------------------------------

    static string Write(Action<Utf8JsonWriter> body)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            body(w);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    static void Node(Utf8JsonWriter w, string name, JsonNode? n)
    {
        w.WritePropertyName(name);
        if (n == null) w.WriteNullValue(); else n.WriteTo(w);
    }

    public static string Init(InitInfo i) => Write(w =>
    {
        w.WriteString("type", "init");
        Node(w, "settings", i.Settings);
        w.WriteString("theme", i.Theme);
        Node(w, "palette", i.Palette);          // so a plugin can draw in the theme without reading files
        w.WriteString("omawinVersion", i.OmawinVersion);
        w.WriteString("id", i.Id);
        w.WriteString("instance", i.Instance);
        w.WriteString("pluginDir", i.PluginDir); // a Linux path for side: linux
        w.WriteString("dataDir", i.DataDir);
        w.WriteString("side", i.Side);
    });

    public static string Click(string button) => Write(w => { w.WriteString("type", "click"); w.WriteString("button", button); });
    public static string Wheel(int delta) => Write(w => { w.WriteString("type", "wheel"); w.WriteNumber("delta", delta); });
    public static string Panel(bool open, string name) => Write(w => { w.WriteString("type", "panel"); w.WriteBoolean("open", open); w.WriteString("name", name); });
    public static string Action(string name, JsonNode? args) => Write(w => { w.WriteString("type", "action"); w.WriteString("name", name); Node(w, "args", args); });
    public static string Settings(JsonObject s) => Write(w => { w.WriteString("type", "settings"); Node(w, "settings", s); });
    public static string Theme(string name, JsonNode? palette) => Write(w => { w.WriteString("type", "theme"); w.WriteString("name", name); Node(w, "palette", palette); });
    public static string Event(string source, string name, JsonNode? data) => Write(w => { w.WriteString("type", "event"); w.WriteString("source", source); w.WriteString("name", name); Node(w, "data", data); });
    public static string Query(int id, string text) => Write(w => { w.WriteString("type", "query"); w.WriteNumber("id", id); w.WriteString("text", text); });
    public static string Cancel(int id) => Write(w => { w.WriteString("type", "cancel"); w.WriteNumber("id", id); });
    public static string Find(int id, string query, int limit, JsonObject filters) => Write(w => { w.WriteString("type", "find"); w.WriteNumber("id", id); w.WriteString("query", query); w.WriteNumber("limit", limit); Node(w, "filters", filters); });
    public static string Range(DateOnly from, DateOnly to) => Write(w => { w.WriteString("type", "range"); w.WriteString("from", from.ToString("yyyy-MM-dd")); w.WriteString("to", to.ToString("yyyy-MM-dd")); });
    public static string AdminResult(string verb, bool ok, string message) => Write(w => { w.WriteString("type", "admin-result"); w.WriteString("verb", verb); w.WriteBoolean("ok", ok); w.WriteString("message", message); });
    public static string SlotRect(int x, int y, int width, int height, double scale) => Write(w => { w.WriteString("type", "slot-rect"); w.WriteNumber("x", x); w.WriteNumber("y", y); w.WriteNumber("width", width); w.WriteNumber("height", height); w.WriteNumber("scale", scale); });
    public static string Shutdown() => Write(w => w.WriteString("type", "shutdown"));
}
