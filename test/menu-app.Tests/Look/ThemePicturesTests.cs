namespace SystemMenu.Tests.Look;

// The background picker's names (ThemePictures.cs): the thumbnail file CI ships has to be the one
// the menu app looks for, and the check has to land on the picture on the desktop.
public class ThemePicturesTests
{
    const string Pinned = "https://raw.githubusercontent.com/basecamp/omarchy/ecf0eb3/themes/gruvbox/backgrounds/";

    [Theory]
    [InlineData(Pinned + "3-village-square.jpg", "3-village-square")]
    [InlineData(Pinned + "2-flower-basket.webp?raw=1", "2-flower-basket")]
    [InlineData("themes/backgrounds/starship/1-starbase.png", "1-starbase")]
    [InlineData(@"C:\Users\you\AppData\Local\launcher\themes\sources\mine\backgrounds\dawn.jpg", "dawn")]
    [InlineData("no-extension", "no-extension")]
    public void Name_is_the_file_name_without_extension_or_query(string source, string name) =>
        Assert.Equal(name, ThemePictures.Name(source));

    [Fact]
    public void Thumbnail_is_named_for_theme_and_picture() =>
        Assert.Equal("gruvbox--3-village-square.png", ThemePictures.ThumbName("gruvbox", Pinned + "3-village-square.jpg?x=1"));

    [Theory]
    [InlineData("3-village-square.jpg", "Village square")]
    [InlineData("10_neon_city.png", "Neon city")]
    [InlineData("80s.png", "80s")]       // a number that's the name, not an order
    [InlineData("1-.png", "1-")]         // nothing after the number: the name as it is
    [InlineData("dawn.jpg", "Dawn")]
    public void Label_drops_the_order_number(string source, string label) =>
        Assert.Equal(label, ThemePictures.Label(source));

    static readonly string[] Gruvbox = [Pinned + "1-the-backwater.jpg", Pinned + "2-flower-basket.webp", Pinned + "11-leaves.jpg", Pinned + "1-leaves.jpg"];

    [Fact]
    public void Current_is_the_saved_source() =>
        Assert.Equal(1, ThemePictures.Current(Gruvbox, Pinned + "2-flower-basket.webp\r\n"));

    [Fact]
    public void Current_survives_a_new_omarchy_commit() =>
        Assert.Equal(3, ThemePictures.Current(Gruvbox, "https://raw.githubusercontent.com/basecamp/omarchy/abc1234/themes/gruvbox/backgrounds/1-leaves.jpg"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://example.com/someone-elses.jpg")]
    public void Current_is_none_for_another_picture(string? saved) =>
        Assert.Equal(-1, ThemePictures.Current(Gruvbox, saved));

    [Theory]
    [InlineData("", true)]
    [InlineData("flower", true)]
    [InlineData("FLOWER bask", true)]
    [InlineData("2-flower", true)]
    [InlineData("leaves", false)]
    public void Matches_every_word(string query, bool match) =>
        Assert.Equal(match, ThemePictures.Matches(Pinned + "2-flower-basket.webp", query));

    [Fact]
    public void Stale_leaves_current_pictures_and_other_themes_alone()
    {
        string[] files =
        [
            "catppuccin--1-totoro.png", "catppuccin--2-waves.png", "catppuccin--0-old-one.png",
            "catppuccin-latte--1-color-fade.png", "catppuccin--3-blue-eye.png.part", "nord--1-x.png",
        ];
        string[] backgrounds = ["https://x/1-totoro.png", "https://x/2-waves.jpg", "https://x/3-blue-eye.webp"];
        Assert.Equal(["catppuccin--0-old-one.png"], ThemePictures.Stale("catppuccin", files, backgrounds));
    }
}
