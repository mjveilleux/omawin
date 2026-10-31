using System.Text.Json.Nodes;
using SystemMenu.Plugins;

namespace SystemMenu.Tests.Plugins;

public class ProtocolTests
{
    [Fact]
    public void ReadsWaybarsBarLine()
    {
        var m = Assert.IsType<BarLine>(Protocol.Parse("""{"text":"󰖐 14°","tooltip":"Light rain","class":"rain","percentage":60}"""));
        Assert.Equal("󰖐 14°", m.Text);
        Assert.Equal(["rain"], m.Classes);
        Assert.Equal(60, m.Percentage);
    }

    [Fact]
    public void ClassMayBeAListOrSpaceSeparated()
    {
        Assert.Equal(["a", "b"], Assert.IsType<BarLine>(Protocol.Parse("""{"text":"x","class":["a","b"]}""")).Classes);
        Assert.Equal(["a", "b"], Assert.IsType<BarLine>(Protocol.Parse("""{"text":"x","class":"a b"}""")).Classes);
    }

    [Fact]
    public void ReadsEachMessageType()
    {
        var state = Assert.IsType<StateMessage>(Protocol.Parse("""{"type":"state","data":{"time":"24:13"},"merge":true}"""));
        Assert.True(state.Merge);
        Assert.Equal("24:13", state.Data["time"]!.GetValue<string>());

        var notify = Assert.IsType<NotifyMessage>(Protocol.Parse("""{"type":"notify","title":"Break time"}"""));
        Assert.Equal("normal", notify.Urgency);

        Assert.True(Assert.IsType<PanelRequest>(Protocol.Parse("""{"type":"open-panel"}""")).Open);
        Assert.False(Assert.IsType<PanelRequest>(Protocol.Parse("""{"type":"close-panel","name":"main"}""")).Open);

        var run = Assert.IsType<RunMessage>(Protocol.Parse("""{"type":"run","command":["omawin","launch","terminal"]}"""));
        Assert.Equal(["omawin", "launch", "terminal"], run.Command!);

        var results = Assert.IsType<ResultsMessage>(Protocol.Parse("""{"type":"results","id":7,"items":[{"name":"a"}],"done":true}"""));
        Assert.Equal((7, 1, true), (results.Id, results.Items.Count, results.Done));

        var events = Assert.IsType<CalendarMessage>(Protocol.Parse("""{"type":"events","from":"2026-10-01","to":"2026-10-31","items":[]}"""));
        Assert.Equal("events", events.Type);

        var reply = Assert.IsType<ReplyMessage>(Protocol.Parse("""{"type":"reply","req":3,"ok":true,"data":{"n":1}}"""));
        Assert.Equal(3, reply.Req);

        var admin = Assert.IsType<AdminRequest>(Protocol.Parse("""{"type":"admin","verb":"dns-set","args":["Wi-Fi","1.1.1.1"],"why":"Cloudflare DNS"}"""));
        Assert.Equal(["Wi-Fi", "1.1.1.1"], admin.Args);

        Assert.Equal(120, Assert.IsType<SlotMessage>(Protocol.Parse("""{"type":"slot","width":120}""")).Width);
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain text from a script")]
    [InlineData("{ broken json")]
    [InlineData("[1,2,3]")]
    [InlineData("""{"type":"from-the-future"}""")]
    public void AnythingElseIsNotJsonAndNeverThrows(string line) =>
        Assert.IsType<NotJson>(Protocol.Parse(line));

    [Fact]
    public void OnlyOmawinCommandsMayBeRunThroughTheShell()
    {
        Assert.True(Protocol.RunAllowed(new RunMessage(["omawin", "theme", "set", "nord"], null)));
        Assert.True(Protocol.RunAllowed(new RunMessage(["omawin.exe", "notify", "hi"], null)));
        Assert.False(Protocol.RunAllowed(new RunMessage(["cmd.exe", "/c", "del", "*"], null)));
        Assert.False(Protocol.RunAllowed(new RunMessage(null, "https://example.com")));
        Assert.True(Protocol.OpenAllowed("https://github.com"));
        Assert.False(Protocol.OpenAllowed("file:///C:/Windows/System32/calc.exe"));
    }

    [Fact]
    public void InitCarriesWhatAPluginNeedsAndRoundTrips()
    {
        var line = Protocol.Init(new InitInfo(
            Id: "b.timer", Instance: "b.timer", Settings: new JsonObject { ["workMinutes"] = 25 },
            Theme: "tokyo-night", Palette: new JsonObject { ["accent"] = "#7aa2f7" },
            OmawinVersion: "1.0.0", PluginDir: @"C:\Users\you\omawin-account\plugins\b.timer", DataDir: "/data", Side: "windows"));
        Assert.DoesNotContain('\n', line); // one message, one line
        var o = JsonNode.Parse(line)!.AsObject();
        Assert.Equal("init", o["type"]!.GetValue<string>());
        Assert.Equal(25, o["settings"]!["workMinutes"]!.GetValue<int>());
        Assert.Equal("#7aa2f7", o["palette"]!["accent"]!.GetValue<string>());
        Assert.Equal(@"C:\Users\you\omawin-account\plugins\b.timer", o["pluginDir"]!.GetValue<string>());
    }

    [Fact]
    public void WrittenMessagesKeepTextReadable()
    {
        var line = Protocol.Action("note", new JsonObject { ["text"] = "Café “quotes” 󰖐" });
        Assert.Contains("Café “quotes”", line); // ordinary text isn't \u-escaped
        // A Nerd Font glyph (outside Unicode's basic plane) is escaped on the wire, and reads back the same.
        Assert.Equal("Café “quotes” 󰖐", JsonNode.Parse(line)!["args"]!["text"]!.GetValue<string>());
    }

    [Fact]
    public void EveryShellMessageIsOneValidLineWithItsType()
    {
        string[] lines =
        [
            Protocol.Click("left"), Protocol.Wheel(-1), Protocol.Panel(true, "main"), Protocol.Action("refresh", null),
            Protocol.Settings(new JsonObject()), Protocol.Theme("nord", null), Protocol.Event("glazewm", "focus_changed", null),
            Protocol.Query(1, "dark"), Protocol.Cancel(1), Protocol.Find(2, "road", 50, new JsonObject()),
            Protocol.Range(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)), Protocol.AdminResult("dns-set", true, "done"),
            Protocol.SlotRect(10, 0, 120, 26, 1.5), Protocol.Shutdown(),
        ];
        string[] types = ["click", "wheel", "panel", "action", "settings", "theme", "event", "query", "cancel", "find", "range", "admin-result", "slot-rect", "shutdown"];
        Assert.Equal(types, lines.Select(l => JsonNode.Parse(l)!["type"]!.GetValue<string>()));
        Assert.All(lines, l => Assert.DoesNotContain('\n', l));
    }
}
