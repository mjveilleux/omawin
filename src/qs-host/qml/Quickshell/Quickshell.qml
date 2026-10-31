pragma Singleton
import QtQuick
import Omawin.Native

// Quickshell's singleton, the parts plugins use: start a program and let it go, the environment.
QtObject {
  function execDetached(command) { return IoHost.execDetached(Array.from(command).join("\x1f")) }
  function env(name) { return IoHost.env(name) }
  readonly property string shellDir: ""
}
