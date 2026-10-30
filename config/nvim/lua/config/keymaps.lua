-- Keymaps are loaded on the VeryLazy event.
-- LazyVim defaults: https://github.com/LazyVim/LazyVim/blob/main/lua/lazyvim/config/keymaps.lua
-- (The clipboard is already shared with the system: LazyVim sets clipboard=unnamedplus.)

local map = vim.keymap.set

-- Line motions and tabs
map({ "n", "v" }, "<Tab>h", "0", { desc = "Start of line" })
map({ "n", "v" }, "<Tab>l", "$", { desc = "End of line" })
map("n", "<Tab>t", "<cmd>tabnext<cr>", { desc = "Next tab" })

-- Surround the selected word with quotes
map("v", "<leader>wsq", "ciw''<Esc>P", { desc = "Surround with single quotes" })
map("v", "<leader>wsQ", 'ciw""<Esc>P', { desc = "Surround with double quotes" })

-- Whole buffer
map("n", "YY", "<cmd>%y<cr>", { desc = "Yank whole buffer" })
map("n", "PP", "ggVGp", { desc = "Replace whole buffer with paste" })
map("n", "<leader>wa", "<cmd>wa<cr>", { desc = "Save all files" })

-- Run code: <leader>r
-- (Moved off <leader><space> and <leader>x so LazyVim's find-files and diagnostics keys fire instantly.)
map("n", "<leader>rs", "<cmd>source %<cr>", { desc = "Source file (Lua)" })
map("n", "<leader>rl", "<cmd>.lua<cr>", { desc = "Run line (Lua)" })
map("v", "<leader>rl", ":lua<cr>", { desc = "Run selection (Lua)" })

--- Python for the project: uv if it has uv.lock, poetry if poetry.lock, else plain python.
local function python()
  local root = LazyVim.root()
  if vim.uv.fs_stat(root .. "/uv.lock") then
    return "uv run python"
  elseif vim.uv.fs_stat(root .. "/poetry.lock") then
    return "poetry run python"
  end
  return "python"
end

map("n", "<leader>rp", function()
  vim.cmd("update")
  vim.cmd("!" .. python() .. " %")
end, { desc = "Run file (Python)" })
map("v", "<leader>rp", function()
  return ":<C-u>'<,'>w !" .. python() .. "<cr>"
end, { desc = "Run selection (Python)", expr = true })
