pragma Singleton
import QtQuick

// The panel window that's open, if any (KeyboardPanel sets it): what the host's --shot captures.
QtObject {
  property var current: null
}
