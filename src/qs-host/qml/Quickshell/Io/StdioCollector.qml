import QtQuick

// StdioCollector { onStreamFinished: ... text ... }: everything a stream said, once it ends.
QtObject {
  property string text: ""
  property bool waitForEnd: true
  signal streamFinished()

  function feed(data) { text += data }
  function finish() { streamFinished() }
}
