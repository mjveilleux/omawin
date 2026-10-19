-- markdown-preview.nvim (LazyVim's lang.markdown extra) runs a server it downloads when the plugin
-- is installed or updated. A PC that turned the extra on after installing it, or whose download
-- failed, has none and the preview silently does nothing: fetch it on the first preview instead.
return {
  {
    "iamcco/markdown-preview.nvim",
    optional = true,
    config = function(plugin)
      local app = plugin.dir .. "/app"
      if vim.fn.has("win32") == 1 and not (vim.uv or vim.loop).fs_stat(app .. "/bin/markdown-preview-win.exe") then
        vim.notify("Downloading markdown-preview's server…", vim.log.levels.INFO, { title = "markdown-preview" })
        local version = vim.json.decode(table.concat(vim.fn.readfile(plugin.dir .. "/package.json"), "")).version
        local result = vim.system({ "cmd.exe", "/c", app .. "\\install.cmd", "v" .. version }, { cwd = app }):wait(120000)
        if result.code ~= 0 then
          vim.notify("Couldn't download it: " .. (result.stderr or ""), vim.log.levels.ERROR, { title = "markdown-preview" })
        end
      end
      -- what LazyVim's spec does: set up the buffer that triggered the load
      vim.cmd([[do FileType]])
    end,
  },
}
