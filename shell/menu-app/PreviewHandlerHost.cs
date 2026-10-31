using System.Runtime.InteropServices;

namespace SystemMenu;

// Windows' preview handlers (IPreviewHandler: what Explorer's preview pane uses) for the files
// Quick Look can't draw itself: Word, Excel, PowerPoint, PDF, fonts, mail, whatever the PC's
// programs registered (roadmap 8.4). Each is created out of process (CLSCTX_LOCAL_SERVER: Windows
// runs it in its own prevhost.exe, as Explorer does), so a handler that crashes or hangs never
// takes the bar and the launcher with it; a handler that can't run that way isn't used at all
// (Quick Look shows the file's icon instead). It draws into a plain Win32 child window laid over
// Quick Look's body. COM by vtable slot, slots as ShObjIdl_core.h / PropSys.h list them.
sealed unsafe partial class PreviewHandlerHost : IDisposable
{
    const string IidPreviewHandlerText = "{8895b1c6-b41f-4c1c-a562-0d564250836f}";
    static readonly Guid IidPreviewHandler = new("8895B1C6-B41F-4C1C-A562-0D564250836F");
    static readonly Guid IidInitializeWithStream = new("B824B49D-22AC-4161-AC8A-9916E8FA3F7F");
    static readonly Guid IidInitializeWithItem = new("7F73BE3F-FB79-493C-A6C7-7EE14E245841");
    static readonly Guid IidInitializeWithFile = new("B7D14566-0509-4CCE-A71F-0A554233BD9B");
    static readonly Guid IidPreviewHandlerVisuals = new("196BF9A5-B346-4EF0-AA1E-5DCDB76768B1");
    static readonly Guid IidShellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");
    const uint CLSCTX_LOCAL_SERVER = 0x4, ASSOCSTR_SHELLEXTENSION = 16;
    const uint STGM_READ = 0x0, STGM_SHARE_DENY_NONE = 0x40;
    const uint WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_CLIPCHILDREN = 0x02000000, WS_CLIPSIBLINGS = 0x04000000;
    const uint SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
    // IPreviewHandler: SetWindow 3, SetRect 4, DoPreview 5, Unload 6. IInitializeWith*: Initialize 3.
    // IPreviewHandlerVisuals: SetBackgroundColor 3, SetFont 4, SetTextColor 5.
    const int SetWindow = 3, SetRect = 4, DoPreview = 5, Unload = 6, Initialize = 3, SetBackgroundColor = 3, SetTextColor = 5;

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

