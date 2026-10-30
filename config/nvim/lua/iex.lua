-- IEx helpers for Elixir buffers (keymaps are set per buffer from init.lua):
--   <leader>is  start IEx in a split below (or show the one already running)
--   <leader>ir  send the whole file to IEx (normal mode) or the selected lines (visual mode)
-- Inside a Mix project IEx starts as `iex -S mix` from the project root, so the project's
-- modules are loaded. On Windows the command is iex.bat: in PowerShell a bare `iex` is
-- the built-in alias for Invoke-Expression, not Elixir.
local M = {}

local iex = { buf = nil, chan = nil }

local function running()
  return iex.buf and vim.api.nvim_buf_is_valid(iex.buf) and iex.chan
    and vim.fn.jobwait({ iex.chan }, 0)[1] == -1 -- -1: still running
end

-- Lines of the visual selection (leaves visual mode so the '< and '> marks are set).
local function visual_lines()
  vim.api.nvim_feedkeys(vim.api.nvim_replace_termcodes("<Esc>", true, false, true), "x", false)
  local start_row, end_row = vim.fn.getpos("'<")[2], vim.fn.getpos("'>")[2]
  return vim.api.nvim_buf_get_lines(0, start_row - 1, end_row, false)
end

function M.start()
  if vim.bo.filetype ~= "elixir" then
    vim.notify("Not an Elixir buffer", vim.log.levels.WARN)
    return
  end
  if running() then
    if vim.fn.bufwinid(iex.buf) == -1 then -- running but hidden: show it again
      vim.cmd("botright sbuffer " .. iex.buf)
      vim.cmd("wincmd p")
    end
    return
  end

  local cmd = { vim.fn.has("win32") == 1 and "iex.bat" or "iex" }
  local mix = vim.fs.root(0, "mix.exs")
  if mix then
    vim.list_extend(cmd, { "-S", "mix" })
  end

  vim.cmd("botright split | enew")
  iex.buf = vim.api.nvim_get_current_buf()
  iex.chan = vim.fn.jobstart(cmd, { term = true, cwd = mix or vim.fn.getcwd() })
  vim.cmd("normal! G")
  vim.cmd("wincmd p") -- back to the code
end

function M.send(mode)
  if not running() then
    vim.notify("IEx isn't running: start it with <leader>is", vim.log.levels.WARN)
    return
  end
  local lines = mode == "visual" and visual_lines() or vim.api.nvim_buf_get_lines(0, 0, -1, false)
  -- One line at a time with Enter, the way you'd type it; IEx waits for unfinished
  -- expressions (do ... end, open brackets) before evaluating.
  vim.fn.chansend(iex.chan, table.concat(lines, "\r") .. "\r")
  -- keep the IEx window scrolled to the newest output
  local win = vim.fn.bufwinid(iex.buf)
  if win ~= -1 then
    vim.api.nvim_win_call(win, function()
      vim.cmd("normal! G")
    end)
  end
end

function M.setup(bufnr)
  vim.keymap.set("n", "<leader>is", M.start, { buffer = bufnr, desc = "Start IEx" })
  vim.keymap.set("n", "<leader>ir", function()
    M.send("normal")
  end, { buffer = bufnr, desc = "Send file to IEx" })
  vim.keymap.set("v", "<leader>ir", function()
    M.send("visual")
  end, { buffer = bufnr, desc = "Send selection to IEx" })
end

return M
