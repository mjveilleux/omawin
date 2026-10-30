-- bootstrap lazy.nvim, LazyVim and your plugins
require("config.lazy")

-- Elixir: IEx helpers (<leader>is start, <leader>ir run), only in Elixir buffers.
vim.api.nvim_create_autocmd("FileType", {
  pattern = "elixir",
  callback = function(args)
    require("iex").setup(args.buf)
  end,
})

-- Neovide's font, padding and motion (nothing in the terminal).
require("config.neovide")
