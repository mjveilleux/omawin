import QtQuick
import qs.Commons
import qs.Commons as Commons

// Omarchy's WidgetButton: a bar cell with text, its colours from the bar, pressed(button).
Item {
  id: root
  property var bar: null
  property string text: ""
  property string tooltipText: ""
  property string fontFamily: bar ? bar.fontFamily : Style.font.family
  property real fontSize: Style.font.body
  property color foreground: bar ? bar.barForeground : Commons.Color.foreground
  property bool active: false
  property real fixedWidth: -1
  signal pressed(int button)

  implicitWidth: fixedWidth > 0 ? fixedWidth : label.implicitWidth + 16
  implicitHeight: Style.bar.sizeHorizontal

  Rectangle {
    anchors.fill: parent
    radius: Style.cornerRadius / 2
    color: mouse.containsMouse ? Style.hoverFillFor(root.foreground) : "transparent"
  }
  Text {
    id: label
    anchors.centerIn: parent
    text: root.text
    color: root.foreground
    font.family: root.fontFamily
    font.pixelSize: root.fontSize
  }
  MouseArea {
    id: mouse
    anchors.fill: parent
    hoverEnabled: true
    acceptedButtons: Qt.LeftButton | Qt.RightButton | Qt.MiddleButton
    cursorShape: Qt.PointingHandCursor
    onPressed: function(m) { root.pressed(m.button) }
  }
}
