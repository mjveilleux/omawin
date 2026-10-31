// Omawin.Native: the native part of the Quickshell.Io module this host gives plugins: running programs and
// reading files. Quickshell's own types (Process, SplitParser, StdioCollector, FileView) are QML
// on top of these (qml/Quickshell/Io/), so the Rust side stays small: a process with its output
// passed on line by line as it comes, and a few helpers.

#[cxx_qt::bridge]
pub mod qobject {
    unsafe extern "C++" {
        include!("cxx-qt-lib/qstring.h");
        type QString = cxx_qt_lib::QString;
    }

    unsafe extern "RustQt" {
        // One running program: start it, write to its stdin, stop it. What it prints comes back
        // as stdoutData / stderrData (as it arrives, not line by line: the QML parsers split it),
        // and its end as exited.
        #[qobject]
        #[qml_element]
        type ProcessCore = super::ProcessCoreRust;

        #[qinvokable]
        // The command comes as one string, its words joined by \u{1f} (QML's join("\x1f")): a
        // JavaScript array doesn't reliably become a QStringList on the way in (Qt 6.4 hands over an
        // empty one), and that character never appears in a command line. (Not `stdin`: MSVC's
        // C headers make that a macro, which breaks the generated C++.)
        fn start(self: Pin<&mut ProcessCore>, command: &QString, cwd: &QString, with_stdin: bool) -> bool;
        #[qinvokable]
        fn write(self: Pin<&mut ProcessCore>, data: &QString);
        #[qinvokable]
        fn kill(self: Pin<&mut ProcessCore>);
        // Quickshell's Process.signal(sig): plugins send 15 (SIGTERM) or 9 (SIGKILL) to stop a
        // program; on Windows either one ends it.
        #[qinvokable]
        fn signal(self: Pin<&mut ProcessCore>, sig: i32);
        #[qinvokable]
        #[cxx_name = "processId"]
        fn process_id(self: &ProcessCore) -> i64;

        #[qsignal]
        fn started(self: Pin<&mut ProcessCore>);
        #[qsignal]
        #[cxx_name = "stdoutData"]
        fn stdout_data(self: Pin<&mut ProcessCore>, data: QString);
        #[qsignal]
        #[cxx_name = "stderrData"]
        fn stderr_data(self: Pin<&mut ProcessCore>, data: QString);
        #[qsignal]
        #[cxx_name = "exited"]
        fn exited(self: Pin<&mut ProcessCore>, exit_code: i32, exit_status: i32);
    }

    unsafe extern "RustQt" {
        // What Quickshell's own singleton and FileView need: a program started and let go, the
        // environment, a text file read.
        #[qobject]
        #[qml_element]
        #[qml_singleton]
        type IoHost = super::IoHostRust;

        #[qinvokable]
        #[cxx_name = "execDetached"]
        fn exec_detached(self: &IoHost, command: &QString) -> bool;
        #[qinvokable]
        fn env(self: &IoHost, name: &QString) -> QString;
        #[qinvokable]
        #[cxx_name = "readText"]
        fn read_text(self: &IoHost, path: &QString) -> QString;
        #[qinvokable]
        #[cxx_name = "fileExists"]
        fn file_exists(self: &IoHost, path: &QString) -> bool;
        #[qinvokable]
        #[cxx_name = "absolutePath"]
        fn absolute_path(self: &IoHost, path: &QString) -> QString;
    }

    impl cxx_qt::Threading for ProcessCore {}
}

use core::pin::Pin;
use cxx_qt::{CxxQtType, Threading};
use cxx_qt_lib::QString;
use std::io::{Read, Write};
use std::process::{Child, ChildStdin, Command, Stdio};
use std::sync::{Arc, Mutex};

#[derive(Default)]
pub struct ProcessCoreRust {
    child: Arc<Mutex<Option<Child>>>,
    stdin: Option<ChildStdin>,
    pid: i64,
}

#[derive(Default)]
pub struct IoHostRust;

// A command's words, from QML's join("\x1f").
fn words(command: &QString) -> Vec<String> {
    command.to_string().split('\u{1f}').filter(|w| !w.is_empty()).map(String::from).collect()
}

// A program as plugins name it: on Windows, without a console window of its own.
fn command(argv: &[String]) -> Command {
    let mut c = Command::new(&argv[0]);
    c.args(&argv[1..]);
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        const CREATE_NO_WINDOW: u32 = 0x0800_0000;
        c.creation_flags(CREATE_NO_WINDOW);
    }
    c
}

