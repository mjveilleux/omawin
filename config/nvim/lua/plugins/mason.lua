-- Formatters that conform.nvim is configured to use but no LazyVim extra installs:
--   prettier: markdown formatting (lang.markdown extra)
--   sqlfluff: SQL formatting (plugins/sql-format.lua); pip-based, so Python must be on PATH
return {
  "mason-org/mason.nvim",
  opts = { ensure_installed = { "prettier", "sqlfluff" } },
}
