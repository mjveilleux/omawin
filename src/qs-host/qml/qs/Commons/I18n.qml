pragma Singleton
import QtQuick

// Omarchy's translations (qs.Commons I18n): omawin is English, so a key reads as itself.
QtObject {
  function t(key) { return key }
}
