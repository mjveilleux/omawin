import QtQuick
import Omawin.Native

// Quickshell.Io's Process, as plugins use it: command, running (set it to start, clear it to
// stop), stdinEnabled and write(), signal(), stdout / stderr parsers (SplitParser,
// StdioCollector), and the started / exited signals. It's the native ProcessCore (src/io.rs: the
// program runs with no console window of its own on Windows) with Quickshell's properties on top.
ProcessCore {
  id: root

  property var command: []
  property bool running: false
  property string workingDirectory: ""
  property var environment: ({})
  property bool clearEnvironment: false
  property bool stdinEnabled: false
  property QtObject stdout: null
  property QtObject stderr: null

  function startDetached() { IoHost.execDetached(Array.from(root.command).join("\x1f")) }

  property bool _live: false
  // Set once the object is complete: QML can set running before command while it builds one
  // (property order isn't the order written), so nothing starts until then.
  property bool _ready: false
  function _start() {
    _live = root.start(Array.from(root.command).join("\x1f"), root.workingDirectory, root.stdinEnabled)
    if (!_live) { running = false; root.exited(-1, 1) }
  }
  onRunningChanged: {
    if (!_ready) return
    if (running && !_live) _start()
    else if (!running && _live) root.kill()
  }
  Component.onCompleted: { _ready = true; if (running && !_live) _start() }

  onStdoutData: function(data) { if (root.stdout && root.stdout.feed) root.stdout.feed(data) }
  onStderrData: function(data) { if (root.stderr && root.stderr.feed) root.stderr.feed(data) }
  onExited: function(exitCode, exitStatus) {
    if (!root._live) return   // our own report of a start that failed
    if (root.stdout && root.stdout.finish) root.stdout.finish()
    if (root.stderr && root.stderr.finish) root.stderr.finish()
    root._live = false
    root.running = false
  }
}
