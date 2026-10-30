-- Elixir: LazyVim's lang.elixir extra (treesitter, neotest-elixir, credo) with Expert,
-- the official Elixir language server, in place of the extra's elixir-ls.
-- Only one Elixir server runs, so diagnostics and completions aren't doubled up.
-- (elixir-ls stays installed in Mason; flip `enabled` below to switch back.)
return {
  {
    "neovim/nvim-lspconfig",
    opts = {
      servers = {
        expert = {},
        elixirls = { enabled = false },
      },
    },
  },
  {
    "mason-org/mason.nvim",
    opts = { ensure_installed = { "expert" } },
  },
}
