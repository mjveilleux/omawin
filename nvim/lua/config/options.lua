-- Options are loaded before lazy.nvim starts.
-- LazyVim defaults: https://github.com/LazyVim/LazyVim/blob/main/lua/lazyvim/config/options.lua

-- No snacks smooth-scroll animation.
vim.g.snacks_scroll = false

-- Remote-plugin providers: nothing in this config uses them, and probing for them costs
-- startup time (and fills :checkhealth with warnings).
vim.g.loaded_node_provider = 0
vim.g.loaded_perl_provider = 0
vim.g.loaded_python3_provider = 0
vim.g.loaded_ruby_provider = 0

-- PowerShell 7 for :!, :terminal and the floating terminal. LazyVim sets the matching
-- shellcmdflag/shellquote options so commands and quoting work.
if vim.fn.has("win32") == 1 and vim.fn.executable("pwsh") == 1 then
  LazyVim.terminal.setup("pwsh")
end
