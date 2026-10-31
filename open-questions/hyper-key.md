# A Hyper key from Caps Lock?

**Question.** Should Caps Lock be offered as a Hyper key (Ctrl+Shift+Alt+Win held together), as Omarchy-style setups on other systems do, so GlazeWM can bind Hyper+letter combinations?

**Context.**
- The live remap (roadmap 8.12, `shell/menu-app/KeyHook.cs`) sends the new keys with `SendInput`. Those are injected keys, and GlazeWM ignores injected keys (AGENTS.md), so a Hyper made this way can't trigger GlazeWM's bindings, which is the main reason to want one.
- Windows' own remap (`Scancode Map`, `omawin keyboard signin`) maps one key to one key only: it can't make a key press four modifiers.
- Win released alone opens Start; a Hyper key has to mask that (send an unassigned key between Win down and up).

**Options.**
1. Don't offer Hyper (Escape, Control, Escape tapped / Control held only).
2. The GlazeWM fork accepts injected keys carrying omawin's tag (`dwExtraInfo == 0x6F6D6177`), then offer Hyper.
3. Hyper bindings handled in the shell's hook instead of GlazeWM (a second binding table).

**Default taken: 1.** `input.caps-lock` has no Hyper choice. Option 2 is the clean one if Hyper is wanted: a small change in the fork's keyboard hook, and Hyper is one more mode in `CapsLockRemap`.

Delete this file once answered.
