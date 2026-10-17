return {
  -- which-key labels for the custom groups
  {
    "folke/which-key.nvim",
    opts = {
      spec = {
        { "<leader>r", group = "run" },
        { "<leader>9", group = "99", mode = "v" },
      },
    },
  },
}
