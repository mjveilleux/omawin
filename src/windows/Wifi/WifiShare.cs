using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Omawin.Windows;

// Share the Wi-Fi with a QR code (roadmap 8.21): a phone's camera joins from it, nobody spells out a
// password. Pure: the saved profile's XML in, the code's text and its drawing out. WlanProfile.cs
// asks Windows for the profile; ZXing makes the code's modules.
public sealed record WifiNetwork(string Ssid, string? Key, string Authentication, string Encryption, bool KeyProtected, bool Hidden);

public static partial class WifiShare
{
    static readonly XNamespace Ns = "http://www.microsoft.com/networking/WLAN/profile/v1";

    // A WLANProfile document, as WlanGetProfile returns it. The key is plain text only when Windows
    // handed it over unencrypted (<protected>false</protected>); else Key is null and KeyProtected set.
    public static WifiNetwork? FromProfile(string xml)
    {
        XElement x;
        try { x = XElement.Parse(xml); }
        catch (System.Xml.XmlException) { return null; }
        var ssidConfig = x.Element(Ns + "SSIDConfig");
        var ssid = ssidConfig?.Element(Ns + "SSID")?.Element(Ns + "name")?.Value ?? x.Element(Ns + "name")?.Value;
        if (string.IsNullOrEmpty(ssid)) return null;
        bool hidden = ssidConfig?.Element(Ns + "nonBroadcast")?.Value == "true";
        var security = x.Element(Ns + "MSM")?.Element(Ns + "security");
        var auth = security?.Element(Ns + "authEncryption")?.Element(Ns + "authentication")?.Value ?? "open";
        var encryption = security?.Element(Ns + "authEncryption")?.Element(Ns + "encryption")?.Value ?? "none";
        var shared = security?.Element(Ns + "sharedKey");
        bool isProtected = shared?.Element(Ns + "protected")?.Value == "true";
        var key = shared != null && !isProtected ? shared.Element(Ns + "keyMaterial")?.Value : null;
        return new WifiNetwork(ssid, key, auth, encryption, isProtected, hidden);
    }

    // Company networks sign in with an account (802.1X), not a password: there's nothing to share.
    public static bool IsEnterprise(WifiNetwork n) =>
        n.Authentication.Equals("WPA2", StringComparison.OrdinalIgnoreCase) || n.Authentication.Equals("WPA", StringComparison.OrdinalIgnoreCase)
        || n.Authentication.Equals("WPA3", StringComparison.OrdinalIgnoreCase) || n.Authentication.Equals("WPA3ENT", StringComparison.OrdinalIgnoreCase)
        || n.Authentication.Equals("WPA3ENT192", StringComparison.OrdinalIgnoreCase);

    public static bool IsOpen(WifiNetwork n) => n.Authentication.Equals("open", StringComparison.OrdinalIgnoreCase) && n.Encryption.Equals("none", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"([\\;,:""])")] private static partial Regex Special();

    // The text phones read: WIFI:T:<WPA|WEP|nopass>;S:<ssid>;P:<password>;H:<true|false>;;
    // with \ ; , : " escaped by a backslash (the ZXing / Android convention, which iOS reads too).
    public static string QrText(WifiNetwork n)
    {
        static string E(string s) => Special().Replace(s, @"\$1");
        bool open = IsOpen(n) || n.Key == null;
        var type = open ? "nopass" : n.Encryption.Equals("WEP", StringComparison.OrdinalIgnoreCase) ? "WEP" : "WPA"; // WPA covers WPA2 and WPA3 personal
        var b = new StringBuilder($"WIFI:T:{type};S:{E(n.Ssid)};");
        if (!open) b.Append($"P:{E(n.Key!)};");
        if (n.Hidden) b.Append("H:true;");
        return b.Append(';').ToString();
    }

    // A QR code drawn in a terminal, two modules per character with half blocks, dark on light
    // whatever the terminal's colours (not every phone reads an inverted code). dark[x, y] is a dark
    // module; the code's own quiet zone must already be in it (ZXing's margin).
    public static List<string> Draw(bool[,] dark)
    {
        int w = dark.GetLength(0), h = dark.GetLength(1);
        const string Dark = "\u001b[30;107m", Reset = "\u001b[0m"; // black on bright white
        var lines = new List<string>();
        for (int y = 0; y < h; y += 2)
        {
            var b = new StringBuilder(Dark);
            for (int x = 0; x < w; x++)
            {
                bool top = dark[x, y], bottom = y + 1 < h && dark[x, y + 1];
                b.Append(top && bottom ? '█' : top ? '▀' : bottom ? '▄' : ' ');
            }
            lines.Add(b.Append(Reset).ToString());
        }
        return lines;
    }
}
