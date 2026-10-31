using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using SystemMenu.Search;

namespace SystemMenu;

// One Search's Windows (roadmap 8.5): the selected window live in the preview's picture box,
// drawn there by DWM (WindowThumbnail.cs) above the XAML, so it moves as the window does. One
// picture at a time: it follows the selection, and goes when another kind of row is selected,
// the preview closes or the menu hides (Highlight and HideNow ask). A window with no picture
// (minimized, or gone) keeps its icon.
public sealed partial class MainWindow
{
    WindowThumbnail? windowPicture;
    bool windowPictureWanted; // set by this selection's preview; Highlight hides the picture otherwise
    bool windowPictureHooked;

    void ShowWindowPicture(OpenWindow w)
    {
        windowPictureWanted = true;
        if (windowPicture?.Source != (nint)w.Handle)
        {
            HideWindowPicture();
            windowPicture = WindowThumbnail.Of(hwnd, (nint)w.Handle);
        }
        if (windowPicture == null) return;
        // A taller box than an icon's, in the theme's background, the picture over it.
        PreviewArt.Height = 230;
        PreviewArt.Background = Brush(Hex(theme.Background), 0x90);
        PreviewArt.Child = null;
        if (!windowPictureHooked)
        {
            windowPictureHooked = true;
            PreviewArt.SizeChanged += (_, _) => PlaceWindowPicture();
        }
        // Placed once the box has its new size (SizeChanged places it again if it changes).
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, PlaceWindowPicture);
    }

    // The picture over the box, in the window's pixels; hidden while the box isn't on screen.
    void PlaceWindowPicture()
    {
        if (windowPicture == null) return;
        if (Content?.XamlRoot is not { } root || !appWindow.IsVisible || Preview.Visibility != Visibility.Visible || PreviewArt.ActualWidth <= 0)
        {
            windowPicture.Place(default);
            return;
        }
        var at = PreviewArt.TransformToVisual(null).TransformBounds(new Windows.Foundation.Rect(0, 0, PreviewArt.ActualWidth, PreviewArt.ActualHeight));
        // (check) DWM's destination box is in the window's client pixels, which is where the XAML is.
        windowPicture.Place(WindowPicture.ToPixels(at.X, at.Y, at.Width, at.Height, root.RasterizationScale, inset: 6));
    }

    void HideWindowPicture()
    {
        windowPicture?.Dispose();
        windowPicture = null;
    }
}
