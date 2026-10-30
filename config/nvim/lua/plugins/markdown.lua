-- markdown-preview.nvim (LazyVim's lang.markdown extra) runs a server it downloads when the plugin
-- is installed or updated. A PC that turned the extra on after installing it, or whose download
-- failed, has none and the preview silently does nothing: fetch it on the first preview instead.
local windows = vim.fn.has("win32") == 1

return {
  {
    "iamcco/markdown-preview.nvim",
    optional = true,
    init = function()
      -- In WSL, open the preview in Windows' browser (vim.ui.open goes through explorer.exe).
      if vim.fn.has("wsl") == 1 then
        vim.cmd([[
          function! OmawinMarkdownPreviewOpen(url)
            call v:lua.vim.ui.open(a:url)
          endfunction
        ]])
        vim.g.mkdp_browserfunc = "OmawinMarkdownPreviewOpen"
      end
    end,
    config = function(plugin)
      local app = plugin.dir .. "/app"
      local server = app .. (windows and "/bin/markdown-preview-win.exe" or "/bin/markdown-preview-linux")
      if not (vim.uv or vim.loop).fs_stat(server) then
        vim.notify("Downloading markdown-preview's server…", vim.log.levels.INFO, { title = "markdown-preview" })
        local version = "v" .. vim.json.decode(table.concat(vim.fn.readfile(plugin.dir .. "/package.json"), "")).version
        local cmd = windows and { "cmd.exe", "/c", app .. "\\install.cmd", version } or { "bash", app .. "/install.sh", version }
        local result = vim.system(cmd, { cwd = app }):wait(120000)
        if result.code ~= 0 then
          vim.notify("Couldn't download it: " .. (result.stderr or ""), vim.log.levels.ERROR, { title = "markdown-preview" })
        end
      end
      -- what LazyVim's spec does: set up the buffer that triggered the load
      vim.cmd([[do FileType]])
    end,
  },
}
