using System.Runtime.InteropServices;

namespace SystemMenu;

// The files selected in an Explorer window, through Explorer's own automation (roadmap 8.4): the
// ShellWindows list → each window's top-level browser (IServiceProvider, SID_STopLevelBrowser)
// → its active view (IShellBrowser::QueryActiveShellView) → IFolderView2::GetSelection → paths.
// The view is matched by its window (SHELLDLL_DefView, the parent of the file list that had the
// focus), so with Windows 11's tabs it's the tab you were in. Every call goes to Explorer's
// process, so this runs on QuickLook's own thread (single-threaded apartment), never in the hook.
// COM by vtable slot, slots as ShObjIdl_core.h / ExDisp.h list the methods (IUnknown's 3 first).
static unsafe partial class ExplorerSelection
{
    static readonly Guid ClsidShellWindows = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    static readonly Guid IidShellWindows = new("85CB6900-4D95-11CF-960C-0080C7F4EE85");
    static readonly Guid IidServiceProvider = new("6D5140C1-7436-11CE-8034-00AA006009FA");
    static readonly Guid SidTopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    static readonly Guid IidShellBrowser = new("000214E2-0000-0000-C000-000000000046");
    static readonly Guid IidFolderView2 = new("1AF3A467-214F-4298-908E-06B03E0B39F9");
    const uint CLSCTX_ALL = 0x17, SIGDN_FILESYSPATH = 0x80058000, GA_ROOT = 2;

    // IShellWindows : IDispatch (4 slots after IUnknown's).
    const int ShellWindowsCount = 7, ShellWindowsItem = 8;
    // IShellBrowser : IOleWindow; IShellView : IOleWindow.
    const int OleWindowGetWindow = 3, QueryActiveShellView = 15;
    // IFolderView2 : IFolderView (3-16), then its own; GetSelection(BOOL fNoneImpliesFolder, IShellItemArray**).
    const int FolderViewGetSelection = 32;
    const int ItemArrayGetCount = 7, ItemArrayGetItemAt = 8, ShellItemGetDisplayName = 5;

    // VARIANT: 24 bytes on 64-bit (16 on 32-bit, which omawin doesn't ship). Passed by value; the
    // runtime passes it as the platform's calling convention says.
    [StructLayout(LayoutKind.Sequential)] struct VARIANT { public ushort vt; ushort r1, r2, r3; public nint a, b; }

    [LibraryImport("ole32.dll")] private static partial int CoCreateInstance(Guid* clsid, nint outer, uint context, Guid* iid, nint* obj);
    [LibraryImport("ole32.dll")] private static partial void CoTaskMemFree(nint p);
    [LibraryImport("user32.dll")] private static partial nint GetAncestor(nint hwnd, uint flags);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool IsWindowVisible(nint hwnd);

    static nint Slot(nint com, int index) => (*(nint**)com)[index];

    static nint Query(nint unknown, Guid iid)
    {
        nint result;
        return ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Slot(unknown, 0))(unknown, &iid, &result) >= 0 ? result : 0;
    }

    static void Release(nint p) { if (p != 0) Marshal.Release(p); }

    // The selected files and folders, in Explorer's order, of the view whose window is `view`
    // (or, without one, of the visible view in the top-level window `explorer`). Empty when
    // nothing is selected or the window isn't Explorer's. Call on an STA thread.
    public static List<string> Of(nint explorer, nint view)
    {
        var paths = new List<string>();
        var clsid = ClsidShellWindows;
        var iid = IidShellWindows;
        nint windows;
        if (CoCreateInstance(&clsid, 0, CLSCTX_ALL, &iid, &windows) < 0 || windows == 0) return paths;
        try
        {
            int count;
            if (((delegate* unmanaged[Stdcall]<nint, int*, int>)Slot(windows, ShellWindowsCount))(windows, &count) < 0) return paths;
            for (int i = 0; i < count; i++)
            {
                nint item = 0, provider = 0, browser = 0, shellView = 0, folderView = 0, array = 0;
                try
                {
                    var index = new VARIANT { vt = 3 /* VT_I4 */, a = i };
                    // S_FALSE (1) with no window: a slot that closed while we counted.
                    if (((delegate* unmanaged[Stdcall]<nint, VARIANT, nint*, int>)Slot(windows, ShellWindowsItem))(windows, index, &item) != 0 || item == 0) continue;
                    if ((provider = Query(item, IidServiceProvider)) == 0) continue;
                    var sid = SidTopLevelBrowser;
                    var browserIid = IidShellBrowser;
                    if (((delegate* unmanaged[Stdcall]<nint, Guid*, Guid*, nint*, int>)Slot(provider, 3))(provider, &sid, &browserIid, &browser) < 0 || browser == 0) continue;
                    if (((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(browser, QueryActiveShellView))(browser, &shellView) < 0 || shellView == 0) continue;
                    nint viewWindow;
                    if (((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(shellView, OleWindowGetWindow))(shellView, &viewWindow) < 0) continue;
                    bool match = view != 0 ? viewWindow == view : GetAncestor(viewWindow, GA_ROOT) == explorer && IsWindowVisible(viewWindow);
                    if (!match) continue;

                    if ((folderView = Query(shellView, IidFolderView2)) == 0) break;
                    if (((delegate* unmanaged[Stdcall]<nint, int, nint*, int>)Slot(folderView, FolderViewGetSelection))(folderView, 0, &array) < 0 || array == 0) break;
                    uint n;
                    if (((delegate* unmanaged[Stdcall]<nint, uint*, int>)Slot(array, ItemArrayGetCount))(array, &n) < 0) break;
                    for (uint k = 0; k < n; k++)
                    {
                        nint shellItem;
                        if (((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Slot(array, ItemArrayGetItemAt))(array, k, &shellItem) < 0 || shellItem == 0) continue;
                        try
                        {
                            // Only things with a path: a library or This PC's drives list has none.
                            char* name;
                            if (((delegate* unmanaged[Stdcall]<nint, uint, char**, int>)Slot(shellItem, ShellItemGetDisplayName))(shellItem, SIGDN_FILESYSPATH, &name) >= 0 && name != null)
                            {
                                paths.Add(new string(name));
                                CoTaskMemFree((nint)name);
                            }
                        }
                        finally { Release(shellItem); }
                    }
                    break; // the window's found
                }
                finally { Release(array); Release(folderView); Release(shellView); Release(browser); Release(provider); Release(item); }
            }
        }
        finally { Release(windows); }
        return paths;
    }
}
