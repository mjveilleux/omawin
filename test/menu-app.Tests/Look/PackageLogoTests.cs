namespace SystemMenu.Tests.Look;

// Store apps' logos come from the package's own files, unplated where it ships them (roadmap 1.8
// step 4). The names below are the ones real packages ship.
public class PackageLogoTests
{
    const string Logo = "Square44x44Logo.png";

    // What Calculator-style packages ship: targetsize unplated, light unplated, plated, and
    // scale variants, plus high-contrast ones.
    static readonly string[] Full =
    [
        "Square44x44Logo.png",
        "Square44x44Logo.scale-100.png",
        "Square44x44Logo.scale-200.png",
        "Square44x44Logo.scale-400.png",
        "Square44x44Logo.targetsize-16.png",
        "Square44x44Logo.targetsize-24.png",
        "Square44x44Logo.targetsize-48.png",
        "Square44x44Logo.targetsize-256.png",
        "Square44x44Logo.targetsize-16_altform-unplated.png",
        "Square44x44Logo.targetsize-24_altform-unplated.png",
        "Square44x44Logo.targetsize-32_altform-unplated.png",
        "Square44x44Logo.targetsize-48_altform-unplated.png",
        "Square44x44Logo.targetsize-256_altform-unplated.png",
        "Square44x44Logo.targetsize-24_altform-lightunplated.png",
        "Square44x44Logo.targetsize-48_altform-lightunplated.png",
        "Square44x44Logo.targetsize-48_contrast-black.png",
        "Square44x44Logo.targetsize-48_altform-unplated_contrast-white.png",
        "Square44x44Logo.scale-200_contrast-high.png",
    ];

    static string? Pick(string[] files, int want, bool light = false, bool plateless = true) =>
        PackageLogo.Best(files, Logo, want, light, plateless)?.File;

    [Theory]
    [InlineData(16, "Square44x44Logo.targetsize-16_altform-unplated.png")]
    [InlineData(26, "Square44x44Logo.targetsize-32_altform-unplated.png")]   // a row at 100%
    [InlineData(39, "Square44x44Logo.targetsize-48_altform-unplated.png")]   // a row at 150%
    [InlineData(48, "Square44x44Logo.targetsize-48_altform-unplated.png")]
    [InlineData(92, "Square44x44Logo.targetsize-256_altform-unplated.png")]  // a tile at 200%
    [InlineData(300, "Square44x44Logo.targetsize-256_altform-unplated.png")] // bigger than any: the largest
    public void OnDarkTheSmallestUnplatedAtOrAboveTheSizeWins(int want, string file) =>
        Assert.Equal(file, Pick(Full, want));

    [Theory]
    [InlineData(20, "Square44x44Logo.targetsize-24_altform-lightunplated.png")]
    [InlineData(39, "Square44x44Logo.targetsize-48_altform-lightunplated.png")]
    [InlineData(92, "Square44x44Logo.targetsize-48_altform-lightunplated.png")] // the light one beats a bigger dark one
    public void OnLightTheLightUnplatedOneWins(int want, string file) =>
        Assert.Equal(file, Pick(Full, want, light: true));

    [Fact]
    public void OnLightWithoutALightOneTheUnplatedOneIsUsed()
    {
        var files = Full.Where(f => !f.Contains("lightunplated")).ToArray();
        Assert.Equal("Square44x44Logo.targetsize-48_altform-unplated.png", Pick(files, 40, light: true));
    }

    [Fact]
    public void TheLightOneIsNeverUsedOnDark()
    {
        string[] files = ["Square44x44Logo.targetsize-48_altform-lightunplated.png"];
        Assert.Null(Pick(files, 48));
        Assert.Null(Pick(files, 48, plateless: false));
    }

    [Fact]
    public void HighContrastVariantsAreIgnored()
    {
        string[] files =
        [
            "Square44x44Logo.targetsize-48_contrast-black.png",
            "Square44x44Logo.targetsize-48_altform-unplated_contrast-white.png",
            "Square44x44Logo.scale-200_contrast-high.png",
        ];
        Assert.Null(Pick(files, 48));
        Assert.Equal("Square44x44Logo.targetsize-48_altform-unplated_contrast-standard.png",
            Pick([.. files, "Square44x44Logo.targetsize-48_altform-unplated_contrast-standard.png"], 48));
    }

