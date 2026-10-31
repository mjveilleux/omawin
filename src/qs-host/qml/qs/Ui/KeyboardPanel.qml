import QtQuick
import QtQuick.Window
import qs.Commons
import qs.Commons as Commons

// Omarchy's KeyboardPanel: the drop-down's window, under the widget that opened it, with the
// panel's content; Escape closes it, and so does clicking elsewhere (it loses focus).
Window {
  id: root
  required property Item anchorItem
  required property QtObject bar
  property var owner: null
  property bool open: false
  property bool centerOnBar: false
  property var focusTarget: null
  property int padding: Style.spacing.popupPadding
  property int contentWidth: Style.space(280)
  property int contentHeight: Style.space(200)
  property Component content
  readonly property Item loadedContent: loader.item
  readonly property real loadedContentHeight: loader.item ? loader.item.implicitHeight : 0

  function fittedContentWidth(width, cap) { return Math.round(cap ? Math.min(width, cap) : width) }
  function fittedContentHeight(height, cap) { var h = Math.max(1, height); return Math.round(cap ? Math.min(h, cap) : h) }

  flags: Qt.Tool | Qt.FramelessWindowHint | Qt.WindowStaysOnTopHint
  color: Commons.Color.popups.background
  width: contentWidth + padding * 2
  height: contentHeight + padding * 2
  visible: open

  // Under the widget, on the widget's own monitor (each monitor's bar has its own host): the
  // panel takes the bar window's screen first, so its position is read in that screen's units.
  function place() {
    if (!anchorItem || !anchorItem.Window.window) return
    var screen = anchorItem.Window.window.screen
    if (screen && root.screen !== screen) root.screen = screen
    var p = anchorItem.mapToGlobal(0, anchorItem.height)
    var right = screen ? screen.virtualX + screen.width : p.x + width
    x = Math.max(screen ? screen.virtualX : 0, Math.min(p.x, right - width - Style.gapsOut))
    y = p.y + Style.gapsOut
  }
  onOpenChanged: {
    if (open) { place(); requestActivate(); OpenPanels.current = root }
    else if (OpenPanels.current === root) OpenPanels.current = null
  }
  onActiveChanged: if (!active && open && owner && owner.close) owner.close()

  Rectangle {
    anchors.fill: parent
    color: Commons.Color.popups.background
    border.color: Commons.Color.popups.border
    border.width: 1
    radius: Style.cornerRadius
  }
  Loader {
    id: loader
    x: root.padding
    y: root.padding
    width: root.contentWidth
    height: root.contentHeight
    sourceComponent: root.content
    focus: true
  }
}
