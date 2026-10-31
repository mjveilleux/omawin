using System.Runtime.InteropServices;

namespace Omawin.Windows;

// Automation objects (IDispatch) by name, for the few documented COM APIs that are only described
// as scripting objects (the Windows Update Agent: Microsoft.Update.Session). Calling by name through
// GetIDsOfNames and Invoke, IDispatch's own four slots, needs no vtable slot counted from a header
// and no reflection, so it stays AOT-safe (plans/07-fixing-windows.md §0: no `dynamic`, no ComImport).
// 64-bit only, as omawin is (x64 and arm64): VARIANT is 24 bytes there.
public sealed unsafe partial class Dispatch : IDisposable
{
    IntPtr ptr;
    Dispatch(IntPtr p) => ptr = p;

    const ushort VT_EMPTY = 0, VT_I2 = 2, VT_I4 = 3, VT_DATE = 7, VT_BSTR = 8, VT_DISPATCH = 9, VT_BOOL = 11,
        VT_UNKNOWN = 13, VT_I1 = 16, VT_UI1 = 17, VT_UI2 = 18, VT_UI4 = 19, VT_I8 = 20, VT_UI8 = 21, VT_INT = 22, VT_UINT = 23;
    const ushort DISPATCH_METHOD = 1, DISPATCH_PROPERTYGET = 2, DISPATCH_PROPERTYPUT = 4;
    const int DISPID_PROPERTYPUT = -3, LOCALE_USER_DEFAULT = 0x0400;
    const uint CLSCTX_ALL = 0x17;
    static readonly Guid IID_IDispatch = new("00020400-0000-0000-C000-000000000046");

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    struct Variant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public long i8;
        [FieldOffset(8)] public int i4;
        [FieldOffset(8)] public short i2;
        [FieldOffset(8)] public double date;
        [FieldOffset(8)] public IntPtr p;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DispParams { public Variant* args; public int* named; public uint count, namedCount; }

