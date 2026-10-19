-- The AI agent in a split on the right: LazyVim's sidekick extra (imported in config/lazy.lua),
-- which runs any agent CLI in a Neovim terminal and sends it the line, selection, file or
-- diagnostics you're on. Which agent opens is omawin's default (`omawin default agent claude`),
-- per account; <leader>as starts another one beside it.
-- Check the setup with :checkhealth sidekick.

-- The pick in %LOCALAPPDATA%\launcher\defaults\agent, else the first agent row of bin/defaults.txt.
local function default_agent()
  local f = io.open(vim.env.LOCALAPPDATA .. "/launcher/defaults/agent", "r")
  if f then
    local name = vim.trim(f:read("*a"))
    f:close()
    if name ~= "" then
      return name
    end
  end
  local repo = vim.fs.dirname((vim.uv or vim.loop).fs_realpath(vim.fn.stdpath("config")) or "")
  for _, line in ipairs(vim.fn.readfile(repo .. "/bin/defaults.txt")) do
    local name = line:match("^agent%s*|%s*([%w_-]+)")
    if name then
      return name
    end
  end
  return "claude"
end

-- Send to the agent that's open; with none open, start the default one rather than asking.
local function target()
  if #require("sidekick.cli.state").get({ attached = true }) > 0 then
    return nil
  end
  return default_agent()
end

local function cli()
  return require("sidekick.cli")
end

return {
  {
    "folke/sidekick.nvim",
    opts = {
      -- Next edit suggestions need a Copilot subscription; off, which also keeps the extra
      -- from starting the Copilot language server.
      nes = { enabled = false },
    },
    -- stylua: ignore
    keys = {
      -- The extra's keys, made to open the default agent when none is open.
      { "<c-.>", function() cli().focus({ name = target() }) end, desc = "Agent: focus", mode = { "n", "t", "i", "x" } },
      { "<leader>aa", function() cli().toggle({ name = target() }) end, desc = "Agent: toggle" },
      { "<leader>at", function() cli().send({ msg = "{this}", name = target() }) end, mode = { "x", "n" }, desc = "Agent: send this line / these lines" },
      { "<leader>af", function() cli().send({ msg = "{file}", name = target() }) end, desc = "Agent: send file" },
      { "<leader>av", function() cli().send({ msg = "{selection}", name = target() }) end, mode = { "x" }, desc = "Agent: send selected text" },
      { "<leader>ap", function() cli().prompt(function(_, text) if text then cli().send({ text = text, name = target() }) end end) end, mode = { "n", "x" }, desc = "Agent: pick a prompt (explain, fix, diagnostics, review…)" },
      { "<leader>as", function() cli().select({ filter = { installed = true } }) end, desc = "Agent: start another" },
    },
  },
}
