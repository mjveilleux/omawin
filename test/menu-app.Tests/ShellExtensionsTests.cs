namespace SystemMenu.Tests;

public class ShellExtensionsTests
{
    const string SevenZip = "{23170F69-40C1-278A-1000-000100020000}";
    const string Defender = "{09A47860-11B0-4DA5-AFA5-26D86198A780}";

    static readonly ShellExtFound[] Found =
    [
        new("{23170f69-40c1-278a-1000-000100020000}", "7-Zip Shell Extension", "*", "ContextMenuHandlers", @"C:\Program Files\7-Zip\7-zip.dll", "Igor Pavlov"),
        new(SevenZip, "7-Zip Shell Extension", "Directory", "ContextMenuHandlers", @"C:\Program Files\7-Zip\7-zip.dll", "Igor Pavlov"),
        new(SevenZip, "7-Zip Shell Extension", "Drive", "DragDropHandlers", @"C:\Program Files\7-Zip\7-zip.dll", "Igor Pavlov"),
        new(Defender, "Scan with Microsoft Defender", "*", "ContextMenuHandlers", @"C:\Program Files\Windows Defender\shellext.dll", "Microsoft Corporation"),
        new("not a guid", "broken", "*", "ContextMenuHandlers", null, null),
    ];

    [Fact]
    public void OneRowPerExtensionOthersFirst()
    {
        var all = ShellExtensions.Merge(Found, new HashSet<string> { SevenZip }, new HashSet<string>());
        Assert.Equal([SevenZip, Defender], all.Select(e => e.Clsid));
        Assert.Equal(["right-click on files", "right-click on folders", "drag and drop on drives"], all[0].Where);
        Assert.True(all[0].Blocked);
        Assert.True(all[1].Windows);
    }

    [Fact]
    public void ATypedWordFindsOne()
    {
        var all = ShellExtensions.Merge(Found, new HashSet<string>(), new HashSet<string>());
        Assert.Equal(SevenZip, ShellExtensions.Match(all, "7-zip").Ext?.Clsid);           // its program's file name
        Assert.Equal(Defender, ShellExtensions.Match(all, "defender").Ext?.Clsid);
        Assert.Equal(Defender, ShellExtensions.Match(all, Defender.ToLowerInvariant().Trim('{', '}')).Ext?.Clsid);
        var (none, ambiguous) = ShellExtensions.Match(all, "shell");                         // both names have it
        Assert.Null(none);
        Assert.Equal(2, ambiguous.Count);
    }

    [Fact]
    public void ListingSaysWhatsOff()
    {
        var all = ShellExtensions.Merge(Found, new HashSet<string>(), new HashSet<string> { Defender });
        var text = ShellExtensions.Format(all);
        Assert.Contains("1 from other programs, 1 Windows' own", text);
        Assert.Contains("off for the PC", text);
        Assert.Contains(@"right-click on files, right-click on folders, drag and drop on drives: C:\Program Files\7-Zip\7-zip.dll", text);
    }
}
