# Where do you turn the music plugin on, until the plugin selector exists?

**The question:** the music player is now a Quickshell plugin (`shell/plugins/omawin.music/`), and you asked for it to move out of the presets into the plugin selector. The plugin selector (`omawin plugin`, Install › Plugins: roadmap 4.5) isn't built yet, and neither is the QML host that runs the plugin on Windows (4.9.4).

**Options:**

1. Keep the `music` preset as the way to install the plugin's programs (yt-dlp, mpv, the .NET runtime) until the selector lands, then move it there with a migration that keeps it on for PCs that had the preset.
2. Build `omawin plugin list|add|remove` now (C#, in the command), with the music plugin as its first entry, even though the plugin can't show on Windows until the QML host exists.
3. Drop the preset now and leave the programs to be installed by hand until the selector lands.

**Default (what's done):** 1. The preset is relabelled "the Music plugin's programs"; nothing changes for a PC that has it. Delete this file once answered.
