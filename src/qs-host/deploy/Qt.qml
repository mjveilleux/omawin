// Qt's own QML modules the host ships, beyond what Host.qml and omawin's plugins use: the ones
// community plugins import (Omarchy's marketplace). Never loaded: release.yml's qs-host job hands
// this folder to windeployqt (--qmldir), which copies the modules a file imports, and the plugin
// gallery's builder (src/plugin-gallery) reads these lines as the Qt modules a plugin may import.
// One added here is in the next release and counts in the next gallery.
import QtQml
import QtQml.Models
import QtQml.WorkerScript
import QtQuick
import QtQuick.Window
import QtQuick.Controls
import QtQuick.Layouts
import QtQuick.Shapes
import QtQuick.Effects

QtObject {}
