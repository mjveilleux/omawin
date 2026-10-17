# Screenshots and text from the screen

## Screenshot (Win+Shift+S)

The screen freezes, dimmed, about 0.2 s after the key. Then:

- **drag** a region (its size shows as you drag)
- **click** a window to take just that window
- **Enter** for everything
- **Esc** or right-click to cancel
- **T** switches to text (below)

The picture goes to the clipboard and is saved in `Pictures\Screenshots`; a small note says so. Windows' Snipping Tool doesn't open: GlazeWM catches the key first, and the overlay is part of the always-running menu app, so nothing has to start up.

## Text from the screen (Win+Shift+T)

Like Omarchy's "capture text": drag over any text (an image, a video, a dialog you can't select in) and the text is copied. A note says how many words and lines.

It uses Windows' own text recognition: offline, nothing to install, in the languages Windows has. Small regions are enlarged before reading. It reads normal text well; with code it can misread symbols (`|` as `I`).

Also: **T** inside the screenshot overlay, **System menu › Trigger › Text**, or `omawin capture text`.

## Colour picker

`omawin capture color`, or **System menu › Trigger › Colour picker**. The screen freezes (not dimmed, so the colours are true) and a loupe follows the mouse: the pixels around it, enlarged, the middle one ringed, and its hex. **Click** (or Enter) copies it as `#rrggbb`; the note shows the colour. The **arrow keys** move one pixel at a time; Esc cancels.

## QR codes

`omawin capture qr`, or **System menu › Trigger › Read a QR code**. Pick the code like a screenshot: drag over it, click a window, or Enter for everything. Its text is copied and shown in the note; a link is never opened. Several codes are copied a line each; if there's none, the note says so. Reading is offline (ZXing.Net, inside the menu app).
