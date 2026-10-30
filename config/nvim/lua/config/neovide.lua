-- Neovide: the same Neovim config as in the terminal (plugins, keys, theme), plus what only a
-- GUI has: font, padding, a little translucency, cursor and scroll animation. Its own
-- settings (no title bar, ...) are in ~/omawin/config/neovide/config.toml.
if not vim.g.neovide then
  return
end

-- The terminal's font (Windows Terminal: JetBrainsMono NF); Ctrl+= / Ctrl+- / Ctrl+0 zoom.
vim.o.guifont = "JetBrainsMono NF:h11"
vim.g.neovide_scale_factor = 1.0
local function zoom(by)
  vim.g.neovide_scale_factor = by and vim.g.neovide_scale_factor * by or 1.0
end
vim.keymap.set({ "n", "i", "v" }, "<C-=>", function() zoom(1.1) end, { desc = "Zoom in" })
vim.keymap.set({ "n", "i", "v" }, "<C-->", function() zoom(1 / 1.1) end, { desc = "Zoom out" })
vim.keymap.set({ "n", "i", "v" }, "<C-0>", function() zoom() end, { desc = "Reset zoom" })

-- Room around the text, like the terminal's padding.
vim.g.neovide_padding_top = 10
vim.g.neovide_padding_bottom = 6
vim.g.neovide_padding_left = 12
vim.g.neovide_padding_right = 12

-- The wallpaper shows through a little, as it does behind Windows Terminal.
vim.g.neovide_opacity = 0.96
vim.g.neovide_normal_opacity = 0.96

-- Quick, quiet motion.
vim.g.neovide_cursor_animation_length = 0.08
vim.g.neovide_cursor_trail_size = 0.5
vim.g.neovide_cursor_vfx_mode = ""
vim.g.neovide_scroll_animation_length = 0.2
vim.g.neovide_position_animation_length = 0.12
vim.g.neovide_hide_mouse_when_typing = true
vim.g.neovide_remember_window_size = true

-- Paste with Ctrl+V, as anywhere else on Windows (Neovide has no terminal to do it).
vim.keymap.set({ "i", "c" }, "<C-v>", "<C-r>+", { desc = "Paste" })
vim.keymap.set("t", "<C-v>", [[<C-\><C-n>"+pi]], { desc = "Paste" })
