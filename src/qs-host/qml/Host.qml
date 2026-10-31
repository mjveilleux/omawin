import QtQuick
import QtQuick.Window
import Quickshell
import Quickshell.Io
import Omawin.Native
import qs.Commons
import qs.Commons as Commons
import qs.Ui

// One plugin, the way Omarchy's shell hosts it (shell/services/PluginRegistry.qml): its
// manifest read, its bar widget loaded with what the bar hands a widget (bar, settings: the
// manifest's defaults), in a window of its own: borderless, on top, the bar's height, at
// --at x,y,w,h (the cell omawin's bar keeps for it) or else the top right of the main screen.
// omawin's bar starts one host per plugin per monitor; each draws its plugin once.
//
//   --plugin-dir <folder>   the plugin (its manifest.json and QML)
//   --at x,y,w,h        where its cell is, in screen pixels
//   --check             load it, say whether it loaded, and quit: 0 when it did (CI and omawin
//                       check use this; QML's own errors are printed above it)
//   --shot <png>        a picture of the widget a moment after it loads (with --open, of its
//                       panel too, as <png> with -panel before .png), then quit
Window {
  id: host

  function arg(name) {
    var a = Qt.application.arguments
    var i = a.indexOf(name)
    return i >= 0 && i + 1 < a.length ? a[i + 1] : ""
  }
  readonly property bool checking: Qt.application.arguments.indexOf("--check") >= 0
  readonly property string pluginDir: IoHost.absolutePath(arg("--plugin-dir")).replace(/\/$/, "")
  readonly property var at: arg("--at").split(",").map(Number)
  property var manifest: ({})
  readonly property string shot: arg("--shot")
  readonly property bool openPanel: Qt.application.arguments.indexOf("--open") >= 0

  function fail(why) {
    console.error("omawin-qs: " + why)
    if (checking) Qt.exit(1)
  }

  // What a widget's `bar` is in Omarchy: the bar's colours, font and size, and run().
  readonly property QtObject bar: QtObject {
    readonly property color barForeground: Commons.Color.bar.foreground
    readonly property color barBackground: Commons.Color.bar.background
    readonly property color foreground: Commons.Color.foreground
    readonly property color background: Commons.Color.background
    readonly property color urgent: Commons.Color.urgent
    readonly property string fontFamily: Style.font.family
    readonly property int barSize: Style.bar.sizeHorizontal
    readonly property string position: "top"
    // omawin's own: whether this bar is on the monitor GlazeWM has focused (the bar says, over
    // stdin: src/bar.rs), so a key that opens a panel opens the one on that monitor.
    readonly property bool screenFocused: BarLink.focused
    function barSizeFor(screenName) { return barSize }
    function run(command) { Quickshell.execDetached(["cmd", "/c", command]) }
    function switchPanelFrom(widget, direction) { return false }
    function setCenterHoverRevealSuppressed(value) { }
  }

  flags: Qt.Tool | Qt.FramelessWindowHint | Qt.WindowStaysOnTopHint
  color: Commons.Color.bar.background

  // --at is in the screen's pixels (what omawin's bar knows); Qt places windows in its own units.
  // Each screen keeps its top left corner in pixels, and everything inside it is pixels divided
  // by that screen's scale (150% is 1.5), so a cell on a second monitor is mapped from that
  // monitor's corner with that monitor's scale: dividing the whole position by one scale would
  // put it on the wrong screen whenever the monitors' scales differ, or one isn't at 0,0.
  readonly property bool placed: at.length === 4 && at[2] > 0 && at[3] > 0
  readonly property var target: placed ? screenAt(at[0], at[1]) : null
  function screenAt(px, py) {
    var screens = Qt.application.screens
    for (var i = 0; i < screens.length; i++) {
      var s = screens[i]
      var r = s.devicePixelRatio > 0 ? s.devicePixelRatio : 1
      if (px >= s.virtualX && px < s.virtualX + s.width * r && py >= s.virtualY && py < s.virtualY + s.height * r) return s
    }
    return screens.length > 0 ? screens[0] : null
  }
  readonly property real ratio: target && target.devicePixelRatio > 0 ? target.devicePixelRatio : (Screen.devicePixelRatio > 0 ? Screen.devicePixelRatio : 1)
  screen: target ? target : Qt.application.screens[0]
  width: placed ? at[2] / ratio : Math.max(40, widget.item ? widget.item.implicitWidth : 40)
  height: placed ? at[3] / ratio : Style.bar.sizeHorizontal
  x: placed ? target.virtualX + (at[0] - target.virtualX) / ratio : Screen.virtualX + Screen.width - width - 200
  y: placed ? target.virtualY + (at[1] - target.virtualY) / ratio : Screen.virtualY
  visible: !checking

  Component.onCompleted: {
    if (!pluginDir) return fail("--plugin-dir <folder> is needed")
    var text = IoHost.readText(pluginDir + "/manifest.json")
    if (!text) return fail("no manifest.json in " + pluginDir)
    try { manifest = JSON.parse(text) } catch (e) { return fail("manifest.json: " + e) }
    var entry = manifest.entryPoints && manifest.entryPoints.barWidget
    if (!entry) return fail(manifest.id + " has no bar widget (entryPoints.barWidget)")
    widget.setSource("file:///" + (pluginDir + "/" + entry).replace(/^\/+/, ""), {})
  }

  Rectangle { anchors.fill: parent; color: Commons.Color.bar.background }
  Loader {
    id: widget
    anchors.fill: parent
    onStatusChanged: {
      if (status === Loader.Error) return host.fail("couldn't load " + host.manifest.id + "'s bar widget (the errors above)")
      if (status !== Loader.Ready) return
      item.bar = host.bar
      item.settings = (host.manifest.barWidget && host.manifest.barWidget.defaults) || {}
      if (host.checking) { console.log("omawin-qs: " + host.manifest.id + " loaded"); Qt.callLater(function() { Qt.exit(0) }) }
      if (host.shot) shooter.start()
    }
  }

  // --shot: the widget, then (--open) its panel, each saved once it has had a moment to fill in.
  Timer {
    id: shooter
    interval: 1500
    property int step: 0
    onTriggered: {
      if (step === 0) {
        host.contentItem.grabToImage(function(r) { r.saveToFile(host.shot); console.log("omawin-qs: saved " + host.shot) })
        if (!host.openPanel || !widget.item.open) { Qt.callLater(function() { Qt.exit(0) }); return }
        widget.item.open()
        step = 1
        start()
      } else {
        var panel = OpenPanels.current
        var path = host.shot.replace(/\.png$/, "-panel.png")
        if (!panel) { console.error("omawin-qs: no panel opened"); Qt.exit(1); return }
        panel.contentItem.grabToImage(function(r) { r.saveToFile(path); console.log("omawin-qs: saved " + path); Qt.exit(0) })
      }
    }
  }
}
