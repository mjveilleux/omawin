import QtQuick
import qs.Commons

// Omarchy's BarIconButton: a glyph in the bar's icon slot.
WidgetButton {
  property real slotSize: Style.bar.iconSlot
  property string labelText: ""
  fontSize: Style.bar.iconFont
  fixedWidth: slotSize
}
