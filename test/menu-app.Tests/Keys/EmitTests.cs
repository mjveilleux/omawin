using System.Text.Json;

namespace SystemMenu.Tests.Keys;

// Keys GlazeWM emits (`emit omawin <command words>`, roadmap 1.2) resolve as omawin.exe would.
public class EmitTests
{
    // The rows the menu keys use, as bin/commands.txt has them, plus a comment and a script row.
    static readonly Dictionary<string, string> Table = Emit.Table([
        "# name | summary | action",
        "",
        "launcher | The launcher (Win+Space); with a page name, that page | app --launcher",
        "files | The file finder (Win+F) | app --files",
        "menu | The system menu | app --open",
        "capture text | Text from the screen | app --snip text",
        "theme next-background | The next background | pwsh themes/next-background.ps1",
        "not a row",
    ]);

    [Theory]
    [InlineData("launcher", "--launcher")]
    [InlineData("launcher clipboard", "--launcher clipboard")]
    [InlineData("launcher audio", "--launcher audio")]
    [InlineData("files", "--files")]
    [InlineData("menu install", "--open install")]
    [InlineData("capture text", "--snip text")]
    public void AppRowsBecomeTheMenuAppsRequest(string words, string request)
    {
        var resolved = Emit.Resolve(Table, words.Split(' '));
        Assert.Equal(Emit.Kind.Request, resolved.Kind);
        Assert.Equal(request.Split(' '), resolved.Words);
    }

    [Fact]
    public void OtherRowsRunThroughOmawin()
    {
        var resolved = Emit.Resolve(Table, ["theme", "next-background"]);
        Assert.Equal(Emit.Kind.Omawin, resolved.Kind);
        Assert.Equal(["theme", "next-background"], resolved.Words);
    }

    [Theory]
    [InlineData("nothing")]
    [InlineData("not")]
    [InlineData("")]
    public void UnknownWordsAreNoCommand(string words) =>
        Assert.Equal(Emit.Kind.Unknown, Emit.Resolve(Table, words.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Kind);

    [Fact]
    public void CommentsAndShortRowsAreSkipped()
    {
        Assert.Equal(5, Table.Count);
        Assert.False(Table.ContainsKey("not a row"));
    }

    // The fork's event, as its tests pin it (wm_event.rs): {"eventType":"custom","name":...,"args":[...]}.
    [Fact]
    public void ReadsTheForksCustomEvent()
    {
        using var doc = JsonDocument.Parse("""{"eventType":"custom","name":"omawin","args":["launcher","clipboard"]}""");
        Assert.True(Emit.TryRead(doc.RootElement, out var name, out var words));
        Assert.Equal("omawin", name);
        Assert.Equal(["launcher", "clipboard"], words);
    }

    [Fact]
    public void AnEventWithoutArgsHasNoWords()
    {
        using var doc = JsonDocument.Parse("""{"eventType":"custom","name":"omawin"}""");
        Assert.True(Emit.TryRead(doc.RootElement, out _, out var words));
        Assert.Empty(words);
    }

    [Theory]
    [InlineData("""{"eventType":"focus_changed","focusedContainer":{}}""")]
    [InlineData("""{"eventType":"custom"}""")]
    [InlineData("""null""")]
    public void OtherEventsAreNotCustom(string json)
    {
        using var doc = JsonDocument.Parse(json);
        Assert.False(Emit.TryRead(doc.RootElement, out _, out _));
    }
}
