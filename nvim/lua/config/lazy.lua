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

-- Your user layer (omawin's docs/work.md: ~/omawin-work, or OMAWIN_USER_LAYER): its nvim/ folder
-- joins the runtimepath, its lua/user/plugins/*.lua are plugin specs loaded after omawin's (add
-- or override plugins, LazyVim's opts), and lua/user/init.lua runs once LazyVim is up.
local user_layer = vim.env.OMAWIN_USER_LAYER
if not user_layer or user_layer == "" then
  user_layer = vim.fn.expand("~") .. "/omawin-work"
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
      -- lazy.nvim resets the runtimepath; keep the user layer's nvim/ on it
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

-- The user layer's lua/user/init.lua: on VeryLazy, registered after LazyVim's own handler there
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
