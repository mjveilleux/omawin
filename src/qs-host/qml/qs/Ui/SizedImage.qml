import QtQuick

// Omarchy's SizedImage: a picture from a URL or path, filling its box (crop) or fitted into it.
Item {
  id: root
  property url url: ""
  property bool crop: false
  property bool asynchronous: true
  readonly property alias status: image.status
  Image {
    id: image
    anchors.fill: parent
    source: root.url
    asynchronous: root.asynchronous
    fillMode: root.crop ? Image.PreserveAspectCrop : Image.PreserveAspectFit
    sourceSize.width: width * Screen.devicePixelRatio
    sourceSize.height: height * Screen.devicePixelRatio
    smooth: true
    clip: true
  }
}
