-- Where omawin's things are, from Windows' Neovim or from WSL's: WSL's Neovim runs the same config
-- through ~/.config/nvim -> /mnt/c/Users/<you>/omawin/config/nvim (wsl/setup.sh), and there `~` and
-- %LOCALAPPDATA% aren't Windows'. The repo is always ~/omawin (omawin check), so Windows' home
-- folder is the one it's in.
local M = {}

local uv = vim.uv or vim.loop
local config = vim.fn.stdpath("config")

M.windows = vim.fn.has("win32") == 1
M.wsl = vim.fn.has("wsl") == 1
-- the omawin checkout (the config is its config/nvim)
M.repo = vim.fs.dirname(vim.fs.dirname(uv.fs_realpath(config) or config))
-- Windows' home folder (~ on Windows)
M.home = M.windows and vim.fn.expand("~") or vim.fs.dirname(M.repo)
-- %LOCALAPPDATA%, where omawin keeps its state (launcher\: defaults, the theme)
M.localappdata = M.windows and vim.env.LOCALAPPDATA or (M.home .. "/AppData/Local")

return M
