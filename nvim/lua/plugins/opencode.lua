-- opencode.nvim: drive OpenCode from Neovim (send selections, buffers, diagnostics).
-- Tracks the plugin's `main` branch, which targets OpenCode v2 (the release tags are v1).
--
-- OpenCode v2 runs a shared background service; the plugin finds it through
-- ~/.local/state/opencode/service.json, so `opencode` no longer needs `--port`.
-- Prompts go to the most recently updated session for Neovim's working directory.
-- v2 removed the remote TUI controls (new session, undo, scrolling): do those in the
-- OpenCode split itself (`/new`, `/undo`, mouse or PageUp/PageDown).
-- Check the setup with :checkhealth opencode.

local cmd = "opencode"
local term_opts = { win = { position = "right", enter = false, width = 0.4 } }

local function terminal_toggle()
  require("snacks.terminal").toggle(cmd, term_opts)
end

-- POST to an endpoint of the current session (the plugin has no wrapper for these).
local function session_post(action, body)
  require("opencode.server.discovery")
    .get()
    :next(function(server)
      return server:resolve_session():next(function(session)
        return server:request("/api/session/" .. session.id .. "/" .. action, "POST", body)
      end)
    end)
    :catch(function(err)
      vim.notify(tostring(err), vim.log.levels.ERROR, { title = "opencode" })
    end)
end

return {
  {
    "nickjvandyke/opencode.nvim",
    version = false, -- main branch: OpenCode v2 support
    dependencies = { "folke/snacks.nvim" },
    config = function()
      ---@type opencode.Opts
      vim.g.opencode_opts = {
        server = {
          -- No OpenCode running? Open it in a snacks terminal split on the right.
          start = function()
            require("snacks.terminal").open(cmd, term_opts)
          end,
        },
      }

      -- Show the OpenCode split when it starts working on something sent from Neovim.
      vim.api.nvim_create_autocmd("User", {
        pattern = "OpencodeEvent:session.execution.started",
        callback = function()
          local win = require("snacks.terminal").get(cmd, { create = false })
          if win then
            win:show()
          end
        end,
      })

      local oc = function()
        return require("opencode")
      end
      local map = vim.keymap.set

      map({ "n", "x" }, "<leader>oa", function() oc().ask("@this: ") end, { desc = "Ask (about @this)" })
      map({ "n", "x" }, "<leader>os", function() oc().select() end, { desc = "Select prompt/command" })
      map("n", "<leader>ob", function() oc().ask("@buffer: ") end, { desc = "Ask about buffer" })
      map("n", "<leader>od", function() oc().prompt("Explain @diagnostics") end, { desc = "Explain diagnostics" })
      map("n", "<leader>of", function() oc().prompt("Fix @diagnostics") end, { desc = "Fix diagnostics" })
      map("n", "<leader>or", function() oc().command("review") end, { desc = "Review uncommitted changes" })
      map("n", "<leader>oi", function() session_post("interrupt") end, { desc = "Interrupt session" })
      map("n", "<leader>oc", function() session_post("compact", {}) end, { desc = "Compact session" })
      map("n", "<leader>ot", terminal_toggle, { desc = "Toggle opencode split" })
      map({ "n", "t" }, "<C-.>", terminal_toggle, { desc = "Toggle opencode split" })

      -- Operator: `go{motion}` sends that range as a prompt, `goo` the current line (dot-repeatable).
      map({ "n", "x" }, "go", function() return oc().operator("@this") end, { desc = "Send range to opencode", expr = true })
      map("n", "goo", function() return oc().operator("@this") .. "_" end, { desc = "Send line to opencode", expr = true })
    end,
  },

  -- which-key group for the <leader>o mappings
  {
    "folke/which-key.nvim",
    opts = { spec = { { "<leader>o", group = "opencode", icon = { icon = "󰚩 ", color = "green" } } } },
  },

  -- opencode server status in the statusline
  {
    "nvim-lualine/lualine.nvim",
    opts = function(_, opts)
      table.insert(opts.sections.lualine_x, 1, {
        function()
          return require("opencode").statusline()
        end,
      })
    end,
  },
}
