-- ThePrimeagen/99: AI edits on a visual selection. Loads on first use of its keys.
-- Completion inside 99 needs nvim-cmp, which would run a second completion engine next to
-- LazyVim's blink.cmp, so it is off. (To get it back, enable the LazyVim extra
-- coding.nvim-cmp via :LazyExtras, which swaps blink for cmp, and set source = "cmp".)
return {
  "ThePrimeagen/99",
  keys = {
    { "<leader>9v", function() require("99").visual() end, mode = "v", desc = "99: edit selection" },
    { "<leader>9s", function() require("99").stop_all_requests() end, mode = "v", desc = "99: stop requests" },
  },
  config = function()
    local _99 = require("99")
    local basename = vim.fs.basename(vim.uv.cwd())
    local tmp = vim.env.TEMP or vim.env.TMP or "C:\\Temp"

    _99.setup({
      logger = {
        level = _99.WARN,
        path = tmp .. "\\" .. basename .. ".99.log",
        print_on_error = true,
      },
      completion = {
        custom_rules = { "scratch/custom_rules/" },
        files = {},
        source = nil,
      },
      md_files = { "AGENTS.md", "AGENT.md", "SPEC.md", "README.md" },
    })
  end,
}
