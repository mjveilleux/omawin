import QtQuick
import qs.Commons
import qs.Commons as Commons

// Omarchy's PanelSeparator: a thin line between a panel's parts.
Rectangle {
  property color foreground: Commons.Color.foreground
  property real strength: 0.12
  implicitHeight: 1
  color: Style.alpha(foreground, strength)
}
