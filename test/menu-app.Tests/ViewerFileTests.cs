namespace SystemMenu.Tests;

// omawin's viewer (8.25): what it shows, how a path reaches it, the pictures beside one.
public class ViewerFileTests
{
    [Fact]
    public void Kinds()
    {
        Assert.Equal(ViewerFile.Kind.Picture, ViewerFile.KindOf(@"C:\a\B.PNG"));
        Assert.Equal(ViewerFile.Kind.Text, ViewerFile.KindOf(@"C:\a\notes.md"));
        Assert.Equal(ViewerFile.Kind.Other, ViewerFile.KindOf(@"C:\a\setup.exe"));
    }

    [Fact]
    public void PathTravelsAsOneWord()
    {
        string path = OperatingSystem.IsWindows() ? @"C:\My Pictures\a b%20c #1.png" : "/My Pictures/a b%20c #1.png";
        var word = ViewerFile.Encode(path);
        Assert.DoesNotContain(' ', word);
        Assert.Equal(path, ViewerFile.Decode(word));
        // Not a full path: nothing (a request can't open something relative to the menu app's folder).
        Assert.Null(ViewerFile.Decode("a.png"));
        Assert.Null(ViewerFile.Decode(""));
    }

    [Fact]
    public void SiblingsInExplorersOrder()
    {
        var files = new[] { "/p/10.png", "/p/2.png", "/p/notes.txt", "/p/B.jpg", "/p/a.gif" };
        var siblings = ViewerFile.Siblings(files, "/p/2.png");
        Assert.Equal(["/p/2.png", "/p/10.png", "/p/a.gif", "/p/B.jpg"], siblings);
        Assert.Equal("/p/10.png", ViewerFile.Step(siblings, "/p/2.png", 1));
        Assert.Equal("/p/B.jpg", ViewerFile.Step(siblings, "/p/2.png", -1)); // wraps
        Assert.Equal("/p/x.png", ViewerFile.Step(["/p/x.png"], "/p/x.png", 1));
    }

    [Fact]
    public void TextShown()
    {
        var (text, note) = ViewerFile.Shown("a.json", "{\"a\":[1,2],// c\n\"é\":\"<b>\",}", 30);
        Assert.Equal("{\n  \"a\": [\n    1,\n    2\n  ],\n  \"é\": \"<b>\"\n}", text.Replace("\r\n", "\n"));
        Assert.Null(note);
        Assert.Equal("{ not json", ViewerFile.Shown("a.json", "{ not json", 10).Text);
        Assert.StartsWith("The last 1024 KB", ViewerFile.Shown("app.log", "x", 5L << 20).Note);
        Assert.StartsWith("The first 1024 KB", ViewerFile.Shown("a.md", "x", 5L << 20).Note);
    }
}
