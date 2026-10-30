# Sourced by ~/.Rprofile (a one-line stub install.ps1 writes; ~ is Documents for R).
#
# RStudio follows the desktop theme, live. The theme picker (system menu > Style > Theme)
# runs themes/set-theme.ps1, which writes an RStudio editor theme made from the palette to
# %APPDATA%\RStudio\themes\<name>.rstheme and the theme's name to %LOCALAPPDATA%\launcher\theme.
# RStudio only reads its prefs at start, so this checks that file every 2 seconds while R
# is idle and applies a new theme with rstudioapi (RStudio colors its panes to match).
# Needs the rstudioapi and later packages; without them it does nothing.
if (interactive() && Sys.getenv("RSTUDIO") == "1" &&
    requireNamespace("rstudioapi", quietly = TRUE) && requireNamespace("later", quietly = TRUE)) {
  setHook("rstudio.sessionInit", function(newSession) {
    state <- file.path(Sys.getenv("LOCALAPPDATA"), "launcher", "theme")
    themes <- file.path(Sys.getenv("APPDATA"), "RStudio", "themes")
    last <- ""
    check <- function() {
      name <- tryCatch(readLines(state, warn = FALSE)[1], error = function(e) NA_character_)
      rstheme <- file.path(themes, paste0(name, ".rstheme"))
      if (!is.na(name) && nzchar(name) && name != last && file.exists(rstheme)) {
        last <<- name
        # Its display name, "Omarchy Tokyo Night", from the file's rs-theme-name line.
        title <- sub(".*rs-theme-name:\\s*(.*?)\\s*\\*/.*", "\\1",
                     grep("rs-theme-name", readLines(rstheme, warn = FALSE), value = TRUE)[1])
        if (!identical(rstudioapi::getThemeInfo()$editor, title)) {
          try(rstudioapi::applyTheme(title), silent = TRUE)
        }
      }
      later::later(check, 2)
    }
    check()
  }, action = "append")
}
