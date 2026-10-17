-- Dashboard header: stars, an aurora and a two-layer snow-capped mountain range in the
-- colorscheme's own colors (whatever theme is on), drawn procedurally with block characters. The near range is
-- layered over the far one by giving partial blocks a background color, and the aurora
-- uses half blocks for twice the vertical resolution. Deterministic: same picture every
-- start for a given window size.
--
-- Two layouts, picked from the dashboard window size on every render:
--   * narrow: a 60-column scene over the single column of shortcuts
--   * wide:   a 120-column panorama split across the two dashboard panes (pane_gap = 0,
--             so the halves meet seamlessly) over shortcuts | recent files
-- and the scene grows or shrinks with the window height so the shortcuts always fit.
local M = {}

M.pane_width = 60 -- dashboard `width`: one pane
M.wide = false -- set on each render; plugins/dashboard.lua shows pane 2 only when true

-- The scene's colors, filled in from the colorscheme on every render (refresh_palette below).
local palette = {}

-- Mountain ranges, as { column, height in rows, slope } for a 7-row tall range; they scale
-- with the number of rows. The panorama is a longer range with mountain-ish in the middle.
local ranges = {
  [60] = {
    far = { { 10, 5.0, 0.42 }, { 29, 6.6, 0.48 }, { 44, 5.5, 0.45 }, { 57, 4.3, 0.4 } },
    near = { { 4, 2.6, 0.35 }, { 21, 4.2, 0.38 }, { 36, 3.0, 0.3 }, { 51, 3.7, 0.4 } },
  },
  [120] = {
    far = {
      { 8, 4.6, 0.40 }, { 24, 5.8, 0.45 }, { 41, 5.0, 0.42 }, { 60, 6.8, 0.42 },
      { 79, 5.4, 0.44 }, { 96, 6.0, 0.45 }, { 113, 4.6, 0.40 },
    },
    near = {
      { 3, 2.4, 0.33 }, { 17, 3.6, 0.36 }, { 33, 2.8, 0.30 }, { 50, 4.0, 0.38 },
      { 70, 3.2, 0.34 }, { 86, 4.2, 0.37 }, { 103, 3.0, 0.32 }, { 117, 2.6, 0.35 },
    },
  },
}

-- Foreground: fir trees as { column, height in pixels (2 per row) for a 7-row range } and
-- bunnies as { column, facing }. Kept sparse on purpose.
local foreground = {
  [60] = {
    trees = { { 3, 9 }, { 6, 12 }, { 9, 7 }, { 50, 8 }, { 53, 13 }, { 56, 9 } },
    bunnies = { { 15, "right" }, { 19, "left" } },
  },
  [120] = {
    trees = {
      { 4, 9 }, { 7, 13 }, { 10, 8 }, { 40, 7 }, { 43, 10 },
      { 98, 8 }, { 102, 12 }, { 105, 14 }, { 108, 9 }, { 116, 7 },
    },
    bunnies = { { 18, "right" }, { 22, "left" }, { 88, "left" } },
  },
}

-- A bunny facing right, 6x4 pixels (ears, head, body with a tail).
local bunny = {
  "...X.X",
  "...XXX",
  "XXXXX.",
  ".XXXX.",
}

-- Scene sizes, largest first; the first that fits the window height wins. BELOW is the
-- rows needed under the art: shortcuts (~10), startup line and paddings.
local BELOW = 15
local sizes = {
  { name = "tall", sky = 2, aurora = 6, mountains = 9 },
  { name = "full", sky = 1, aurora = 4, mountains = 7 },
  { name = "compact", sky = 0, aurora = 0, mountains = 7 },
  { name = "minimal", sky = 0, aurora = 0, mountains = 0 },
}

-- color helpers -------------------------------------------------------------------------
local function rgb(hex)
  return tonumber(hex:sub(2, 3), 16), tonumber(hex:sub(4, 5), 16), tonumber(hex:sub(6, 7), 16)
end

local function blend(a, b, t)
  local r1, g1, b1 = rgb(a)
  local r2, g2, b2 = rgb(b)
  local function mix(x, y)
    return math.floor(x + (y - x) * t + 0.5)
  end
  return string.format("#%02x%02x%02x", mix(r1, r2), mix(g1, g2), mix(b1, b2))
end

