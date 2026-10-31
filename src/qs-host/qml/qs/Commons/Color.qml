pragma Singleton
import QtQuick
import Quickshell
import Omawin.Native

// Omarchy's palette roles (qs.Commons Color), from omawin's current theme: its name in
// %LOCALAPPDATA%\launcher\theme, its palette in launcher\themes\<name>.json (installed themes) or
// the checkout's themes\<name>.json. Read again when the theme changes (the file is watched by
// polling it every few seconds: cheap, and a theme switch shows within that).
QtObject {
  id: root
  readonly property string state: (Quickshell.env("LOCALAPPDATA") || (Quickshell.env("HOME") + "/.local/share")) + "/launcher"
  readonly property string repo: Quickshell.env("DOTFILES") || ((Quickshell.env("USERPROFILE") || Quickshell.env("HOME")) + "/omawin")
  property var palette: ({})

  function load() {
    var name = (IoHost.readText(state + "/theme") || "starship").trim()
    for (var path of [state + "/themes/" + name + ".json", repo + "/themes/" + name + ".json"]) {
      if (!IoHost.fileExists(path)) continue
      try { palette = JSON.parse(IoHost.readText(path)); return } catch (e) { }
    }
  }
  function pick(key, fallback) { return palette[key] || fallback }

  readonly property color background: pick("background", "#1a1b26")
  readonly property color foreground: pick("foreground", "#c0caf5")
  readonly property color accent: pick("accent", "#7aa2f7")
  readonly property color urgent: pick("red", "#f7768e")
  readonly property color surface: pick("surface", pick("background", "#1a1b26"))
  readonly property color muted: pick("muted", pick("comment", "#565f89"))

  readonly property QtObject bar: QtObject {
    readonly property color background: root.background
    readonly property color foreground: root.foreground
  }
  readonly property QtObject popups: QtObject {
    readonly property color background: root.surface
    readonly property color text: root.foreground
    readonly property color border: root.pick("border", root.accent)
  }
  readonly property QtObject tooltip: popups
  readonly property QtObject menu: popups

  Component.onCompleted: load()
  property Timer _watch: Timer { interval: 3000; running: true; repeat: true; onTriggered: root.load() }
}
