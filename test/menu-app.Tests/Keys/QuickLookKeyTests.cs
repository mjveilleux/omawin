namespace SystemMenu.Tests.Keys;

// Quick Look's Space in Explorer (roadmap 8.4, QuickLookKey in KeyRules.cs): taken only in the
// file list, and only when it can't be part of a name being typed.
public class QuickLookKeyTests
{
    const uint Space = 0x20, M = 0x4D;
    static readonly KeyFocus FileList = new("CabinetWClass", "DirectUIHWND", "SHELLDLL_DefView");

    static (bool Swallow, bool Fire) Press(QuickLookKey q, uint vk, bool down, uint time, KeyFocus? at = null, bool modified = false)
    {
        bool swallow = q.OnKey(vk, down, modified, time, () => at ?? FileList, out bool fire);
        return (swallow, fire);
    }

    [Fact]
    public void SpaceInTheFileListOpensItOnceAndTakesItsRepeatsAndKeyUp()
    {
        var q = new QuickLookKey { On = true };
        Assert.Equal((true, true), Press(q, Space, true, 0));
        Assert.Equal((true, false), Press(q, Space, true, 30)); // auto-repeat
        Assert.Equal((true, false), Press(q, Space, false, 60));
        Assert.Equal((false, false), Press(q, Space, false, 90)); // an up it never took goes on
    }

    [Fact]
    public void OffItLeavesSpaceAlone()
    {
        var q = new QuickLookKey();
        Assert.Equal((false, false), Press(q, Space, true, 0));
    }

    [Theory]
    [InlineData("CabinetWClass", "Edit", "DirectUIHWND")]                                   // renaming a file
    [InlineData("CabinetWClass", "Microsoft.UI.Content.DesktopChildSiteBridge", "")]       // the address bar or search box
    [InlineData("CabinetWClass", "SysTreeView32", "NamespaceTreeControl")]                 // the folder tree
    [InlineData("Progman", "DirectUIHWND", "SHELLDLL_DefView")]                             // the desktop's icons
    [InlineData("Chrome_WidgetWin_1", "Chrome_RenderWidgetHostHWND", "Chrome_WidgetWin_1")] // another app
    public void AnywhereElseSpaceTypesASpace(string front, string focus, string parent)
    {
        var q = new QuickLookKey { On = true };
        Assert.Equal((false, false), Press(q, Space, true, 0, new KeyFocus(front, focus, parent)));
        Assert.Equal((false, false), Press(q, Space, false, 20, new KeyFocus(front, focus, parent)));
    }

    [Fact]
    public void ASpaceWhileTypingToFindANameIsPartOfTheName()
    {
        var q = new QuickLookKey { On = true };
        Press(q, M, true, 1000);
        Assert.Equal((false, false), Press(q, Space, true, 1500));
        Assert.Equal((true, true), Press(q, Space, true, 1000 + QuickLookKey.TypeAheadMs));
    }

    [Fact]
    public void TypingInAnotherWindowDoesntCount()
    {
        var q = new QuickLookKey { On = true };
        Press(q, M, true, 1000);
        q.Reset(); // Explorer came to the front
        Assert.Equal((true, true), Press(q, Space, true, 1100));
    }

    [Fact]
    public void WithAModifierSpaceIsExplorers()
    {
        var q = new QuickLookKey { On = true };
        Assert.Equal((false, false), Press(q, Space, true, 0, modified: true)); // Ctrl+Space selects
    }

    [Fact]
    public void TheFocusIsOnlyAskedForASpace()
    {
        var q = new QuickLookKey { On = true };
        int asked = 0;
        q.OnKey(M, true, false, 0, () => { asked++; return FileList; }, out _);
        q.OnKey(M, false, false, 10, () => { asked++; return FileList; }, out _);
        Assert.Equal(0, asked);
    }
}
