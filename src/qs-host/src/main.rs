// omawin-qs: runs a plugin in Omarchy's format (manifest.json and QML, written for Quickshell) on
// Windows. Quickshell itself is Linux-only (Wayland); this gives a plugin the parts of it plugins
// use: Quickshell.Io (its native part src/io.rs, the rest qml/Quickshell/Io/), the Quickshell singleton, and Omarchy's qs.Commons and qs.Ui
// as omawin draws them (qml/). roadmap 4.9.4, plans/05-plugins.md 10.3.
//
//   omawin-qs --plugin-dir <folder>                  its bar widget, in a small window of its own
//   omawin-qs --plugin-dir <folder> --at x,y,w,h     ... at that place (the bar's slot for it)
//   omawin-qs --plugin-dir <folder> --check          load it, print any QML errors, exit 1 if there were
//   ... --parent <pid>                               end when that process does (the bar that started it)
mod bar;
mod io;

use cxx_qt_lib::{QGuiApplication, QQmlApplicationEngine, QString, QUrl};
use std::path::PathBuf;

// Where the QML beside the program is: <exe>\qml, or OMAWIN_QS_QML (a checkout: src/qs-host/qml).
fn qml_dir() -> PathBuf {
    if let Ok(dir) = std::env::var("OMAWIN_QS_QML") {
        return PathBuf::from(dir);
    }
    let exe = std::env::current_exe().unwrap_or_default();
    let beside = exe.parent().map(|d| d.join("qml")).unwrap_or_default();
    if beside.join("Host.qml").is_file() {
        return beside;
    }
    // Run from the build folder of a checkout: src/qs-host/target/<profile>/omawin-qs.
    exe.ancestors().map(|a| a.join("qml")).find(|d| d.join("Host.qml").is_file()).unwrap_or(beside)
}

// --parent <pid>: the host ends with the bar that started it, even when the bar is killed and
// never gets to stop it. Windows hands a process's end to anyone holding its handle, so a thread
// waits on it (no polling).
fn follow_parent() {
    let args: Vec<String> = std::env::args().collect();
    let Some(pid) = args.iter().position(|a| a == "--parent").and_then(|i| args.get(i + 1)).and_then(|p| p.parse::<u32>().ok()) else {
        return;
    };
    #[cfg(windows)]
    {
        use windows_sys::Win32::Foundation::CloseHandle;
        use windows_sys::Win32::System::Threading::{INFINITE, OpenProcess, PROCESS_SYNCHRONIZE, WaitForSingleObject};
        // SAFETY: OpenProcess takes plain values; a null handle (the bar already gone) is checked.
        let handle = unsafe { OpenProcess(PROCESS_SYNCHRONIZE, 0, pid) };
        if handle.is_null() {
            std::process::exit(0);
        }
        // A raw handle can't cross threads as a pointer; it's only a number to the kernel.
        let handle = handle as usize;
        std::thread::spawn(move || {
            let handle = handle as windows_sys::Win32::Foundation::HANDLE;
            // SAFETY: the handle is ours and valid until closed here.
            unsafe {
                WaitForSingleObject(handle, INFINITE);
                CloseHandle(handle);
            }
            std::process::exit(0);
        });
    }
    #[cfg(not(windows))]
    let _ = pid;
}

fn main() {
    follow_parent();
    let mut app = QGuiApplication::new();
    let mut engine = QQmlApplicationEngine::new();
    let dir = qml_dir();
    if let Some(mut engine) = engine.as_mut() {
        engine.as_mut().add_import_path(&QString::from(dir.to_string_lossy().as_ref()));
        let host = QUrl::from_local_file(&QString::from(dir.join("Host.qml").to_string_lossy().as_ref()));
        engine.load(&host);
    }
    if let Some(app) = app.as_mut() {
        std::process::exit(app.exec());
    }
}
