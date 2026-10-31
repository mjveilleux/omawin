pragma Singleton
import QtQuick

// Omarchy's structural tokens (qs.Commons Style): spacing, the type scale, the bar's sizes, motion.
// The same numbers as Omarchy's defaults, in omawin's font.
QtObject {
  id: root
  readonly property string fontFamily: "JetBrainsMono NF"
  readonly property int fontBaseSize: 12
  readonly property real fontScale: 1
  property int cornerRadius: 8
  readonly property int gapsOut: 8
  readonly property bool reduceMotion: false
  readonly property var easeOut: [0.23, 1, 0.32, 1, 1, 1]

  function space(px) { return Math.round(px) }
  function duration(ms) { return reduceMotion ? 0 : ms }
  function alpha(c, a) { return Qt.rgba(c.r, c.g, c.b, a) }
  function selectedFillFor(foreground, accent, urgent) { return alpha(accent || foreground, 0.18) }
  function hoverFillFor(foreground) { return alpha(foreground, 0.08) }

  readonly property QtObject spacing: QtObject {
    readonly property int xs: 2
    readonly property int sm: 4
    readonly property int md: 8
    readonly property int lg: 12
    readonly property int popupPadding: 12
    readonly property int controlHeight: 32
  }
  readonly property QtObject font: QtObject {
    readonly property string family: root.fontFamily
    readonly property int baseSize: root.fontBaseSize
    readonly property int caption: 10
    readonly property int bodySmall: 11
    readonly property int body: 12
    readonly property int subtitle: 13
    readonly property int title: 14
    readonly property int heading: 16
    readonly property int display: 24
    readonly property int iconSmall: 11
    readonly property int icon: 14
    readonly property int iconLarge: 18
  }
  readonly property QtObject bar: QtObject {
    readonly property int sizeHorizontal: 26
    readonly property int sizeVertical: 26
    readonly property int iconSlot: 27
    readonly property int statusSlot: 27
    readonly property int iconCanvas: 16
    readonly property int iconFont: 13
    readonly property int iconSize: 12
  }
}
