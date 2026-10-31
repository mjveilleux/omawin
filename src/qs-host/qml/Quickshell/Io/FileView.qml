import QtQuick
import Omawin.Native

// FileView { path: ...; text() }: a text file, read when its path is set and again on reload().
QtObject {
  id: root
  property string path: ""
  property bool printErrors: true
  signal loaded()
  signal loadFailed(int error)

  property string _text: ""
  function text() { return _text }
  function reload() {
    if (!path) return
    if (!IoHost.fileExists(path)) { _text = ""; loadFailed(1); return }
    _text = IoHost.readText(path)
    loaded()
  }
  onPathChanged: reload()
  Component.onCompleted: reload()
}
