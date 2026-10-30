local lazypath = vim.fn.stdpath("data") .. "/lazy/lazy.nvim"
if not (vim.uv or vim.loop).fs_stat(lazypath) then
  local lazyrepo = "https://github.com/folke/lazy.nvim.git"
  local out = vim.fn.system({ "git", "clone", "--filter=blob:none", "--branch=stable", lazyrepo, lazypath })
  if vim.v.shell_error ~= 0 then
    vim.api.nvim_echo({
      { "Failed to clone lazy.nvim:\n", "ErrorMsg" },
      { out, "WarningMsg" },
      { "\nPress any key to exit..." },
    }, true, {})
    vim.fn.getchar()
    os.exit(1)
  end
end
vim.opt.rtp:prepend(lazypath)

-- What lazy.nvim and LazyVim write (the plugin lockfile after :Lazy update, lazyvim.json after
-- :LazyExtras) lives in nvim-data, outside the omawin checkout, so an update never trips over it.
-- The repo's copies are omawin's tested picks: they seed nvim-data the first time.
local data = vim.fn.stdpath("data")
local config = vim.fn.stdpath("config")
local lockfile = data .. "/lazy-lock.json"
vim.g.lazyvim_json = data .. "/lazyvim.json"
for _, name in ipairs({ "lazy-lock.json", "lazyvim.json" }) do
  if not (vim.uv or vim.loop).fs_stat(data .. "/" .. name) then
    vim.fn.mkdir(data, "p")
    pcall((vim.uv or vim.loop).fs_copyfile, config .. "/" .. name, data .. "/" .. name)
  end
end

-- An extra omawin adds to config/nvim/lazyvim.json later reaches this PC too: each of the repo's extras
-- is turned on here once, and noted in omawin-extras-offered.txt, so one you then turn off in
-- :LazyExtras stays off. (A PC from before this note gets every missing one back, once.)
pcall(function()
  local offered_file = data .. "/omawin-extras-offered.txt"
  local offered = {}
  for _, name in ipairs(vim.fn.filereadable(offered_file) == 1 and vim.fn.readfile(offered_file) or {}) do
    offered[name] = true
  end
  local new = vim.tbl_filter(function(name)
    return not offered[name]
  end, vim.json.decode(table.concat(vim.fn.readfile(config .. "/lazyvim.json"), "\n")).extras)
  if #new == 0 then
    return
  end
  local live = vim.json.decode(table.concat(vim.fn.readfile(vim.g.lazyvim_json), "\n"))
  local changed = false
  for _, name in ipairs(new) do
    if not vim.tbl_contains(live.extras, name) then
      table.insert(live.extras, name)
      changed = true
    end
  end
  if changed then
    table.sort(live.extras)
    vim.fn.writefile(vim.split(vim.json.encode(live, { indent = "  ", sort_keys = true }), "\n"), vim.g.lazyvim_json)
  end
  vim.fn.writefile(new, offered_file, "a")
end)

-- This account's folder (omawin's docs/work.md: ~/omawin-account, or OMAWIN_USER_LAYER; the
-- older ~/omawin-work until it has moved): its config/nvim/ folder joins the runtimepath, its
-- lua/user/plugins/*.lua are plugin specs loaded after omawin's (add or override plugins,
-- LazyVim's opts), and lua/user/init.lua runs once LazyVim is up.
local user_layer = vim.env.OMAWIN_USER_LAYER
if not user_layer or user_layer == "" then
  user_layer = require("omawin").home .. "/omawin-account" -- Windows' home, from WSL too
  local before = require("omawin").home .. "/omawin-work"
  if vim.fn.isdirectory(user_layer) == 0 and vim.fn.isdirectory(before) == 1 then
    user_layer = before
  end
end
local user_nvim = user_layer .. "/nvim"
local user_spec = {}
local has_user = (vim.uv or vim.loop).fs_stat(user_nvim .. "/lua/user") ~= nil
if has_user then
  vim.opt.rtp:append(user_nvim)
  if (vim.uv or vim.loop).fs_stat(user_nvim .. "/lua/user/plugins") then
    user_spec = { { import = "user.plugins" } }
  end
end

require("lazy").setup({
  lockfile = lockfile,
  spec = vim.list_extend({
    -- add LazyVim and import its plugins
    { "LazyVim/LazyVim", import = "lazyvim.plugins" },
    -- The AI agent's split (config/nvim/lua/plugins/ai.lua). Imported here rather than listed in
    -- lazyvim.json, so it reaches PCs whose lazyvim.json predates it.
    { import = "lazyvim.plugins.extras.ai.sidekick" },
    -- import/override with your plugins
    { import = "plugins" },
  }, user_spec),
  defaults = {
    -- By default, only LazyVim plugins will be lazy-loaded. Your custom plugins will load during startup.
    -- If you know what you're doing, you can set this to `true` to have all your custom plugins lazy-loaded by default.
    lazy = false,
    -- It's recommended to leave version=false for now, since a lot the plugin that support versioning,
    -- have outdated releases, which may break your Neovim install.
    version = false, -- always use the latest git commit
    -- version = "*", -- try installing the latest stable version for plugins that support semver
  },
  install = { colorscheme = { "tokyonight", "habamax" } },
  rocks = { enabled = false }, -- no plugin here needs luarocks
  checker = {
    enabled = true, -- check for plugin updates periodically
    notify = false, -- notify on update
  }, -- automatically check for plugin updates
  performance = {
    rtp = {
      -- lazy.nvim resets the runtimepath; keep the account folder's nvim/ on it
      paths = has_user and { user_nvim } or nil,
      -- disable some rtp plugins
      disabled_plugins = {
        "gzip",
        -- "matchit",
        -- "matchparen",
        "netrwPlugin", -- snacks explorer replaces netrw
        "tarPlugin",
        "tohtml",
        "tutor",
        "zipPlugin",
      },
    },
  },
})

-- The account folder's lua/user/init.lua: on VeryLazy, registered after LazyVim's own handler there
-- (which loads its keymaps), so your options and keymaps win.
if has_user and (vim.uv or vim.loop).fs_stat(user_nvim .. "/lua/user/init.lua") then
  vim.api.nvim_create_autocmd("User", {
    pattern = "VeryLazy",
    once = true,
    callback = function()
      require("user")
    end,
  })
end
