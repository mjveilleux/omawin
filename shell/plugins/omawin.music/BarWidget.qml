import QtQuick
import Quickshell
import Quickshell.Io
import qs.Commons
import qs.Commons as Commons
import qs.Ui
import "MusicModel.js" as Model

// The music player in the bar: a glyph for what it's doing and, while something plays, its title.
// A click opens the panel (Panel.qml); right click plays or pauses; the wheel skips.
//
// It reads `omawin-music watch` (src/music/Watch.cs), which never starts the player: it waits for
// one, passes on its state, the playlists, the speakers' level and Win+M. Each bar (one a monitor)
// runs its own watch, and tells it over stdin whether its panel is open, so Win+M
// (`omawin-music open`) closes an open panel, or opens one.
BarWidget {
  id: root
  moduleName: "omawin.music"

  property var player: ({ running: false })
  property var playlists: []
  property real level: 0

  readonly property bool showTitle: setting("showTitle", true)
  readonly property int titleWidth: setting("titleWidth", 180)
  readonly property string label: Model.barText(root.player)

  // A player command (`omawin-music <words>`): it starts the player when it isn't running, except
  // for the ones that only make sense while it plays.
  function send(words) {
    Quickshell.execDetached(["omawin-music"].concat(words))
  }

  function injectPanel() {
    var target = panelLoader.item
    if (!target) return
    target.bar = root.bar
    target.anchorItem = button
    target.hostWidget = root
  }

  // The bar's contract for summon / hide / toggle (and Tab between panels).
  readonly property bool opened: panelLoader.item ? panelLoader.item.opened === true : false
  readonly property bool popoutSwitchClosing: panelLoader.item ? panelLoader.item.popoutSwitchClosing === true : false
  function open() { if (panelLoader.item) panelLoader.item.open() }
  function close() { if (panelLoader.item) panelLoader.item.close() }
  function toggle() { if (panelLoader.item) panelLoader.item.toggle() }
  function closeForPopoutSwitch() { if (panelLoader.item) panelLoader.item.closeForPopoutSwitch() }

  onOpenedChanged: if (watch.running) watch.write("panel " + (opened ? "1" : "0") + "\n")
  onBarChanged: injectPanel()

  implicitWidth: button.implicitWidth + (title.visible ? title.width + Style.space(4) : 0)
  implicitHeight: button.implicitHeight

  Process {
    id: watch
    command: ["omawin-music", "watch"]
    running: true
    stdinEnabled: true
    stdout: SplitParser {
      onRead: function(line) {
        var m = Model.parseLine(line)
        if (!m) return
        if (m.type === "state") root.player = m.value
        else if (m.type === "playlists") root.playlists = m.value
        else if (m.type === "level") root.level = m.value
        else if (m.type === "open") { if (m.value) root.open(); else root.close() }
      }
    }
    // This bar answers Win+M; with several monitors, `omawin-music open` picks the bar that says
    // it's on the focused one, and else the first.
    onStarted: watch.write("focus 1\n")
    // It ends when the player can't be found at all (not installed): tried again a while later.
    onExited: retry.start()
  }

  Timer {
    id: retry
    interval: 30000
    onTriggered: watch.running = true
  }

  Loader {
    id: panelLoader
    active: true
    source: Qt.resolvedUrl("Panel.qml")
    visible: false
    onLoaded: {
      item.music = root
      root.injectPanel()
      Qt.callLater(root.injectPanel)
    }
  }

  // Right click plays or pauses, middle click and the wheel skip; a click opens the panel.
  function pressed(b) {
    if (b === Qt.RightButton) root.send(["toggle"])
    else if (b === Qt.MiddleButton) root.send(["next"])
    else root.toggle()
  }

  BarIconButton {
    id: button
    anchors.left: parent.left
    anchors.verticalCenter: parent.verticalCenter
    bar: root.bar
    text: Model.glyph(root.player)
    tooltipText: root.showTitle || !root.label ? "" : root.label
    onPressed: function(b) { root.pressed(b) }
  }

  Text {
    id: title
    visible: root.showTitle && root.label !== ""
    anchors.left: button.right
    anchors.leftMargin: Style.space(4)
    anchors.verticalCenter: parent.verticalCenter
    width: Math.min(implicitWidth, root.titleWidth)
    elide: Text.ElideRight
    textFormat: Text.PlainText
    text: root.label
    color: root.bar ? root.bar.barForeground : Commons.Color.foreground
    font.family: root.bar ? root.bar.fontFamily : Style.font.family
    font.pixelSize: Style.font.body

    MouseArea {
      anchors.fill: parent
      acceptedButtons: Qt.LeftButton | Qt.RightButton | Qt.MiddleButton
      cursorShape: Qt.PointingHandCursor
      onPressed: function(mouse) { root.pressed(mouse.button) }
    }
  }

  WheelHandler {
    onWheel: function(event) { root.send([event.angleDelta.y < 0 ? "next" : "prev"]) }
  }
}
