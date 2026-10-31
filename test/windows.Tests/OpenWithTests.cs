namespace Omawin.Windows.Tests;

// omawin open-with (8.25): which types, and the registry values that offer omawin's viewer.
public class OpenWithTests
{
    [Fact]
    public void TypesFromWordsAndGroups()
    {
        Assert.Equal(["png", "md"], OpenWith.Types([".PNG", "md", "*.png"], out var unknown));
        Assert.Empty(unknown);
        Assert.Equal(SystemMenu.ViewerFile.Pictures, OpenWith.Types(["pictures"], out _));
        Assert.Empty(OpenWith.Types(["exe", "docx"], out unknown));
        Assert.Equal(["exe", "docx"], unknown);
        // The default set is the roadmap's: no .txt or .csv taken from your editor unless named.
        Assert.DoesNotContain("txt", OpenWith.Default);
        Assert.Contains("log", OpenWith.Default);
    }

    [Fact]
    public void PlanOffersWithoutTouchingUserChoice()
    {
        const string exe = @"C:\Users\me\.local\bin\omawin-view.exe";
        var plan = OpenWith.Plan(exe, ["png"]);
        string Get(string key, string name) => Assert.Single(plan, v => v.Key == key && v.Name == name).Data;
        Assert.Equal("\"C:\\Users\\me\\.local\\bin\\omawin-view.exe\" \"%1\"", Get(@"Software\Classes\omawin.view.png\shell\open\command", ""));
        Assert.Equal("", Get(@"Software\Classes\.png\OpenWithProgids", "omawin.view.png"));
        Assert.Equal("omawin.view.png", Get(@"Software\omawin\Capabilities\FileAssociations", ".png"));
        Assert.Equal(@"Software\omawin\Capabilities", Get(@"Software\RegisteredApplications", "omawin"));
        // Everything is this account's classes and omawin's own keys; never Windows' choice.
        Assert.All(plan, v => Assert.DoesNotContain("UserChoice", v.Key));
        Assert.All(plan, v => Assert.True(v.Key.StartsWith(@"Software\Classes\") || v.Key.StartsWith(@"Software\omawin") || v.Key == @"Software\RegisteredApplications"));
    }

    [Fact]
    public void RemovalUndoesEveryType()
    {
        var (keys, values) = OpenWith.Removal();
        foreach (var t in OpenWith.Supported)
        {
            Assert.Contains(@"Software\Classes\omawin.view." + t, keys);
            Assert.Contains(values, v => v.Key == $@"Software\Classes\.{t}\OpenWithProgids" && v.Name == "omawin.view." + t);
        }
        // The type's own key (.png) is never removed: only omawin's value in it.
        Assert.DoesNotContain(@"Software\Classes\.png", keys);
    }

    [Fact]
    public void StatusLines()
    {
        Assert.Equal(".png      offered  omawin viewer", OpenWith.Line("png", true, "omawin.view.png", null));
        Assert.Equal(".md       -        Windows' default", OpenWith.Line("md", false, null, null));
        Assert.Equal(".json     -        Visual Studio Code", OpenWith.Line("json", false, "Applications\\Code.exe", "Visual Studio Code"));
        // A resource string isn't a name: the ProgID instead.
        Assert.Equal(".jpg      offered  AppX43hnxtbyyps62jhe9sqpdzxn1790zetc", OpenWith.Line("jpg", true, "AppX43hnxtbyyps62jhe9sqpdzxn1790zetc", "@{Microsoft.Windows.Photos}"));
    }
}
