-- Databases. LazyVim's lang.sql extra already sets up vim-dadbod, blink.cmp completion
-- (vim-dadbod-completion) and DBUI. This file only overrides DBUI:
--   * Doekeb's fork of vim-dadbod-ui (DuckDB support),
--   * opens on the right, no help banner, no auto-run on save or for table helpers,
--   * keeps DBUI's default save location, where your saved connections already live.
return {
  {
    "kristijanhusak/vim-dadbod-ui",
    url = "https://github.com/Doekeb/vim-dadbod-ui",
    keys = {
      { "<leader>db", "<cmd>DBUIToggle<cr>", desc = "Toggle DBUI" },
    },
    init = function()
      vim.g.db_ui_use_postgres_views = 0
      vim.g.db_ui_show_help = 0
      vim.g.db_ui_auto_execute_table_helpers = 0
      vim.g.db_ui_win_position = "right"
      vim.g.db_ui_execute_on_save = 0
      vim.g.db_ui_use_nerd_fonts = 1
      vim.g.db_ui_use_nvim_notify = 1
    end,
  },
}
