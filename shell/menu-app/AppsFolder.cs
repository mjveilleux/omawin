using System.Runtime.InteropServices;

namespace SystemMenu;

// Windows' Apps folder (shell:AppsFolder: every app Start lists, desktop and Store alike), item
// by item: its name and the id that starts it (a path, or a Store app's AUMID), as
// Shell.Application's FolderItem.Name and .Path give them. Through IShellItem's vtable, since
// late-bound COM (`dynamic`) can't be compiled as one native program (Native AOT).
static unsafe class AppsFolder
{
    public static List<(string Name, string Id)> Items()
    {
        var items = new List<(string, string)>();
        CoInitializeEx(IntPtr.Zero, 2 /* apartment */); // a no-op where the thread has COM already
        var folderId = new Guid("1e87508d-89c2-42f0-8a7e-645a0f50ca58"); // FOLDERID_AppsFolder
        var shellItem = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"); // IID_IShellItem
        Vtable.Check(SHGetKnownFolderItem(folderId, 0, IntPtr.Zero, shellItem, out var folder));
        IntPtr list = IntPtr.Zero;
        try
        {
            var enumItems = new Guid("94f60519-2850-4924-aa5a-d15e84868039"); // BHID_EnumItems
            var enumIid = new Guid("70629033-e363-4a28-a567-0db78006e6d7"); // IID_IEnumShellItems
            var bind = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, Guid*, IntPtr*, int>)Vtable.Slot(folder, 3);
            Vtable.Check(bind(folder, IntPtr.Zero, &enumItems, &enumIid, &list));
            var next = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, uint*, int>)Vtable.Slot(list, 3);
            while (true)
            {
                IntPtr item; uint fetched;
                if (next(list, 1, &item, &fetched) != 0 || fetched == 0) break;
                try
                {
                    var name = DisplayName(item, 0); // SIGDN_NORMALDISPLAY
                    var id = DisplayName(item, unchecked((int)0x80018001)); // SIGDN_PARENTRELATIVEPARSING
                    if (name != null && id != null) items.Add((name, id));
                }
                finally { Marshal.Release(item); }
            }
        }
        finally
        {
            if (list != IntPtr.Zero) Marshal.Release(list);
            Marshal.Release(folder);
        }
        return items;
    }

    static string? DisplayName(IntPtr item, int kind)
    {
        IntPtr text;
        var get = (delegate* unmanaged[Stdcall]<IntPtr, int, IntPtr*, int>)Vtable.Slot(item, 5);
        if (get(item, kind, &text) < 0) return null;
        try { return Marshal.PtrToStringUni(text); }
        finally { Marshal.FreeCoTaskMem(text); }
    }

    [DllImport("shell32.dll")] static extern int SHGetKnownFolderItem(in Guid folderId, uint flags, IntPtr token, in Guid iid, out IntPtr item);
    [DllImport("ole32.dll")] static extern int CoInitializeEx(IntPtr reserved, uint flags);
}
