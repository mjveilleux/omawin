import QtQuick
import qs.Commons
import qs.Commons as Commons

// Omarchy's PanelSlider: a value between minimum and maximum, dragged or clicked; moved while
// dragging, released at the end.
Item {
  id: root
  property QtObject bar: null
  property real value: 0
  property real minimum: 0
  property real maximum: 1
  property real step: 0.05
  property bool integer: false
  property color trackColor: Style.alpha(fillColor, 0.2)
  property color fillColor: Commons.Color.accent
  property color knobColor: Commons.Color.foreground
  property bool dragging: false
  property real liveValue: value
  signal moved(real value)
  signal released(real value)
  signal rightClicked()

  implicitHeight: Style.spacing.controlHeight / 2
  onValueChanged: if (!dragging) liveValue = value

  readonly property real fraction: maximum > minimum ? (liveValue - minimum) / (maximum - minimum) : 0
  function valueAt(x) {
    var v = minimum + Math.max(0, Math.min(1, x / width)) * (maximum - minimum)
    v = Math.round(v / step) * step
    return integer ? Math.round(v) : v
  }

  Rectangle { anchors.verticalCenter: parent.verticalCenter; width: parent.width; height: 4; radius: 2; color: root.trackColor }
  Rectangle { anchors.verticalCenter: parent.verticalCenter; width: parent.width * root.fraction; height: 4; radius: 2; color: root.fillColor }
  Rectangle {
    anchors.verticalCenter: parent.verticalCenter
    x: parent.width * root.fraction - width / 2
    width: 12; height: 12; radius: 6
    color: root.knobColor
  }
  MouseArea {
    anchors.fill: parent
    acceptedButtons: Qt.LeftButton | Qt.RightButton
    onPressed: function(m) {
      if (m.button === Qt.RightButton) { root.rightClicked(); return }
      root.dragging = true; root.liveValue = root.valueAt(m.x); root.moved(root.liveValue)
    }
    onPositionChanged: function(m) { if (root.dragging) { root.liveValue = root.valueAt(m.x); root.moved(root.liveValue) } }
    onReleased: function(m) { if (root.dragging) { root.dragging = false; root.released(root.liveValue) } }
  }
}
