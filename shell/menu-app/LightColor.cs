namespace SystemMenu;

// A theme colour as the keyboard's lights take it (roadmap 8.9, KeyboardLights.cs): the palette's
// "#rrggbb" (or "#rgb", or "#aarrggbb" with its alpha dropped: a lamp has no transparency).
// Pure, tested in menu-app.Tests.
public static class LightColor
{
    public static (byte R, byte G, byte B)? Parse(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        var s = hex.Trim().TrimStart('#');
        if (s.Length == 3) s = string.Concat(s.Select(c => new string(c, 2)));
        else if (s.Length == 8) s = s[2..];
        if (s.Length != 6 || !s.All(Uri.IsHexDigit)) return null;
        return (Convert.ToByte(s[..2], 16), Convert.ToByte(s[2..4], 16), Convert.ToByte(s[4..], 16));
    }
}