impl qobject::ProcessCore {
    fn start(mut self: Pin<&mut Self>, command_line: &QString, cwd: &QString, with_stdin: bool) -> bool {
        let argv = words(command_line);
        if argv.is_empty() {
            eprintln!("omawin-qs: a Process started with no command");
            return false;
        }
        let mut c = command(&argv);
        let dir = cwd.to_string();
        if !dir.is_empty() {
            c.current_dir(dir);
        }
        c.stdin(if with_stdin { Stdio::piped() } else { Stdio::null() }).stdout(Stdio::piped()).stderr(Stdio::piped());
        let mut child = match c.spawn() {
            Ok(child) => child,
            Err(e) => {
                eprintln!("omawin-qs: couldn't start {}: {e}", argv[0]);
                return false;
            }
        };
        let out = child.stdout.take();
        let err = child.stderr.take();
        let pid = child.id() as i64;
        {
            let mut rust = self.as_mut().rust_mut();
            rust.stdin = child.stdin.take();
            rust.pid = pid;
            *rust.child.lock().unwrap() = Some(child);
        }
        let qt = self.qt_thread();
        // What it prints, handed to the UI thread as it arrives; then its exit, once both streams
        // are closed (so no output comes after exited).
        let pump = |mut stream: Box<dyn Read + Send>, qt: cxx_qt::CxxQtThread<qobject::ProcessCore>, is_err: bool| {
            std::thread::spawn(move || {
                let mut buf = [0u8; 8192];
                let mut pending = Vec::new();
                loop {
                    match stream.read(&mut buf) {
                        Ok(0) | Err(_) => break,
                        Ok(n) => {
                            pending.extend_from_slice(&buf[..n]);
                            // Hand over only whole UTF-8 characters; the rest waits for the next read.
                            let cut = match std::str::from_utf8(&pending) {
                                Ok(_) => pending.len(),
                                Err(e) if e.error_len().is_none() => e.valid_up_to(),
                                Err(_) => pending.len(),
                            };
                            let text = String::from_utf8_lossy(&pending[..cut]).into_owned();
                            pending.drain(..cut);
                            let _ = qt.queue(move |o: Pin<&mut qobject::ProcessCore>| {
                                let s = QString::from(text.as_str());
                                if is_err { o.stderr_data(s) } else { o.stdout_data(s) }
                            });
                        }
                    }
                }
            })
        };
        let readers: Vec<_> = [out.map(|s| (Box::new(s) as Box<dyn Read + Send>, false)), err.map(|s| (Box::new(s) as Box<dyn Read + Send>, true))]
            .into_iter()
            .flatten()
            .map(|(s, is_err)| pump(s, qt.clone(), is_err))
            .collect();
        let child = self.as_ref().rust().child.clone();
        std::thread::spawn(move || {
            for r in readers {
                let _ = r.join();
            }
            let code = loop {
                let mut guard = child.lock().unwrap();
                match guard.as_mut().map(|c| c.try_wait()) {
                    Some(Ok(Some(status))) => break status.code().unwrap_or(-1),
                    Some(Ok(None)) => {}
                    _ => break -1,
                }
                drop(guard);
                std::thread::sleep(std::time::Duration::from_millis(20));
            };
            *child.lock().unwrap() = None;
            // Quickshell's exitStatus: 0 for a normal exit, 1 for one it couldn't report (killed).
            let _ = qt.queue(move |o: Pin<&mut qobject::ProcessCore>| o.exited(code, if code < 0 { 1 } else { 0 }));
        });
        self.started();
        true
    }

    fn write(mut self: Pin<&mut Self>, data: &QString) {
        if let Some(stdin) = self.as_mut().rust_mut().stdin.as_mut() {
            let _ = stdin.write_all(data.to_string().as_bytes());
            let _ = stdin.flush();
        }
    }

    fn kill(mut self: Pin<&mut Self>) {
        let mut rust = self.as_mut().rust_mut();
        rust.stdin = None;
        if let Some(child) = rust.child.lock().unwrap().as_mut() {
            let _ = child.kill();
        }
    }

    fn signal(self: Pin<&mut Self>, _sig: i32) {
        self.kill()
    }

    fn process_id(&self) -> i64 {
        self.rust().pid
    }
}

impl qobject::IoHost {
    fn exec_detached(&self, command_line: &QString) -> bool {
        let argv = words(command_line);
        if argv.is_empty() {
            return false;
        }
        let mut c = command(&argv);
        c.stdin(Stdio::null()).stdout(Stdio::null()).stderr(Stdio::null());
        match c.spawn() {
            Ok(mut child) => {
                // Reaped in the background, so it never lingers as a zombie on Linux.
                std::thread::spawn(move || {
                    let _ = child.wait();
                });
                true
            }
            Err(e) => {
                eprintln!("omawin-qs: couldn't start {}: {e}", argv[0]);
                false
            }
        }
    }

    fn env(&self, name: &QString) -> QString {
        QString::from(std::env::var(name.to_string()).unwrap_or_default().as_str())
    }

    fn read_text(&self, path: &QString) -> QString {
        QString::from(std::fs::read_to_string(path.to_string()).unwrap_or_default().as_str())
    }

    fn file_exists(&self, path: &QString) -> bool {
        std::path::Path::new(&path.to_string()).is_file()
    }

    // A path from the command line, made whole (from the folder the host was started in), with
    // forward slashes, as QML's file URLs want it.
    fn absolute_path(&self, path: &QString) -> QString {
        let p = std::path::PathBuf::from(path.to_string());
        let full = std::fs::canonicalize(&p).unwrap_or(p);
        let text = full.to_string_lossy().replace('\\', "/");
        // Windows' canonical form starts with \\?\ ('//?/' once the slashes turn); QML wants C:/...
        QString::from(text.trim_start_matches("//?/"))
    }
}
