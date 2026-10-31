// Omawin.Native's BarLink: what omawin's bar tells the host it started, over the host's stdin.
// The bar (shell/menu-app/Bar/PluginSlot.cs) runs one host per plugin per monitor, and only it
// knows which monitor GlazeWM has focused; a plugin needs that to answer a key on the right one
// (the music plugin's Win+M opens the panel on the focused monitor's bar). One line a message,
// `<name> <value>`:
//
//   focused 1|0     this host's bar is on GlazeWM's focused monitor (1) or not (0)
//
// Lines it doesn't know are skipped, so the bar can say more than an older host understands.

#[cxx_qt::bridge]
pub mod qobject {
    unsafe extern "RustQt" {
        // The bar's word, as properties a plugin binds to (Host.qml hands them on as `bar.*`).
        #[qobject]
        #[qml_element]
        #[qml_singleton]
        #[qproperty(bool, focused)]
        type BarLink = super::BarLinkRust;
    }

    impl cxx_qt::Threading for BarLink {}
    impl cxx_qt::Initialize for BarLink {}
}

use core::pin::Pin;
use cxx_qt::Threading;
use std::io::BufRead;
use std::sync::atomic::{AtomicBool, Ordering};

pub struct BarLinkRust {
    focused: bool,
}

// Until the bar says otherwise this host's bar is the focused one: a host run on its own (by
// hand, or a bar older than this) behaves as it did when there was only ever one.
impl Default for BarLinkRust {
    fn default() -> Self {
        Self { focused: true }
    }
}

/// One message from the bar.
#[derive(Debug, PartialEq, Eq)]
pub enum Message {
    /// Whether this host's bar is on GlazeWM's focused monitor.
    Focused(bool),
}

/// Reads one line of the bar's; `None` for a line it doesn't know (or a blank one).
pub fn parse(line: &str) -> Option<Message> {
    let mut words = line.split_whitespace();
    let name = words.next()?;
    let value = words.next()?;
    if words.next().is_some() {
        return None;
    }
    match (name, value) {
        ("focused", "1") => Some(Message::Focused(true)),
        ("focused", "0") => Some(Message::Focused(false)),
        _ => None,
    }
}

// stdin has one reader for the life of the program, however many times QML makes the singleton.
static READING: AtomicBool = AtomicBool::new(false);

impl cxx_qt::Initialize for qobject::BarLink {
    /// Starts reading the bar's lines from stdin, each handed to the UI thread as it comes.
    ///
    /// Without a bar (stdin closed, or a terminal nobody types in) nothing ever arrives and the
    /// defaults stand.
    fn initialize(self: Pin<&mut Self>) {
        if READING.swap(true, Ordering::SeqCst) {
            return;
        }
        let qt = self.qt_thread();
        std::thread::spawn(move || {
            for line in std::io::stdin().lock().lines() {
                let Ok(line) = line else { break };
                let Some(message) = parse(&line) else {
                    continue;
                };
                let queued = qt.queue(move |link: Pin<&mut qobject::BarLink>| match message {
                    Message::Focused(on) => link.set_focused(on),
                });
                // The QObject is gone (the program is ending): nothing left to tell.
                if queued.is_err() {
                    break;
                }
            }
        });
    }
}

#[cfg(test)]
mod tests {
    use super::{Message, parse};

    #[test]
    fn reads_focus() {
        assert_eq!(parse("focused 1"), Some(Message::Focused(true)));
        assert_eq!(parse("focused 0\r"), Some(Message::Focused(false)));
        assert_eq!(parse("  focused   1  "), Some(Message::Focused(true)));
    }

    #[test]
    fn skips_what_it_does_not_know() {
        assert_eq!(parse(""), None);
        assert_eq!(parse("focused"), None);
        assert_eq!(parse("focused yes"), None);
        assert_eq!(parse("focused 1 extra"), None);
        assert_eq!(parse("monitor DISPLAY1"), None);
    }
}
