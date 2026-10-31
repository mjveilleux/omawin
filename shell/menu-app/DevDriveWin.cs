// The PC's side of `omawin dev-drive` (DevDrive.cs, roadmap 8.32): the drives in use, and making
// the Dev Drive in the one elevated run (bin/settings-admin.ps1's devdrive verb).

namespace SystemMenu;

sealed class DevDriveWin : IDevDriveHost
{
    public static readonly DevDriveWin Instance = new();

    public int Build => WinSettings.Build;

    public IReadOnlyList<(char Letter, bool ReFS)> Volumes() => DriveInfo.GetDrives().Select(d =>
    {
        bool refs = false;
        try { refs = d.IsReady && d.DriveFormat.Equals("ReFS", StringComparison.OrdinalIgnoreCase); }
        catch (IOException) { } // a card reader with no card, a network drive that's gone
        return (char.ToUpperInvariant(d.Name[0]), refs);
    }).ToList();

    public async Task<(bool Ran, string? Error)> CreateAsync(string vhdx, int gb, char letter)
    {
        var run = await WinSettings.RunElevated([string.Join('\t', "dev-drive", "devdrive", vhdx, gb.ToString(), letter.ToString())]);
        return (run.Ran, run.Failures.FirstOrDefault());
    }

    public void MakeFolder(string path) => Directory.CreateDirectory(path);
}
