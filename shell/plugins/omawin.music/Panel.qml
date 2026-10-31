import QtQuick
import Quickshell
import qs.Commons
import qs.Commons as Commons
import qs.Ui
import "MusicModel.js" as Model

// The music panel, under the bar's music widget (BarWidget.qml, which owns the player's watch and
// hands itself in as `music`): what's playing with its cover, the controls, your rating, the
// volume, and the playlists, podcasts and radio stations, one tab each. Everything it does is an
// `omawin-music` command; what it shows comes back through the watch.
Panel {
  id: root
  moduleName: "omawin.music"
  ipcTarget: "omawin.music"
  manageIpc: false

  property var music: null          // the bar widget: state, playlists, send()
  property var anchorItem: null
  property var hostWidget: null
  readonly property var barIdentity: hostWidget || root

  readonly property var player: music ? music.player : ({ running: false })
  readonly property bool hasTrack: player.running === true && !!player.title
  property string tab: "music"

  // The tab of what's playing, when the panel opens.
  function open() {
    if (player.running && player.kind) tab = Model.tabOf(player.kind)
    root.controller.show()
  }

  function send(words) { if (music) music.send(words) }

  KeyboardPanel {
    id: panel
    anchorItem: root.anchorItem
    owner: root.barIdentity
    bar: root.bar
    open: root.opened
    centerOnBar: false
    focusTarget: panel.loadedContent
    contentWidth: panel.fittedContentWidth(Style.space(380))
    contentHeight: panel.fittedContentHeight(panel.loadedContentHeight, Style.space(560))

    content: PanelKeyCatcher {
      implicitHeight: column.implicitHeight
      anchors.fill: parent
      onCloseRequested: root.close()
      onActivateRequested: root.send(["toggle"])
      onMoveRequested: function(dx, dy) { if (dx !== 0) root.send([dx > 0 ? "next" : "prev"]) }
      onTabRequested: function(direction) { root.switchPanel(direction) }

      Column {
        id: column
        width: parent.width
        spacing: Style.space(12)

        // ---- What's playing: the cover, the title and where it's from.
        Row {
          width: parent.width
          spacing: Style.space(12)
          visible: root.hasTrack

          SizedImage {
            id: cover
            width: Style.space(64)
            height: Style.space(64)
            url: root.player.thumb || ""
            visible: String(url) !== ""
            crop: true
          }

          Column {
            width: parent.width - (cover.visible ? cover.width + parent.spacing : 0)
            anchors.verticalCenter: parent.verticalCenter
            spacing: Style.space(2)

            Text {
              width: parent.width
              elide: Text.ElideRight
              textFormat: Text.PlainText
              text: root.player.title || ""
              color: Commons.Color.popups.text
              font.family: Style.font.family
              font.pixelSize: Style.font.title
              font.bold: true
            }
            Text {
              width: parent.width
              elide: Text.ElideRight
              textFormat: Text.PlainText
              text: [root.player.artist, root.player.playlistTitle].filter(function(x) { return !!x }).join(" · ")
              color: Commons.Color.popups.text
              opacity: 0.7
              font.family: Style.font.family
              font.pixelSize: Style.font.body
            }
            Text {
              textFormat: Text.PlainText
              visible: !root.player.live && root.player.duration > 0
              text: Model.clock(root.player.position) + " / " + Model.clock(root.player.duration)
              color: Commons.Color.popups.text
              opacity: 0.6
              font.family: Style.font.family
              font.pixelSize: Style.font.caption
            }
            Text {
              textFormat: Text.PlainText
              visible: root.player.live === true
              text: "LIVE"
              color: Commons.Color.accent
              font.family: Style.font.family
              font.pixelSize: Style.font.caption
              font.bold: true
            }
          }
        }

        Text {
          width: parent.width
          visible: !root.hasTrack
          wrapMode: Text.WordWrap
          textFormat: Text.PlainText
          text: root.player.error ? root.player.error : "Nothing playing. Pick a playlist, a podcast or a station below."
          color: Commons.Color.popups.text
          opacity: 0.7
          font.family: Style.font.family
          font.pixelSize: Style.font.body
        }

        // ---- Where it is: seek by dragging (not for a live station).
        PanelSlider {
          width: parent.width
          bar: root.bar
          visible: root.hasTrack && !root.player.live && root.player.duration > 0
          minimum: 0
          maximum: Math.max(1, root.player.duration || 1)
          step: 1
          value: root.player.position || 0
          onReleased: function(value) { root.send(["seek", String(Math.round(value))]) }
        }

        // ---- The controls, the stars and the shuffle.
        Row {
          anchors.horizontalCenter: parent.horizontalCenter
          spacing: Style.space(8)

          PanelActionButton { iconText: "\u{F04AE}"; tooltipText: "Previous"; onClicked: root.send(["prev"]) }
          PanelActionButton {
            iconText: root.player.state === "playing" ? "\u{F03E4}" : "\u{F040A}"
            tooltipText: root.player.state === "playing" ? "Pause" : "Play"
            fontSize: Style.font.heading
            onClicked: root.send(["toggle"])
          }
          PanelActionButton { iconText: "\u{F04AD}"; tooltipText: "Next"; onClicked: root.send(["next"]) }
          PanelActionButton {
            iconText: "\u{F049D}"
            tooltipText: root.player.shuffle ? "Shuffle: on" : "Shuffle: off"
            opacity: root.player.shuffle ? 1 : 0.5
            onClicked: root.send(["shuffle"])
          }
        }

        Row {
          anchors.horizontalCenter: parent.horizontalCenter
          spacing: Style.space(2)
          visible: root.hasTrack && !root.player.live

          Repeater {
            model: 5
            PanelActionButton {
              required property int index
              iconText: index < (root.player.stars || 0) ? "\u{F04CE}" : "\u{F04D2}"   // star / star_outline
              tooltipText: "Rate " + (index + 1)
              onClicked: root.send(["rate", String(index + 1)])
            }
          }
        }

        // ---- The volume (omawin-music's own; Windows' stays where it is).
        Row {
          width: parent.width
          spacing: Style.space(8)
          visible: root.player.running === true

          Text {
            anchors.verticalCenter: parent.verticalCenter
            text: "\u{F057E}"   // nf-md-volume_high
            color: Commons.Color.popups.text
            font.family: Style.font.family
            font.pixelSize: Style.font.icon
          }
          PanelSlider {
            width: parent.width - Style.space(32)
            anchors.verticalCenter: parent.verticalCenter
            bar: root.bar
            minimum: 0
            maximum: 1
            step: 0.05
            value: root.player.volume === undefined ? 1 : root.player.volume
            onReleased: function(value) { root.send(["volume", String(Math.round(value * 100) / 100)]) }
          }
        }

        PanelSeparator { width: parent.width }

        // ---- The library: playlists, podcasts, radio.
        ButtonGroup {
          anchors.horizontalCenter: parent.horizontalCenter
          options: Model.kinds
          value: root.tab
          focusable: false
          onChanged: function(value) { root.tab = value }
        }

        Column {
          width: parent.width
          spacing: Style.space(2)

          Repeater {
            model: Model.ofKind(root.music ? root.music.playlists : [], root.tab)

            Rectangle {
              required property var modelData
              readonly property bool current: root.player.playlist === modelData.slug
              width: parent.width
              height: Style.space(30)
              radius: Style.cornerRadius
              color: current || hover.hovered ? Style.selectedFillFor(Commons.Color.popups.text, Commons.Color.accent) : "transparent"

              Text {
                anchors.left: parent.left
                anchors.leftMargin: Style.space(8)
                anchors.right: count.left
                anchors.rightMargin: Style.space(8)
                anchors.verticalCenter: parent.verticalCenter
                elide: Text.ElideRight
                textFormat: Text.PlainText
                text: modelData.title || modelData.slug
                color: Commons.Color.popups.text
                font.family: Style.font.family
                font.pixelSize: Style.font.body
                font.bold: current
              }
              Text {
                id: count
                anchors.right: parent.right
                anchors.rightMargin: Style.space(8)
                anchors.verticalCenter: parent.verticalCenter
                visible: root.tab !== "radio"
                textFormat: Text.PlainText
                text: String(modelData.count || 0)
                color: Commons.Color.popups.text
                opacity: 0.6
                font.family: Style.font.family
                font.pixelSize: Style.font.caption
              }
              HoverHandler { id: hover; cursorShape: Qt.PointingHandCursor }
              TapHandler { onTapped: root.send(["play", modelData.slug]) }
            }
          }

          Text {
            width: parent.width
            visible: Model.ofKind(root.music ? root.music.playlists : [], root.tab).length === 0
            wrapMode: Text.WordWrap
            textFormat: Text.PlainText
            text: root.tab === "radio" ? "No stations yet: omawin music add radio <url>"
                : root.tab === "podcasts" ? "No podcasts yet: omawin music add podcasts <url>"
                : "No playlists yet: omawin music add <url>"
            color: Commons.Color.popups.text
            opacity: 0.6
            font.family: Style.font.family
            font.pixelSize: Style.font.caption
          }
        }
      }
    }
  }
}
