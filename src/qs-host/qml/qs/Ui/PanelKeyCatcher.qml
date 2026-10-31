import QtQuick

// Omarchy's PanelKeyCatcher: a panel's keys as meanings: Escape closes, Enter activates, the
// arrows and h j k l move, Tab goes to the next panel.
FocusScope {
  id: root
  property bool blocked: false
  property bool reorderable: false
  signal moveRequested(int dx, int dy)
  signal reorderRequested(int dy)
  signal activateRequested()
  signal returnRequested()
  signal closeRequested()
  signal deleteRequested()
  signal tabRequested(int direction)
  focus: true

  Keys.onPressed: function(e) {
    if (root.blocked) return
    var k = e.key
    if (k === Qt.Key_Escape) root.closeRequested()
    else if (k === Qt.Key_Return || k === Qt.Key_Enter) { root.activateRequested(); root.returnRequested() }
    else if (k === Qt.Key_Space) root.activateRequested()
    else if (k === Qt.Key_Left || k === Qt.Key_H) root.moveRequested(-1, 0)
    else if (k === Qt.Key_Right || k === Qt.Key_L) root.moveRequested(1, 0)
    else if (k === Qt.Key_Up || k === Qt.Key_K) root.moveRequested(0, -1)
    else if (k === Qt.Key_Down || k === Qt.Key_J) root.moveRequested(0, 1)
    else if (k === Qt.Key_Tab) root.tabRequested(1)
    else if (k === Qt.Key_Backtab) root.tabRequested(-1)
    else if (k === Qt.Key_Delete) root.deleteRequested()
    else return
    e.accepted = true
  }
}
