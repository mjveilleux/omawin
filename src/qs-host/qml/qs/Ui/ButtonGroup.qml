import QtQuick
import qs.Commons
import qs.Commons as Commons

// Omarchy's ButtonGroup: pick one of a row of options (strings, or { value, label }).
Row {
  id: root
  property var options: []
  property string value: ""
  property color foreground: Commons.Color.foreground
  property color background: Commons.Color.background
  property color accent: Commons.Color.accent
  property string fontFamily: Style.font.family
  property real fontSize: Style.font.body
  property bool focusable: true
  property int cursorIndex: -1
  signal changed(string value)
  signal hovered(int index, bool isHovered)
  spacing: Style.spacing.sm

  Repeater {
    model: root.options
    Rectangle {
      required property var modelData
      required property int index
      readonly property string optionValue: typeof modelData === "string" ? modelData : modelData.value
      readonly property string optionLabel: typeof modelData === "string" ? modelData : (modelData.label || modelData.value)
      readonly property bool selected: optionValue === root.value
      implicitWidth: label.implicitWidth + 20
      implicitHeight: label.implicitHeight + 8
      radius: Style.cornerRadius / 2
      color: selected ? Style.selectedFillFor(root.foreground, root.accent) : (mouse.containsMouse || index === root.cursorIndex ? Style.hoverFillFor(root.foreground) : "transparent")
      Text { id: label; anchors.centerIn: parent; text: parent.optionLabel; color: parent.selected ? root.accent : root.foreground; font.family: root.fontFamily; font.pixelSize: root.fontSize }
      MouseArea {
        id: mouse
        anchors.fill: parent
        hoverEnabled: true
        cursorShape: Qt.PointingHandCursor
        onClicked: if (!parent.selected) { root.value = parent.optionValue; root.changed(parent.optionValue) }
        onContainsMouseChanged: root.hovered(parent.index, containsMouse)
      }
    }
  }
}
