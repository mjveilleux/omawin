-- The colorscheme follows the desktop theme, live. The theme picker (system menu > Style >
-- Theme, which runs themes/set-theme.ps1) writes the theme's name to
-- %LOCALAPPDATA%\launcher\nvim-theme; its spec is ~/omawin/themes/nvim/<name>.lua
-- (Omarchy's neovim.lua for that theme: a colorscheme plugin, and LazyVim's colorscheme).
-- Every theme's plugin is installed (and loaded only when used), so a running Neovim or
-- Neovide switches as soon as the file changes, without a restart.
local M = {}

local dir = vim.fn.expand("~/omawin/themes/nvim")
local state = vim.env.LOCALAPPDATA and (vim.env.LOCALAPPDATA .. "/launcher/nvim-theme")

-- The chosen theme's name; nil while the file is being rewritten (it's briefly empty).
local function read()
  local file = state and io.open(state, "r")
  if not file then
    return nil
  end
  local name = vim.trim(file:read("*a") or "")
  file:close()
  return name ~= "" and name or nil
end

function M.current()
  return read() or "starship"
end

-- Tokyo Night this setup's way: "night", the same palette as the Windows Terminal scheme;
-- transparent in a terminal (its acrylic shows through), solid in Neovide.
local function tokyonight()
  local transparent = not vim.g.neovide
  return {
    {
      "folke/tokyonight.nvim",
      priority = 1000,
      opts = {
        style = "night",
        transparent = transparent,
        styles = {
          sidebars = transparent and "transparent" or "dark",
          floats = transparent and "transparent" or "dark",
          comments = { italic = true },
          keywords = { italic = true },
        },
      },
    },
    { "LazyVim/LazyVim", opts = { colorscheme = "tokyonight-night" } },
  }
end

function M.spec(name)
  if name == "tokyo-night" then
    return tokyonight()
  end
  local ok, spec = pcall(dofile, dir .. "/" .. name .. ".lua")
  return ok and type(spec) == "table" and spec or tokyonight()
end

-- A theme's spec: its plugins, and the colorscheme it sets.
local function split(spec)
  local plugins, colorscheme = {}, nil
  for _, s in ipairs(spec) do
    if s[1] == "LazyVim/LazyVim" then
      colorscheme = s.opts and s.opts.colorscheme
    else
      table.insert(plugins, s)
    end
  end
  return plugins, colorscheme
end

-- lazy.nvim's name for a plugin: its name =, or the repo's.
local function plugin_name(p)
  return p.name or p[1]:match("[^/]+$")
end

-- Dark or light, as the theme is (themes/<name>.json).
local function set_background(name)
  local file = io.open(vim.fn.expand("~/omawin/themes/" .. name .. ".json"), "r")
  if file then
    local ok, json = pcall(vim.json.decode, file:read("*a"))
    file:close()
    vim.o.background = ok and json.mode == "light" and "light" or "dark"
  end
end

-- For lazy.nvim: the current theme's spec as it is, plus every other theme's plugin, lazy.
function M.lazy_specs()
  set_background(M.current())
  local plugins, colorscheme = split(M.spec(M.current()))
  local specs, seen = {}, {}
  for _, p in ipairs(plugins) do
    table.insert(specs, p)
    seen[plugin_name(p)] = true
  end
  for _, file in ipairs(vim.fn.glob(dir .. "/*.lua", false, true)) do
    for _, p in ipairs((split(M.spec(vim.fn.fnamemodify(file, ":t:r"))))) do
      local name = plugin_name(p)
      if not seen[name] then
        seen[name] = true
        table.insert(specs, { p[1], name = p.name, branch = p.branch, dependencies = p.dependencies, lazy = true })
      end
    end
  end
  table.insert(specs, { "LazyVim/LazyVim", opts = { colorscheme = colorscheme } })
  return specs
end

-- Switch to a theme now: load its plugin, set it up with the theme's options, apply it.
function M.apply(name)
  local plugins, colorscheme = split(M.spec(name))
  if not colorscheme then
    return
  end
  local Config = require("lazy.core.config")
  for _, p in ipairs(plugins) do
    local plugin = Config.plugins[plugin_name(p)]
    if plugin then
      if not plugin._.installed then
        require("lazy").install({ plugins = { plugin.name }, wait = true, show = false })
      end
      require("lazy").load({ plugins = { plugin.name } })
      if p.opts then
        local main = require("lazy.core.loader").get_main(plugin)
        local ok, mod = pcall(require, main)
        if ok and type(mod) == "table" and mod.setup then
          pcall(mod.setup, p.opts)
        end
      end
    end
  end
  -- Before the colorscheme, or one with both (Kanagawa) keeps the previous theme's.
  set_background(name)
  local ok, err = pcall(vim.cmd.colorscheme, colorscheme)
  if not ok then
    vim.notify("theme " .. name .. ": " .. tostring(err), vim.log.levels.WARN)
  end
end

-- Watch the theme file; apply a new theme as it's picked. The folder is watched, not the file:
-- a watch on the file itself stops when the file is replaced (written aside, then renamed over).
-- Other files there change often (the terminal's focus at every prompt, the clipboard), so the
-- name is checked in the fast callback itself, and only the theme file schedules any work.
function M.watch()
  if not state or M.watcher then
    return
  end
  local last = M.current()
  local base = vim.fn.fnamemodify(state, ":t")
  local changed = vim.schedule_wrap(function()
    local name = read()
    if name and name ~= last then
      last = name
      M.apply(name)
    end
  end)
  M.watcher = vim.uv.new_fs_event()
  M.watcher:start(vim.fn.fnamemodify(state, ":h"), {}, function(_, file)
    if file and file:lower() ~= base:lower() then
      return
    end
    changed()
  end)
end

return M
