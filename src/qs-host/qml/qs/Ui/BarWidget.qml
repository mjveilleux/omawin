import QtQuick
import qs.Commons

// Omarchy's BarWidget base: what the bar hands a widget (bar, settings) and its setting() helper.
Item {
  property QtObject bar: null
  property string moduleName: ""
  property var settings: ({})
  function setting(name, fallback) {
    var value = settings ? settings[name] : undefined
    return value === undefined || value === null ? fallback : value
  }
}
