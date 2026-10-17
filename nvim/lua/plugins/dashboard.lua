-- Start screen: custom art header (lua/dashboard_art.lua) in place of the LazyVim logo.
-- The shortcuts are LazyVim's own (f n p g r c s x l q), each in its own color, taken from
-- the colorscheme's syntax colors so it follows the theme.
-- On wide windows the art becomes a panorama across two columns, with Recent Files and
-- Projects in the second column next to the shortcuts.
local art = require("dashboard_art")

-- one color per shortcut icon: the colorscheme's own, a rainbow of its syntax groups
local icon_colors = { "Keyword", "Statement", "Function", "Special", "Type", "String", "Constant", "DiagnosticWarn", "DiagnosticError" }

local function set_highlights()
  vim.api.nvim_set_hl(0, "DashDesc", { link = "Normal" })
  for i, group in ipairs(icon_colors) do
    local ok, h = pcall(vim.api.nvim_get_hl, 0, { name = group, link = false })
    local color = ok and h and h.fg or nil
    vim.api.nvim_set_hl(0, "DashIcon" .. i, { fg = color })
    vim.api.nvim_set_hl(0, "DashKey" .. i, { fg = color, bold = true })
  end
end

local function wide()
  return art.wide -- decided by the header on each render, from the window size
end

return {
  "folke/snacks.nvim",
  opts = function(_, opts)
    set_highlights()
    vim.api.nvim_create_autocmd("ColorScheme", { callback = set_highlights })

    local dashboard = opts.dashboard or {}
    opts.dashboard = dashboard
    dashboard.width = art.pane_width
    dashboard.pane_gap = 0 -- the panorama's two halves meet without a seam

    -- icon color by the shortcut's position in LazyVim's key list
    local position = {}
    for i, item in ipairs(dashboard.preset and dashboard.preset.keys or {}) do
      position[item.key] = i
    end
    local function color(item, prefix, fallback)
      local i = position[item.key]
      return i and (prefix .. ((i - 1) % #icon_colors + 1)) or fallback
    end

    dashboard.formats = vim.tbl_extend("force", dashboard.formats or {}, {
      key = function(item)
        return { item.key, hl = color(item, "DashKey", "key") }
      end,
      desc = function(item)
        return { item.desc, hl = "DashDesc" }
      end,
      icon = function(item)
        if item.file and (item.icon == "file" or item.icon == "directory") then
          return Snacks.dashboard.icon(item.file, item.icon)
        end
        return { item.icon, width = 2, hl = color(item, "DashIcon", "icon") }
      end,
    })

    dashboard.sections = {
      art.section(1),
      art.section(2), -- right half of the panorama (nothing on narrow windows)
      { section = "keys", gap = 0, padding = 1, indent = 6 },
      { section = "startup" },

      { pane = 2, icon = " ", title = "Recent Files", indent = 4, enabled = wide },
      { pane = 2, section = "recent_files", limit = 5, indent = 6, padding = 1, enabled = wide },
      { pane = 2, icon = " ", title = "Projects", indent = 4, enabled = wide },
      { pane = 2, section = "projects", limit = 4, indent = 6, padding = 1, enabled = wide },
    }
  end,
}
