-- The colorscheme: the desktop theme's (system menu > Style > Theme), switching live in every
-- running Neovim and Neovide. Tokyo Night when no theme has been picked, or off Windows.
-- See lua/live_theme.lua.
local theme = require("live_theme")

vim.api.nvim_create_autocmd("User", {
  pattern = "VeryLazy",
  once = true,
  callback = theme.watch,
})

return theme.lazy_specs()
