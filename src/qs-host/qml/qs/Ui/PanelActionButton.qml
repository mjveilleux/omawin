import QtQuick
import qs.Commons
import qs.Commons as Commons

// Omarchy's PanelActionButton: a glyph that does something when clicked.
Item {
  id: root
  property string iconText: ""
  property string tooltipText: ""
  property color foreground: Commons.Color.popups.text
  property color hoverColor: foreground
  property string fontFamily: Style.font.family
  property real fontSize: Style.font.icon
  property real size: Math.round(Math.max(Style.space(22), fontSize + Style.spacing.sm * 2))
  property bool focusable: false
  property bool hasCursor: false
  property bool bordered: false
  signal clicked()
  signal hovered(bool isHovered)

  implicitWidth: size
  implicitHeight: size

  Rectangle {
    anchors.fill: parent
    radius: Style.cornerRadius / 2
    color: mouse.containsMouse || root.hasCursor ? Style.hoverFillFor(root.foreground) : "transparent"
    border.width: root.bordered ? 1 : 0
    border.color: Style.alpha(root.foreground, 0.3)
  }
  Text {
    anchors.centerIn: parent
    text: root.iconText
    color: mouse.containsMouse ? root.hoverColor : root.foreground
    font.family: root.fontFamily
    font.pixelSize: root.fontSize
  }
  MouseArea {
    id: mouse
    anchors.fill: parent
    hoverEnabled: true
    cursorShape: Qt.PointingHandCursor
    onClicked: root.clicked()
    onContainsMouseChanged: root.hovered(containsMouse)
  }
}