    [LibraryImport("ole32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int CLSIDFromProgID(string progId, out Guid clsid);
    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, IntPtr outer, uint context, in Guid iid, out IntPtr obj);
    [LibraryImport("oleaut32.dll")]
    private static partial int VariantClear(Variant* v);

    // A new object by its ProgID ("Microsoft.Update.Session"). The calling thread must have COM
    // initialized (Com.InStaThread does).
    public static Dispatch Create(string progId)
    {
        Check(CLSIDFromProgID(progId, out var clsid), progId);
        Check(CoCreateInstance(clsid, IntPtr.Zero, CLSCTX_ALL, IID_IDispatch, out var p), progId);
        return new Dispatch(p);
    }

    public object? Get(string name, params object[] args) => Invoke(name, DISPATCH_PROPERTYGET | DISPATCH_METHOD, args);
    public object? Call(string name, params object[] args) => Invoke(name, DISPATCH_METHOD, args);
    public void Set(string name, object value) => Invoke(name, DISPATCH_PROPERTYPUT, [value]);

    public Dispatch GetObject(string name, params object[] args) =>
        Get(name, args) as Dispatch ?? throw new InvalidOperationException($"{name} gave nothing");
    public Dispatch CallObject(string name, params object[] args) =>
        Call(name, args) as Dispatch ?? throw new InvalidOperationException($"{name} gave nothing");

    // An automation collection's items (Count and Item(i)), each disposed by the caller.
    public IEnumerable<Dispatch> Items()
    {
        int count = Convert.ToInt32(Get("Count") ?? 0);
        for (int i = 0; i < count; i++)
            if (Get("Item", i) is Dispatch d) yield return d;
    }

    // A collection of strings (an update's KBArticleIDs).
    public List<string> Strings()
    {
        var list = new List<string>();
        int count = Convert.ToInt32(Get("Count") ?? 0);
        for (int i = 0; i < count; i++)
            if (Get("Item", i) is string s) list.Add(s);
        return list;
    }

    object? Invoke(string name, ushort flags, object[] args)
    {
        if (ptr == IntPtr.Zero) throw new ObjectDisposedException(nameof(Dispatch));
        var vtable = *(IntPtr**)ptr;
        int id;
        fixed (char* n = name)
        {
            var names = stackalloc char*[1];
            names[0] = n;
            var iidNull = Guid.Empty;
            var getIds = (delegate* unmanaged[Stdcall]<IntPtr, Guid*, char**, uint, int, int*, int>)vtable[5];
            Check(getIds(ptr, &iidNull, names, 1, LOCALE_USER_DEFAULT, &id), name);
        }

        // Automation takes its arguments last to first.
        var vars = stackalloc Variant[Math.Max(1, args.Length)];
        for (int i = 0; i < args.Length; i++) vars[args.Length - 1 - i] = ToVariant(args[i]);
        int putId = DISPID_PROPERTYPUT;
        var dp = new DispParams
        {
            args = vars,
            count = (uint)args.Length,
            named = flags == DISPATCH_PROPERTYPUT ? &putId : null,
            namedCount = flags == DISPATCH_PROPERTYPUT ? 1u : 0u,
        };
        Variant result = default;
        try
        {
            var iidNull = Guid.Empty;
            var invoke = (delegate* unmanaged[Stdcall]<IntPtr, int, Guid*, int, ushort, DispParams*, Variant*, IntPtr, uint*, int>)vtable[6];
            uint argError;
            Check(invoke(ptr, id, &iidNull, LOCALE_USER_DEFAULT, flags, &dp, &result, IntPtr.Zero, &argError), name);
            return FromVariant(&result);
        }
        finally
        {
            for (int i = 0; i < args.Length; i++) VariantClear(&vars[i]);
            VariantClear(&result);
        }
    }

    static Variant ToVariant(object value) => value switch
    {
        string s => new Variant { vt = VT_BSTR, p = Marshal.StringToBSTR(s) },
        bool b => new Variant { vt = VT_BOOL, i2 = (short)(b ? -1 : 0) },
        int i => new Variant { vt = VT_I4, i4 = i },
        _ => throw new ArgumentException($"can't pass a {value.GetType().Name} to COM"),
    };

    // Owned values are copied out before the caller clears the variant; an object is AddRef'd.
    static object? FromVariant(Variant* v) => v->vt switch
    {
        VT_EMPTY => null,
        VT_BSTR => v->p == IntPtr.Zero ? "" : Marshal.PtrToStringBSTR(v->p),
        VT_BOOL => v->i2 != 0,
        VT_I1 or VT_I2 => (int)v->i2,
        VT_UI1 => (int)(byte)v->i2,
        VT_UI2 => (int)(ushort)v->i2,
        VT_I4 or VT_INT or VT_UI4 or VT_UINT => v->i4,
        VT_I8 or VT_UI8 => v->i8,
        VT_DATE => DateTime.FromOADate(v->date),
        VT_DISPATCH or VT_UNKNOWN => v->p == IntPtr.Zero ? null : AddRef(v->p),
        _ => null,
    };

    static Dispatch AddRef(IntPtr p)
    {
        Marshal.AddRef(p);
        return new Dispatch(p);
    }

    static void Check(int hr, string what)
    {
        if (hr < 0) throw new InvalidOperationException($"{what}: {Marshal.GetExceptionForHR(hr)?.Message ?? $"0x{hr:X8}"}");
    }

    public void Dispose()
    {
        if (ptr != IntPtr.Zero) Marshal.Release(ptr);
        ptr = IntPtr.Zero;
    }
}

// Runs COM work on a thread of its own with a single-threaded apartment, the model automation
// objects are written for, and hands back its result (or its exception).
public static partial class Com
{
    [LibraryImport("ole32.dll")] private static partial int CoInitializeEx(IntPtr reserved, uint coinit);
    [LibraryImport("ole32.dll")] private static partial void CoUninitialize();

    public static T InStaThread<T>(Func<T> work)
    {
        T result = default!;
        Exception? failed = null;
        var t = new Thread(() =>
        {
            int hr = CoInitializeEx(IntPtr.Zero, 0x2); // COINIT_APARTMENTTHREADED
            try { result = work(); }
            catch (Exception ex) { failed = ex; }
            finally { if (hr >= 0) CoUninitialize(); }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (failed != null) throw failed is InvalidOperationException ? failed : new InvalidOperationException(failed.Message, failed);
        return result;
    }
}
