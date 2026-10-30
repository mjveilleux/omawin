-- ~/.config/nvim/lua/plugins/sql-format.lua
return {
  {
    "stevearc/conform.nvim",
    opts = function(_, opts)
      opts.formatters = opts.formatters or {}
      opts.formatters.sqlfluff = { args = { "format", "--dialect=ansi", "-" } }
      opts.formatters_by_ft = opts.formatters_by_ft or {}
      for _, ft in ipairs({ "sql", "mysql", "plsql", "psql" }) do
        opts.formatters_by_ft[ft] = opts.formatters_by_ft[ft] or {}
        table.insert(opts.formatters_by_ft[ft], "sqlfluff")
      end
    end,
  },
}
