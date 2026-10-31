import QtQuick
import qs.Commons
import qs.Commons as Commons

// Omarchy's Panel base: a bar widget's drop-down, its open state in a PanelController.
Item {
  id: root
  property QtObject bar: null
  property string moduleName: ""
  property var settings: ({})
  property string ipcTarget: ""
  property bool manageIpc: true
  property alias controller: panelController
  property bool popoutSwitching: false
  property bool popoutSwitchClosing: false
  readonly property bool opened: panelController.open
  readonly property color barForeground: bar ? bar.barForeground : Commons.Color.foreground

  function open() { panelController.show() }
  function close() { panelController.hide() }
  function closeForPopoutSwitch() { popoutSwitchClosing = true; close(); Qt.callLater(function() { popoutSwitchClosing = false }) }
  function toggle() { opened ? close() : open() }
  function switchPanel(direction) { return false }
  function setting(name, fallback) {
    var value = settings ? settings[name] : undefined
    return value === undefined || value === null ? fallback : value
  }

  PanelController { id: panelController }
}
