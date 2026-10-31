// Windows' own folder picker (IFileOpenDialog with FOS_PICKFOLDERS), for a setting that acts on a
// folder you choose (developer.case-sensitive). COM by vtable slot, as AppIcons.cs and Wmi.cs do
// (no [ComImport] under Native AOT), on a thread of its own: the dialog needs a single-threaded
// apartment, and settings are set from the thread pool.

using System.Runtime.InteropServices;

namespace SystemMenu;

static unsafe partial class FolderPicker
{
    static readonly Guid ClsidFileOpenDialog = new("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");
    static readonly Guid IidFileOpenDialog = new("D57C7288-D4AD-4768-BE02-9D969532D960");
    const uint CLSCTX_INPROC_SERVER = 0x1;
    const uint FOS_PICKFOLDERS = 0x20, FOS_FORCEFILESYSTEM = 0x40, FOS_PATHMUSTEXIST = 0x800;
    const uint SIGDN_FILESYSPATH = 0x80058000;

    // IFileOpenDialog : IFileDialog : IModalWindow : IUnknown, slots as ShObjIdl_core.h lists them.
    const int Show = 3, SetOptions = 9, GetOptions = 10, SetTitle = 17, GetResult = 20;
    // IShellItem : IUnknown.
    const int GetDisplayName = 5;

    [LibraryImport("ole32.dll")] private static partial int CoCreateInstance(Guid* clsid, nint outer, uint context, Guid* iid, nint* obj);
    [LibraryImport("ole32.dll")] private static partial void CoTaskMemFree(nint p);

    // The folder's path, or null when the dialog was closed without one.
    public static string? Pick(string title)
    {
        string? chosen = null;
        var thread = new Thread(() =>
        {
            try { chosen = PickHere(title); }
            catch (Exception ex) { App.Log(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return chosen;
    }

    static nint Slot(nint com, int index) => (*(nint**)com)[index];

    static string? PickHere(string title)
    {
        var clsid = ClsidFileOpenDialog;
        var iid = IidFileOpenDialog;
        nint dialog;
        if (CoCreateInstance(&clsid, 0, CLSCTX_INPROC_SERVER, &iid, &dialog) < 0 || dialog == 0) return null;
        try
        {
            uint options = 0;
            ((delegate* unmanaged[Stdcall]<nint, uint*, int>)Slot(dialog, GetOptions))(dialog, &options);
            ((delegate* unmanaged[Stdcall]<nint, uint, int>)Slot(dialog, SetOptions))(dialog, options | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST);
            fixed (char* t = title) ((delegate* unmanaged[Stdcall]<nint, char*, int>)Slot(dialog, SetTitle))(dialog, t);
            // No owner window: the menu is hidden by the time a setting runs. Cancelled: an error HRESULT.
            if (((delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(dialog, Show))(dialog, 0) < 0) return null;
            nint item;
            if (((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(dialog, GetResult))(dialog, &item) < 0 || item == 0) return null;
            try
            {
                char* name;
                if (((delegate* unmanaged[Stdcall]<nint, uint, char**, int>)Slot(item, GetDisplayName))(item, SIGDN_FILESYSPATH, &name) < 0) return null;
                try { return new string(name); }
                finally { CoTaskMemFree((nint)name); }
            }
            finally { Marshal.Release(item); }
        }
        finally { Marshal.Release(dialog); }
    }
}