    [Fact]
    public void WithoutUnplatedOnesTargetsizeComesBeforeScale()
    {
        string[] files =
        [
            "Square44x44Logo.scale-100.png",
            "Square44x44Logo.scale-200.png",
            "Square44x44Logo.targetsize-24.png",
            "Square44x44Logo.targetsize-48.png",
        ];
        Assert.Equal("Square44x44Logo.targetsize-48.png", Pick(files, 39));
        Assert.Equal("Square44x44Logo.targetsize-48.png", Pick(files, 80)); // the largest targetsize, not scale-200's 88
    }

    [Theory]
    [InlineData(40, "Square44x44Logo.scale-100.png")]   // 44 pixels
    [InlineData(60, "Square44x44Logo.scale-150.png")]   // 66
    [InlineData(80, "Square44x44Logo.scale-200.png")]   // 88
    [InlineData(200, "Square44x44Logo.scale-400.png")]  // 176, the largest
    public void ScaleVariantsAre44PixelsAt100(int want, string file) =>
        Assert.Equal(file, Pick(["Square44x44Logo.scale-100.png", "Square44x44Logo.scale-150.png",
            "Square44x44Logo.scale-200.png", "Square44x44Logo.scale-400.png"], want));

    [Fact]
    public void ThePixelsAndKindAreReported()
    {
        var choice = PackageLogo.Best(Full, Logo, 92, light: false, plateless: true)!;
        Assert.Equal(256, choice.Pixels);
        Assert.True(choice.Unplated);
        var scaled = PackageLogo.Best(["Square44x44Logo.scale-200.png"], Logo, 40, false, true)!;
        Assert.Equal(88, scaled.Pixels);
        Assert.False(scaled.Unplated);
    }

    [Fact]
    public void PlatedOnesAreLeftToTheShellWhenThePlateIsColoured()
    {
        string[] plated = ["Square44x44Logo.scale-200.png", "Square44x44Logo.targetsize-48.png", "Square44x44Logo.png"];
        Assert.Null(Pick(plated, 48, plateless: false));
        Assert.Equal("Square44x44Logo.targetsize-48_altform-unplated.png",
            Pick([.. plated, "Square44x44Logo.targetsize-48_altform-unplated.png"], 48, plateless: false));
    }

    [Fact]
    public void TheBareFileIsScale100()
    {
        var choice = PackageLogo.Best(["Square44x44Logo.png"], Logo, 26, false, true)!;
        Assert.Equal(("Square44x44Logo.png", 44), (choice.File, choice.Pixels));
    }

    [Fact]
    public void QualifiersInAnyOrderAndInFolders()
    {
        Assert.Equal("Square44x44Logo.altform-unplated_targetsize-32.png",
            Pick(["Square44x44Logo.altform-unplated_targetsize-32.png", "Square44x44Logo.targetsize-32.png"], 32));
        Assert.Equal(@"scale-200\Square44x44Logo.png", Pick([@"scale-100\Square44x44Logo.png", @"scale-200\Square44x44Logo.png"], 80));
        Assert.Equal("contrast-standard/Square44x44Logo.targetsize-48_altform-unplated.png",
            Pick(["contrast-black/Square44x44Logo.targetsize-48_altform-unplated.png",
                  "contrast-standard/Square44x44Logo.targetsize-48_altform-unplated.png"], 48));
    }

    [Fact]
    public void TheOtherThemesVariantIsSkipped()
    {
        string[] files = ["Square44x44Logo.targetsize-48_altform-unplated_theme-light.png", "Square44x44Logo.targetsize-32_altform-unplated_theme-dark.png"];
        Assert.Equal("Square44x44Logo.targetsize-32_altform-unplated_theme-dark.png", Pick(files, 48));
        Assert.Equal("Square44x44Logo.targetsize-48_altform-unplated_theme-light.png", Pick(files, 48, light: true));
    }

    [Fact]
    public void OtherFilesAndAltformsAreNotVariants()
    {
        string[] files =
        [
            "Square44x44LogoBig.png",
            "Square44x44Logo.old.png",
            "Square44x44Logo.targetsize-48.jpg",
            "Square44x44Logo.targetsize-big.png",
            "Square44x44Logo.targetsize-48_altform-colorful.png",
            "Square150x150Logo.scale-200.png",
            @"old\Square44x44Logo.targetsize-48_altform-unplated.png",
        ];
        Assert.Null(Pick(files, 48));
    }

