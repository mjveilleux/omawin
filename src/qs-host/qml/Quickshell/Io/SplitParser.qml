import QtQuick

// SplitParser { onRead: data => ... }: a stream cut at splitMarker (a newline by default), one
// read per piece; a last piece with no marker after it comes when the stream ends.
QtObject {
  property string splitMarker: "\n"
  signal read(string data)

  property string _buffer: ""
  function feed(data) {
    var text = _buffer + data
    var at
    while ((at = text.indexOf(splitMarker)) >= 0) {
      var piece = text.substring(0, at)
      if (splitMarker === "\n" && piece.endsWith("\r")) piece = piece.substring(0, piece.length - 1)
      read(piece)
      text = text.substring(at + splitMarker.length)
    }
    _buffer = text
  }
  function finish() {
    if (_buffer.length > 0) { var rest = _buffer; _buffer = ""; read(rest) }
  }
}
