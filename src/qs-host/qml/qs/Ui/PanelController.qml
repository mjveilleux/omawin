import QtQuick

// Omarchy's PanelController: whether a panel is open.
QtObject {
  property bool open: false
  function show() { open = true }
  function hide() { open = false }
}