    [Fact]
    public void ALanguageVariantLosesATieToThePlainOne() =>
        Assert.Equal("Square44x44Logo.targetsize-48_altform-unplated.png",
            Pick(["Square44x44Logo.lang-en-us_targetsize-48_altform-unplated.png", "Square44x44Logo.targetsize-48_altform-unplated.png"], 48));

    [Fact]
    public void NamesAreMatchedWhateverTheirCase() =>
        Assert.Equal("square44x44logo.TargetSize-48_AltForm-Unplated.PNG", Pick(["square44x44logo.TargetSize-48_AltForm-Unplated.PNG"], 48));

    [Theory]
    [InlineData("scale-200", true)]
    [InlineData("contrast-standard", true)]
    [InlineData("lang-en-us_scale-100", true)]
    [InlineData("Images", false)]
    [InlineData("old-stuff", false)]
    public void QualifierFolders(string name, bool qualifier) =>
        Assert.Equal(qualifier, PackageLogo.IsQualifierFolder(name));

    [Theory]
    [InlineData("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", "Microsoft.WindowsCalculator_8wekyb3d8bbwe", "App")]
    [InlineData("Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", "Microsoft.WindowsTerminal_8wekyb3d8bbwe", "App")]
    [InlineData("5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App", "5319275A.WhatsAppDesktop_cv1g1gvanyjgm", "App")]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", "Microsoft.ZuneMusic_8wekyb3d8bbwe", "Microsoft.ZuneMusic")]
    public void StoreAppIdsSplit(string id, string family, string app) =>
        Assert.Equal((family, app), PackageLogo.SplitAumid(id));

    [Theory]
    [InlineData(@"{6D809377-6AF0-444B-8957-A3773F02200E}\Git\git-bash.exe")]
    [InlineData(@"C:\Tools\app.exe")]
    [InlineData("MSEdge")]
    [InlineData("Microsoft.Windows.Explorer")]
    [InlineData("NoPublisher!App")]
    [InlineData("Name_SHORT!App")]
    [InlineData("Microsoft.WindowsCalculator_8wekyb3d8bbwe!")]
    [InlineData("A_8wekyb3d8bbwe!App!Again")]
    public void OtherIdsAreNotStoreApps(string id) =>
        Assert.Null(PackageLogo.SplitAumid(id));

    const string Manifest = """
        <?xml version="1.0" encoding="utf-8"?>
        <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
                 xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10">
          <Applications>
            <Application Id="Other" Executable="other.exe">
              <uap:VisualElements DisplayName="Other" Square150x150Logo="Assets\Other150.png"
                                  Square44x44Logo="Assets\Other44.png" BackgroundColor="#0078D4" Description="x" />
            </Application>
            <Application Id="App" Executable="app.exe">
              <uap:VisualElements DisplayName="App" Square150x150Logo="Assets\Square150x150Logo.png"
                                  Square44x44Logo="Assets\Square44x44Logo.png" BackgroundColor="transparent" Description="x" />
            </Application>
          </Applications>
        </Package>
        """;

    [Fact]
    public void TheManifestNamesEachAppsLogoAndPlate()
    {
        Assert.Equal(new PackageLogo.Visual(@"Assets\Square44x44Logo.png", true), PackageLogo.Manifest(Manifest, "App"));
        Assert.Equal(new PackageLogo.Visual(@"Assets\Other44.png", false), PackageLogo.Manifest(Manifest, "Other"));
        Assert.Null(PackageLogo.Manifest(Manifest, "Missing"));
    }

    [Theory]
    [InlineData("<Package")]                                            // not XML
    [InlineData(@"<Package><Applications><Application Id=""App""><VisualElements Square44x44Logo=""..\..\x.png"" /></Application></Applications></Package>")]
    [InlineData(@"<Package><Applications><Application Id=""App""><VisualElements Square44x44Logo=""C:\x.png"" /></Application></Applications></Package>")]
    [InlineData(@"<Package><Applications><Application Id=""App""><VisualElements Square44x44Logo=""ms-resource:Logo"" /></Application></Applications></Package>")]
    [InlineData(@"<Package><Applications><Application Id=""App""><VisualElements DisplayName=""x"" /></Application></Applications></Package>")]
    [InlineData(@"<!DOCTYPE Package [<!ENTITY x ""y"">]><Package />")] // no DTDs
    public void ABadOrUnsafeManifestGivesNothing(string xml) =>
        Assert.Null(PackageLogo.Manifest(xml, "App"));
}
