-- Floating terminal: toggleterm.nvim.
--   <C-\>        toggle the floating terminal (normal, insert and terminal mode)
--   2<C-\>       a second, separate terminal (any count works)
--   <leader>T    same toggle, discoverable in which-key
--   <Esc><Esc>   leave terminal mode (then <C-\> hides it)
-- Runs PowerShell 7 (see config/options.lua). LazyVim's own <C-/> split terminal still works.
return {
  {
    "akinsho/toggleterm.nvim",
    version = "*",
    event = "VeryLazy",
    keys = {
      { "<leader>T", "<cmd>ToggleTerm direction=float<cr>", desc = "Floating terminal" },
    },
    opts = {
      open_mapping = [[<c-\>]],
      direction = "float",
      insert_mappings = true,
      terminal_mappings = true,
      start_in_insert = true,
      persist_mode = true,
      shade_terminals = false,
      float_opts = {
        border = "curved",
        width = function()
          return math.floor(vim.o.columns * 0.85)
        end,
        height = function()
          return math.floor(vim.o.lines * 0.8)
        end,
        winblend = 0,
      },
    },
  },
}
