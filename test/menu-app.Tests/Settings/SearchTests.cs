namespace SystemMenu.Tests.Settings;

public class SearchTests
{
    static readonly string Root = CatalogTests.RepoRoot();
    static readonly List<SettingEntry> Shipped = Catalog.Parse([("settings.txt", File.ReadAllLines(Path.Combine(Root, "default", "launcher", "settings.txt")))]).Entries;
    static readonly List<IndexedSetting> Pages = SettingsIndex.ParsePages(File.ReadAllLines(Path.Combine(Root, "default", "launcher", "settings-pages.txt")));

    // The roadmap's "never open Settings" list starts with a few letters each: these must put the
    // switch first, against the whole shipped catalog and every page.
    [Theory]
    [InlineData("dark", "look.dark-apps")]
    [InlineData("ext", "explorer.extensions")]
    [InlineData("hz", "display.refresh-rate")]
    [InlineData("hidden", "explorer.hidden")]
    [InlineData("mic", "privacy.microphone")]
    [InlineData("accel", "input.mouse-accel")]
    [InlineData("long path", "developer.long-paths")]
    [InlineData("power mode", "system.power-mode")]
    public void EverydayWordsFindTheSwitch(string query, string id) =>
        Assert.Equal("setting:" + id, SettingSearch.Rank(Shipped, Pages, query)[0].Key);

    [Fact]
    public void PagesFillTheLongTail()
    {
        var top = SettingSearch.Rank(Shipped, Pages, "device manager")[0];
        Assert.Equal("devmgmt.msc", top.Page?.Uri);
    }

    [Fact]
    public void APageNamedLikeASwitchIsLeftOut()
    {
        var pages = new List<IndexedSetting> { new("Night light", "System › Display", "", "ms-settings:nightlight") };
        var entries = Fixture.Entries();
        Assert.Single(SettingSearch.Rank(entries, pages, "night"));
    }

    [Fact]
    public void WhatYouPickedForAQueryComesFirstNextTime()
    {
        var entries = Shipped.Where(e => e.Id is "look.dark-apps" or "look.dark-windows").ToList();
        Assert.Equal("setting:look.dark-apps", SettingSearch.Rank(entries, [], "dark")[0].Key);
        var picked = SettingSearch.Rank(entries, [], "dark", (key, q) => key == "setting:look.dark-windows" && q == "dark" ? 3 : 0);
        Assert.Equal("setting:look.dark-windows", picked[0].Key);
    }

    [Fact]
    public void NothingTypedFindsNothing() => Assert.Empty(SettingSearch.Rank(Shipped, Pages, "  "));

    [Fact]
    public void ShippedPagesAreAllUsable()
    {
        var lines = File.ReadAllLines(Path.Combine(Root, "default", "launcher", "settings-pages.txt"))
            .Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith('#')).ToList();
        Assert.Equal(lines.Count, Pages.Count); // every line parsed
        Assert.Equal(Pages.Count, Pages.Select(p => p.Uri).Distinct().Count());
    }
}

public class IndexTests
{
    // The shape remembered from Windows' AllSystemSettings file (plans/02 §10); the reader collects
    // by element name, so a nesting change on a new build still reads.
    const string Xml = """
        <?xml version="1.0" encoding="utf-8"?>
        <PCSettings>
          <SearchableContent xmlns="http://schemas.microsoft.com/Search/2013/SettingContent">
            <ApplicationInformation><AppID>windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel</AppID>
              <DeepLink>ms-settings:display</DeepLink></ApplicationInformation>
            <SettingIdentity><PageID>SettingsPageDisplay</PageID><SettingID>SettingsBrightness</SettingID></SettingIdentity>
            <SettingInformation><Description>@shell,-2</Description><Keywords>@shell,-3</Keywords><Name>@shell,-1</Name></SettingInformation>
          </SearchableContent>
          <SearchableContent>
            <ApplicationInformation><DeepLink>calc.exe</DeepLink></ApplicationInformation>
            <SettingIdentity><PageID>SettingsPageMouse</PageID></SettingIdentity>
            <SettingInformation><Name>Pointer speed</Name></SettingInformation>
          </SearchableContent>
          <SearchableContent>
            <SettingInformation><Name>@missing,-9</Name></SettingInformation>
          </SearchableContent>
          <SearchableContent>
            <ApplicationInformation><DeepLink>ms-settings:display</DeepLink></ApplicationInformation>
            <SettingInformation><Name>@shell,-1</Name></SettingInformation>
          </SearchableContent>
        </PCSettings>
        """;

    static string? Resolve(string? s) => s switch
    {
        "@shell,-1" => "Change brightness", "@shell,-2" => "Make the screen brighter", "@shell,-3" => "dim;bright",
        { } t when t.StartsWith('@') => null, // a resource this PC doesn't have
        _ => s,
    };

    static (string, string)? PageOf(string id) => id == "SettingsPageMouse" ? ("ms-settings:mousetouchpad", "Bluetooth & devices › Mouse") : null;

    [Fact]
    public void ReadsNamesKeywordsAndLinks()
    {
        var rows = SettingsIndex.ReadXml(new StringReader(Xml), Resolve, PageOf);
        Assert.Equal(2, rows.Count); // the unresolvable one and the repeat are dropped
        Assert.Equal(new IndexedSetting("Change brightness", "Settings", "dim bright Make the screen brighter", "ms-settings:display"), rows[0]);
        // Only ms-settings: links are taken from the file; others fall back to the page's.
        Assert.Equal(new IndexedSetting("Pointer speed", "Bluetooth & devices › Mouse", "", "ms-settings:mousetouchpad"), rows[1]);
    }

    [Fact]
    public void TheCacheReadsBackWhatWasWritten()
    {
        var rows = new List<IndexedSetting> { new("A\tB", "P", "k1 k2", "ms-settings:x") };
        var lines = SettingsIndex.CacheLines(SettingsIndex.Stamp(@"C:\Windows\x.xml", 42, "en-US"), rows).ToList();
        Assert.Equal("#\tC:\\Windows\\x.xml\t42\ten-US", lines[0]);
        Assert.Equal([new IndexedSetting("A B", "P", "k1 k2", "ms-settings:x")], SettingsIndex.ParseCache(lines));
    }

    [Fact]
    public void ADoctypeIsRefused() =>
        Assert.ThrowsAny<System.Xml.XmlException>(() => SettingsIndex.ReadXml(
            new StringReader("<!DOCTYPE x [<!ENTITY e SYSTEM \"file:///etc/passwd\">]><x>&e;</x>"), Resolve, PageOf));
}