    [LibraryImport("shlwapi.dll", EntryPoint = "AssocQueryStringW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int AssocQueryString(uint flags, uint str, string assoc, string extra, char* output, uint* size);
    [LibraryImport("ole32.dll")] private static partial int CoCreateInstance(Guid* clsid, nint outer, uint context, Guid* iid, nint* obj);
    [LibraryImport("shlwapi.dll", EntryPoint = "SHCreateStreamOnFileEx", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHCreateStreamOnFileEx(string file, uint mode, uint attributes, int create, nint template, nint* stream);
    [LibraryImport("shell32.dll", EntryPoint = "SHCreateItemFromParsingName", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHCreateItemFromParsingName(string path, nint bindContext, Guid* iid, nint* item);
    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowEx(uint exStyle, string cls, string? name, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DestroyWindow(nint hwnd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetWindowPos(nint hwnd, nint after, int x, int y, int w, int h, uint flags);

    static nint Slot(nint com, int index) => (*(nint**)com)[index];

    nint handler, window;

    // The preview handler Windows has for this kind of file, or null.
    public static Guid? For(string path)
    {
        var ext = Path.GetExtension(path);
        if (ext.Length == 0) return null;
        var buffer = stackalloc char[64];
        uint size = 64;
        return AssocQueryString(0, ASSOCSTR_SHELLEXTENSION, ext, IidPreviewHandlerText, buffer, &size) >= 0
            && Guid.TryParse(new string(buffer), out var clsid) ? clsid : null;
    }

    // The file previewed in a child window of `parent` at `box` (the parent's client pixels), in
    // the theme's colours where the handler takes them; null when the handler wouldn't run.
    // On the UI thread: the handler's window is a child of ours.
    public static PreviewHandlerHost? Show(Guid clsid, string path, nint parent, PixelBox box, (byte R, byte G, byte B)? background, (byte R, byte G, byte B)? text)
    {
        var host = new PreviewHandlerHost();
        try
        {
            nint h;
            var iid = IidPreviewHandler;
            if (CoCreateInstance(&clsid, 0, CLSCTX_LOCAL_SERVER, &iid, &h) < 0 || h == 0) return null;
            host.handler = h;
            if (!InitializeWith(h, path)) { host.Dispose(); return null; }

            host.window = CreateWindowEx(0, "Static", null, WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN | WS_CLIPSIBLINGS,
                box.Left, box.Top, box.Width, box.Height, parent, 0, 0, 0);
            if (host.window == 0) { host.Dispose(); return null; }
            // Above WinUI's own content window (its sibling), or it would draw underneath. (check)
            SetWindowPos(host.window, 0 /* HWND_TOP */, box.Left, box.Top, box.Width, box.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);

            var visuals = Query(h, IidPreviewHandlerVisuals);
            if (visuals != 0)
            {
                if (background is { } bg) ((delegate* unmanaged[Stdcall]<nint, uint, int>)Slot(visuals, SetBackgroundColor))(visuals, ColorRef(bg));
                if (text is { } fg) ((delegate* unmanaged[Stdcall]<nint, uint, int>)Slot(visuals, SetTextColor))(visuals, ColorRef(fg));
                Marshal.Release(visuals);
            }
            var rect = new RECT { Right = box.Width, Bottom = box.Height };
            if (((delegate* unmanaged[Stdcall]<nint, nint, RECT*, int>)Slot(h, SetWindow))(h, host.window, &rect) < 0
                || ((delegate* unmanaged[Stdcall]<nint, int>)Slot(h, DoPreview))(h) < 0)
            {
                host.Dispose();
                return null;
            }
            return host;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            host.Dispose();
            return null;
        }
    }

    // Explorer's order: a stream (the handler never sees a path, so it can't wander off), then a
    // shell item, then a path.
    static bool InitializeWith(nint handler, string path)
    {
        nint init;
        if ((init = Query(handler, IidInitializeWithStream)) != 0)
        {
            try
            {
                nint stream;
                if (SHCreateStreamOnFileEx(path, STGM_READ | STGM_SHARE_DENY_NONE, 0, 0, 0, &stream) < 0 || stream == 0) return false;
                try { return ((delegate* unmanaged[Stdcall]<nint, nint, uint, int>)Slot(init, Initialize))(init, stream, STGM_READ) >= 0; }
                finally { Marshal.Release(stream); }
            }
            finally { Marshal.Release(init); }
        }
        if ((init = Query(handler, IidInitializeWithItem)) != 0)
        {
            try
            {
                nint item;
                var iid = IidShellItem;
                if (SHCreateItemFromParsingName(path, 0, &iid, &item) < 0 || item == 0) return false;
                try { return ((delegate* unmanaged[Stdcall]<nint, nint, uint, int>)Slot(init, Initialize))(init, item, STGM_READ) >= 0; }
                finally { Marshal.Release(item); }
            }
            finally { Marshal.Release(init); }
        }
        if ((init = Query(handler, IidInitializeWithFile)) != 0)
        {
            try { fixed (char* p = path) return ((delegate* unmanaged[Stdcall]<nint, char*, uint, int>)Slot(init, Initialize))(init, p, STGM_READ) >= 0; }
            finally { Marshal.Release(init); }
        }
        return false;
    }

    // Moved with Quick Look's body (its window resized, another monitor's scale).
    public void Move(PixelBox box)
    {
        if (window == 0 || handler == 0) return;
        SetWindowPos(window, 0, box.Left, box.Top, box.Width, box.Height, SWP_NOACTIVATE);
        var rect = new RECT { Right = box.Width, Bottom = box.Height };
        ((delegate* unmanaged[Stdcall]<nint, RECT*, int>)Slot(handler, SetRect))(handler, &rect);
    }

    public void Dispose()
    {
        if (handler != 0)
        {
            try { ((delegate* unmanaged[Stdcall]<nint, int>)Slot(handler, Unload))(handler); }
            catch (Exception ex) { App.Log(ex); }
            Marshal.Release(handler);
            handler = 0;
        }
        if (window != 0) DestroyWindow(window);
        window = 0;
    }

    static nint Query(nint unknown, Guid iid)
    {
        nint result;
        return ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Slot(unknown, 0))(unknown, &iid, &result) >= 0 ? result : 0;
    }

    static uint ColorRef((byte R, byte G, byte B) c) => c.R | (uint)c.G << 8 | (uint)c.B << 16;
}
