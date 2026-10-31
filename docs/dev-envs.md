# Development environments

Install > Development and Remove > Development set up a language the way Omarchy's `omarchy-install-dev-env` / `omarchy-remove-dev-env` do: the same environments, in the same order, mostly through [mise](https://mise.jdx.dev). `bin/install-dev-env.ps1 <name>` and `bin/remove-dev-env.ps1 <name>` do the work; `install-dev-env.ps1 -Check [name]` says what's installed (one name: exit 0 or 1; no name: the installed ones, one a line).

**Which side.** With your Linux in WSL set up (the `wsl` preset), both ask first whether it goes in Linux or in Windows, Linux first (Windows first while PowerShell is your default shell, `omawin default shell windows`); `-Linux` or `-Windows` answers, and a run with nobody to answer takes the default shell's side. In Linux the work is `wsl/bin/omawin-dev-env` (also in Linux's `~/.local/bin`): Omarchy's `omarchy-install-dev-env`, `omarchy-remove-dev-env` and `omarchy-install-docker-dbs` as they are on Omarchy (mise for the languages, rustup with clang, opam from the distro, `symfony-cli` from pacman or mise on Ubuntu), and Docker DB on Docker Engine, which comes with Linux (roadmap 3.6): the same six containers, bound to `127.0.0.1`, which mirrored networking makes `localhost:5432` for Windows' apps too. `omawin-dev-env check [name]` is Linux's `-Check`; `install-dev-env.ps1 -Check -Linux` asks it from Windows (starting Linux). The rest of this page is the Windows side.

mise comes from winget (`jdx.mise`, user scope) the first time one is installed. Its tools are global (`~/.config/mise/config.toml`) and run through its shims in `%LOCALAPPDATA%\mise\shims`, put first on the user PATH, so terminals and GUI programs see the same versions without a shell hook. Windows puts the system PATH before the user's, so a copy installed for all users (a machine-wide Node in `C:\Program Files\nodejs`, say) still wins; the install says so when that happens.

Where mise can't install a language on Windows, the Windows-native route stands in:

| Environment | On Windows | Omarchy |
|---|---|---|
| Ruby on Rails | RubyInstaller with MSYS2 (newest `RubyInstallerTeam.RubyWithDevKit.*`), `ridk install 3`, `gem install rails` | mise ruby (Linux builds) |
| Docker DB | `docker run`, Omarchy's six databases and settings, picked with fzf; needs Docker Desktop running (Linux has Docker Engine: pick Linux) | the same |
| Node.js, Bun, Deno, Go | mise | mise |
| PHP | php.net's build (newest `PHP.PHP.*`, winget), a `php.ini` with the usual extensions, Composer in `~/.local/bin` | mise, static Linux builds |
| Laravel | PHP + Node, `composer global require laravel/installer` | the same |
| Symfony | PHP + the Symfony CLI's release through mise (`github:symfony-cli/symfony-cli`) | pacman `symfony-cli` |
| Python | mise python (prebuilt) + uv | the same |
| Elixir, Phoenix | Erlang/OTP and Elixir installers (winget), then `mix local.hex`, `local.rebar`, `phx_new` | mise (kerl, Unix only) |
| Zig | mise zig + zls | the same |
| Rust | rustup (winget `Rustlang.Rustup`); linking needs the Visual Studio C++ build tools, which it names when missing | rustup + clang |
| Java | mise java (Temurin) | the same |
| .NET | mise dotnet | the same |
| OCaml | opam for Windows (winget `OCaml.opam`), `opam init` with its own Cygwin, then the LSP, odoc, ocamlformat, utop | opam |
| Clojure | Java from mise + deps.clj (`github:borkdude/deps.clj`, a native Clojure CLI), as `clojure` and `clj` | mise clojure (Unix script) |
| Scala | mise java, scala, scala-cli | the same |

What counts as installed (the menu's check mark) follows Omarchy's checks: a version in `%LOCALAPPDATA%\mise\installs\<tool>`, `~/.rustup` for Rust, the opam root for OCaml, `C:\Ruby*` for Ruby, `~/.mix/archives/phx_new*` for Phoenix.
