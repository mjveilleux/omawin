// The native part of the QML modules this host gives plugins (src/io.rs: running programs, reading
// files; src/bar.rs: what the bar tells the host), as the module Omawin.Native, compiled into the
// program. Quickshell, Quickshell.Io, qs.Commons, qs.Ui and Host.qml are plain QML in qml/, shipped
// next to the program; Quickshell.Io's Process, SplitParser, StdioCollector and FileView are QML on
// top of Omawin.Native.
use cxx_qt_build::{CxxQtBuilder, QmlModule};

fn main() {
    CxxQtBuilder::new_qml_module(QmlModule::new("Omawin.Native")).file("src/io.rs").file("src/bar.rs").build();
}