local function gradient(stops, t) -- t in [0, 1] across a list of colors
  t = math.max(0, math.min(1, t)) * (#stops - 1)
  local i = math.min(math.floor(t) + 1, #stops - 1)
  return blend(stops[i], stops[i + 1], t - (i - 1))
end

-- The colorscheme's colors, so the scene follows the theme: the background (the aurora
-- fades into it), the text and comment colors, and four syntax colors for the aurora, the
-- far range and the firs. A transparent background (Neovim in the terminal) borrows the
-- floating windows', menus' or status line's; only a colorscheme with none of them gets plain
-- black or white (by 'background'), never another theme's colors.
local function hl_color(group, attr)
  local ok, h = pcall(vim.api.nvim_get_hl, 0, { name = group, link = false })
  local v = ok and h and h[attr or "fg"]
  return v and string.format("#%06x", v) or nil
end

local function refresh_palette()
  local light = vim.o.background == "light"
  local bg = hl_color("Normal", "bg") or hl_color("NormalFloat", "bg") or hl_color("Pmenu", "bg")
    or hl_color("StatusLine", "bg") or (light and "#ffffff" or "#000000")
  local fg = hl_color("Normal") or hl_color("NormalFloat") or hl_color("Identifier")
    or (light and "#000000" or "#ffffff")
  local dim = hl_color("Comment") or blend(bg, fg, 0.4)
  local a = hl_color("Keyword") or hl_color("Statement") or fg
  local b = hl_color("Function") or fg
  local c = hl_color("Type") or hl_color("Special") or fg
  local green = hl_color("String") or fg
  palette.sky = bg
  palette.star = fg
  palette.star_dim = dim
  palette.aurora = { a, b, c, green } -- left to right
  palette.far = { blend(bg, dim, 0.25), blend(bg, dim, 0.45) } -- bottom row -> top row
  palette.far_snow = blend(dim, fg, 0.35)
  palette.near = { blend(bg, fg, 0.08), blend(bg, b, 0.45) }
  palette.near_snow = fg
  palette.ground = blend(bg, dim, 0.45)
  palette.firs = { blend(bg, green, 0.45), blend(bg, green, 0.6) } -- two greens, for depth
  palette.bunny = fg
  palette.sub = dim
end

-- Highlight groups are made on demand, one per fg/bg pair, and (re)applied on every
-- render (a colorscheme change clears them).
local groups = {}
local function hl(fg, bg, bold)
  local name = "DashArt" .. fg:sub(2) .. (bg and bg:sub(2) or "") .. (bold and "b" or "")
  groups[name] = { fg = fg, bg = bg, bold = bold }
  return name
end

-- the scene ---------------------------------------------------------------------------
local function hash(x, y)
  local n = math.sin(x * 12.9898 + y * 78.233) * 43758.5453
  return n - math.floor(n)
end

local blocks = { "▁", "▂", "▃", "▄", "▅", "▆", "▇", "█" }
local stars = { "·", "✦", "˚", "·", "+" }

--- Layers of a scene `w` columns wide: functions (x, row) -> char, highlight.
local function scene(w, size)
  refresh_palette()
  local s = {}
  local range = ranges[w]
  local scale = size.mountains / 7
  local ascale = size.aurora / 4

  function s.sky(x, y)
    local r = hash(x, y)
    if r < 0.04 then
      local ch = stars[math.floor(hash(y, x) * #stars) + 1]
      return ch, hl(r < 0.015 and palette.star or palette.star_dim)
    end
    return " ", nil
  end

  -- aurora color at column x and height y (rows from its top, fractional), or nil
  local function aurora_at(x, y)
    -- a ribbon that wanders up and down, with faint vertical rays, fading at both sides
    local center = ascale * (2.1 + 1.0 * math.sin(x * 0.085 + 0.4) + 0.25 * math.sin(x * 0.23))
    local d = y - center
    local spread = (d < 0 and 1.5 or 0.9) * ascale -- trails off upward, sharper lower edge
    local edge = math.min(1, x / 10, (w + 1 - x) / 10)
    local rays = 0.8 + 0.2 * math.sin(x * 1.7) * math.sin(x * 0.5 + 1)
    local glow = math.max(0, 1 - math.abs(d) / spread) * (0.7 + 0.3 * math.sin(x * 0.17 + 2.0)) * edge * rays
    if glow < 0.08 then
      return nil
    end
    glow = math.floor(glow * 10 + 0.5) / 10 -- fewer distinct highlight groups
    return blend(palette.sky, gradient(palette.aurora, (x - 1) / (w - 1)), math.min(1, glow ^ 0.8 * 1.15))
  end

  function s.aurora(x, row)
    -- half blocks: each cell shows two vertical samples (fg = top half, bg = bottom half)
    local top, bottom = aurora_at(x, row - 0.75), aurora_at(x, row - 0.25)
    if top and bottom then
      return "▀", hl(top, bottom)
    elseif top then
      return "▀", hl(top)
    elseif bottom then
      return "▄", hl(bottom)
    end
    return s.sky(x, row + 10)
  end

  local function ridge(peaks, base, x, seed)
    local h = base
    for _, p in ipairs(peaks) do
      h = math.max(h, (p[2] - p[3] * math.abs(x - p[1])) * scale)
    end
    return h + 0.12 * math.sin(x * 1.1 + seed) + 0.05 * math.sin(x * 2.7 + seed * 2)
  end

  local function coverage(h, below) -- eighths of this cell filled by a layer of height h
    return math.floor(math.max(0, math.min(1, h - below)) * 8 + 0.5)
  end

  -- both ranges at a cell: how much each fills it (eighths) and its color there
  local function ranges_at(x, row)
    local rows = size.mountains
    local below = rows - row -- full rows underneath this cell
    local far_h, near_h = ridge(range.far, 1.4 * scale, x, 0.7), ridge(range.near, 0.7 * scale, x, 2.3)
    local t = below / (rows - 1)
    local snowline = 0.35 * math.sin(x * 0.9)
    local far_c = below >= 4.2 * scale + snowline and palette.far_snow or gradient(palette.far, t)
    local near_c = below >= 2.5 * scale + snowline and palette.near_snow or gradient(palette.near, t * 1.6)
    return coverage(far_h, below), coverage(near_h, below), far_c, near_c
  end

  -- the color filling a whole cell of the ranges, or nil where it's sky or a ridge edge
  local function behind(x, row)
    local far_e, near_e, far_c, near_c = ranges_at(x, row)
    if near_e == 8 then
      return near_c
    elseif near_e == 0 and far_e == 8 then
      return far_c
    end
  end

  -- Foreground pixel layer (2x2 pixels per cell via quadrant blocks): fir trees and bunnies
  -- standing on the ground line. Pixel (0, 0) is the top-left of the mountain area.
  local fg = foreground[w]
  local ground = 2 * size.mountains -- first pixel row below the mountains
  local function pixel(px, py)
    for _, b in ipairs(fg.bunnies) do
      local bx, by = px - 2 * (b[1] - 1), py - (ground - #bunny)
      local row = bunny[by + 1]
      if row and bx >= 0 and bx < #row then
        local i = b[2] == "left" and (#row - bx) or (bx + 1)
        if row:sub(i, i) == "X" then
          return palette.bunny
        end
      end
    end
    for i, t in ipairs(fg.trees) do
      local h = math.max(5, math.floor(t[2] * scale + 0.5)) -- tree height in pixels
      local y = py - (ground - h) -- 0 at the tip
      if y >= 0 and y < h then
        local tiers = math.max(2, math.floor(h / 4))
        local tier = h / tiers
        local p = (y + 1) / h
        local hw = 0.5 + p * h * 0.5 * (0.5 + 0.5 * ((y % tier) / tier)) -- jagged fir tiers
        if y == h - 1 and h >= 8 then
          hw = 0.5 -- a bit of trunk
        end
        if math.abs(px + 0.5 - (2 * t[1] - 1)) <= hw then
          return palette.firs[(i % #palette.firs) + 1]
        end
      end
    end
  end

  local quadrants = {
    [0] = " ", "▗", "▖", "▄", "▝", "▐", "▞", "▟", "▘", "▚", "▌", "▙", "▀", "▜", "▛", "█",
  }
  local function foreground_cell(x, row)
    local px, py = 2 * (x - 1), 2 * (row - 1)
    local tl, tr, bl, br = pixel(px, py), pixel(px + 1, py), pixel(px, py + 1), pixel(px + 1, py + 1)
    local color = tl or tr or bl or br
    if not color then
      return nil
    end
    -- one color per cell: bunnies win over trees
    for _, c in ipairs({ tl, tr, bl, br }) do
      if c == palette.bunny then
        color = c
      end
    end
    local bits = (tl == color and 8 or 0) + (tr == color and 4 or 0) + (bl == color and 2 or 0) + (br == color and 1 or 0)
    return quadrants[bits], hl(color, bits < 15 and behind(x, row) or nil)
  end

  function s.mountains(x, row) -- row 1..size.mountains, top to bottom
    local ch, group = foreground_cell(x, row)
    if ch then
      return ch, group
    end
    local far_e, near_e, far_c, near_c = ranges_at(x, row)

    if near_e == 8 then
      return "█", hl(near_c)
    elseif near_e > 0 then
      -- near ridge top in front of the far range (or the sky)
      return blocks[near_e], hl(near_c, far_e > near_e and far_c or nil)
    elseif far_e > 0 then
      return blocks[far_e], hl(far_c)
    end
    return s.sky(x, row + 20)
  end

  return s
end

-- lines ---------------------------------------------------------------------------------
-- A line is a list of single-width cells { char, highlight }, so it can be split cleanly.
local function draw(w, cell, row)
  local cells = {}
  for x = 1, w do
    local ch, group = cell(x, row)
    cells[x] = { ch, group }
  end
  return cells
end

local function text_cells(w, parts) -- centered colored text
  local cells = {}
  for _, p in ipairs(parts) do
    for _, ch in ipairs(vim.fn.split(p[1], "\\zs")) do
      cells[#cells + 1] = { ch, p[2] }
    end
  end
  local left = math.floor((w - #cells) / 2)
  local out = {}
  for x = 1, w do
    out[x] = cells[x - left] or { " " }
  end
  return out
end

-- "neovim" in a thin box-drawing font, each letter a step along the aurora gradient
local letters = {
  { "┏┓╻", "┃┗┫", "╹ ╹" },
  { "┏━╸", "┣╸ ", "┗━╸" },
  { "┏━┓", "┃ ┃", "┗━┛" },
  { "╻ ╻", "┃┏┛", "┗┛ " },
  { "╻", "┃", "╹" },
  { "┏┳┓", "┃┃┃", "╹ ╹" },
}

local function wordmark(w, row)
  local parts = {}
  for i, l in ipairs(letters) do
    local color = gradient(palette.aurora, (i - 1) / (#letters - 1))
    parts[#parts + 1] = { l[row] .. (i < #letters and " " or ""), hl(color, nil, true) }
  end
  return text_cells(w, parts)
end

local function subtitle(w)
  local v = vim.version()
  local text = string.format("v%d.%d.%d  ·  %s", v.major, v.minor, v.patch, os.date("%A, %B %d"))
  return text_cells(w, { { text, hl(palette.sub) } })
end

local function art_rows(size)
  local mountains = size.mountains > 0 and (size.mountains + 2) or 0 -- + ground and gap
  return size.sky + size.aurora + mountains + 5 -- + wordmark, subtitle, gap
end

local function pick_size(height)
  for _, size in ipairs(sizes) do
    if height >= art_rows(size) + BELOW then
      return size
    end
  end
  return sizes[#sizes]
end

--- All lines of the header for a scene `w` columns wide and the given window height.
local function lines(w, height)
  local size = pick_size(height)
  local s = scene(w, size)
  local out = {}
  for r = 1, size.sky do
    out[#out + 1] = draw(w, s.sky, r)
  end
  for r = 1, size.aurora do
    out[#out + 1] = draw(w, s.aurora, r)
  end
  if size.mountains > 0 then
    for r = 1, size.mountains do
      out[#out + 1] = draw(w, s.mountains, r)
    end
    out[#out + 1] = text_cells(w, { { ("▔"):rep(w), hl(palette.ground) } })
    out[#out + 1] = text_cells(w, {})
  end
  for r = 1, 3 do
    out[#out + 1] = wordmark(w, r)
  end
  out[#out + 1] = subtitle(w)
  out[#out + 1] = text_cells(w, {}) -- breathing room above the shortcuts
  return out
end

--- Cells from..to of a line, as snacks text chunks (neighbors sharing a highlight merged).
local function chunks(cells, from, to)
  local out = {}
  for x = from, to do
    local ch, group = cells[x][1], cells[x][2] or "SnacksDashboardNormal"
    local last = out[#out]
    if last and last.hl == group then
      last[1] = last[1] .. ch
    else
      out[#out + 1] = { ch, hl = group }
    end
  end
  return out
end

--- Snacks dashboard section for one pane of the header. A function of the dashboard, so it
--- is rebuilt on every render and follows the window size.
---@param pane 1|2
function M.section(pane)
  return function(self)
    local size = self._size or { width = vim.o.columns, height = vim.o.lines }
    if pane == 1 then
      M.wide = size.width >= 2 * M.pane_width + 4
    end
    if pane == 2 and not M.wide then
      return {}
    end
    local w = M.wide and 2 * M.pane_width or M.pane_width
    local from = pane == 2 and M.pane_width + 1 or 1
    local items = {}
    for _, cells in ipairs(lines(w, size.height)) do
      items[#items + 1] = { text = chunks(cells, from, from + M.pane_width - 1), pane = pane }
    end
    for name, spec in pairs(groups) do
      vim.api.nvim_set_hl(0, name, spec)
    end
    return items
  end
end

return M
