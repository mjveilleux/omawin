// Stands in for Snip.cs in the Native AOT build (build.ps1 -Aot), which can't hold WinForms: no
// screenshot overlay yet; a request for one is logged. Snip.cs moves onto WinUI before the AOT
// build becomes the one installed (docs/native-bar.md).
namespace SystemMenu;

public enum SnipMode { Picture, Text, Color, Qr }

public static class Snip
{
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
    public static void Warm() { }
    public static void Start(SnipMode mode = SnipMode.Picture) => App.Log(new NotSupportedException("screenshots aren't in the AOT build yet"));
    public static void Say(string message) { }
}
