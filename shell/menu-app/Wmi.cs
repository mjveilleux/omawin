using System.Runtime.InteropServices;

namespace SystemMenu;

// Just enough WMI for the display's brightness (QuickSettings.cs, Brightness): a query, and one
// method call. Straight through WMI's COM interfaces by their vtables, since System.Management
// can't be compiled as one native program (Native AOT, build.ps1 -Aot) and this is all it did here.
// Slots are from wbemcli.h.
sealed unsafe class Wmi : IDisposable
{
    IntPtr services;

    Wmi(IntPtr services) { this.services = services; }

    public static Wmi Connect(string ns)
    {
        var clsid = new Guid("4590f811-1d3a-11d0-891f-00aa004b2e24"); // WbemLocator
        var iid = new Guid("dc12a687-737f-11cf-884d-00aa004b2e24"); // IWbemLocator
        Vtable.Check(CoCreateInstance(clsid, IntPtr.Zero, 1 /* in-process */, iid, out var locator));
        try
        {
            IntPtr name = Marshal.StringToBSTR(ns), services;
            try
            {
                // ConnectServer(namespace, user, password, locale, flags, authority, context, services)
                var connect = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, IntPtr, IntPtr, IntPtr*, int>)Vtable.Slot(locator, 3);
                Vtable.Check(connect(locator, name, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero, &services));
            }
            finally { Marshal.FreeBSTR(name); }
            // As yourself, every call checked: what System.Management set too.
            Vtable.Check(CoSetProxyBlanket(services, 10 /* WinNT */, 0, IntPtr.Zero, 3 /* call */, 3 /* impersonate */, IntPtr.Zero, 0));
            return new Wmi(services);
        }
        finally { Marshal.Release(locator); }
    }

    // Each object the query finds; dispose them (or let the list go: Query disposes as it goes).
    public List<WmiObject> Query(string wql)
    {
        var found = new List<WmiObject>();
        IntPtr lang = Marshal.StringToBSTR("WQL"), query = Marshal.StringToBSTR(wql), results;
        try
        {
            // ExecQuery(language, query, flags: forward only | return immediately, context, results)
            var exec = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, int, IntPtr, IntPtr*, int>)Vtable.Slot(services, 20);
            Vtable.Check(exec(services, lang, query, 0x20 | 0x10, IntPtr.Zero, &results));
        }
        finally { Marshal.FreeBSTR(lang); Marshal.FreeBSTR(query); }
        try
        {
            var next = (delegate* unmanaged[Stdcall]<IntPtr, int, uint, IntPtr*, uint*, int>)Vtable.Slot(results, 4);
            while (true)
            {
                IntPtr item; uint count;
                int hr = next(results, -1 /* wait */, 1, &item, &count);
                if (hr < 0) Vtable.Check(hr);
                if (count == 0) break;
                found.Add(new WmiObject(item));
            }
        }
        finally { Marshal.Release(results); }
        return found;
    }

    // Calls a method of the object at `path` (its __PATH), with these arguments in.
    public void Call(string path, string className, string method, params (string Name, Variant Value)[] arguments)
    {
        IntPtr klass = IntPtr.Zero, signature = IntPtr.Zero, input = IntPtr.Zero, output = IntPtr.Zero;
        IntPtr cls = Marshal.StringToBSTR(className), at = Marshal.StringToBSTR(path), name = Marshal.StringToBSTR(method);
        try
        {
            var getObject = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, IntPtr, IntPtr*, IntPtr, int>)Vtable.Slot(services, 6);
            Vtable.Check(getObject(services, cls, 0, IntPtr.Zero, &klass, IntPtr.Zero));
            fixed (char* m = method)
            {
                var getMethod = (delegate* unmanaged[Stdcall]<IntPtr, char*, int, IntPtr*, IntPtr*, int>)Vtable.Slot(klass, 19);
                Vtable.Check(getMethod(klass, m, 0, &signature, null));
            }
            var spawn = (delegate* unmanaged[Stdcall]<IntPtr, int, IntPtr*, int>)Vtable.Slot(signature, 15);
            Vtable.Check(spawn(signature, 0, &input));
            var put = (delegate* unmanaged[Stdcall]<IntPtr, char*, int, Variant*, int, int>)Vtable.Slot(input, 5);
            foreach (var (argument, value) in arguments)
            {
                var v = value;
                fixed (char* a = argument) Vtable.Check(put(input, a, 0, &v, 0));
            }
            var execMethod = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, int, IntPtr, IntPtr, IntPtr*, IntPtr, int>)Vtable.Slot(services, 24);
            Vtable.Check(execMethod(services, at, name, 0, IntPtr.Zero, input, &output, IntPtr.Zero));
        }
        finally
        {
            Marshal.FreeBSTR(cls); Marshal.FreeBSTR(at); Marshal.FreeBSTR(name);
            foreach (var p in new[] { klass, signature, input, output }) if (p != IntPtr.Zero) Marshal.Release(p);
        }
    }

    public void Dispose()
    {
        if (services != IntPtr.Zero) Marshal.Release(services);
        services = IntPtr.Zero;
    }

    [DllImport("ole32.dll")] static extern int CoCreateInstance(in Guid clsid, IntPtr outer, uint context, in Guid iid, out IntPtr unknown);
    [DllImport("ole32.dll")] static extern int CoSetProxyBlanket(IntPtr proxy, uint authn, uint authz, IntPtr principal, uint level, uint impersonation, IntPtr identity, uint capabilities);
}

// One object a query found: its properties by name, as int, string or int[] (WMI hands every
// integer type up to 32 bits over as a 32-bit one, arrays too).
sealed unsafe class WmiObject : IDisposable
{
    IntPtr obj;

    internal WmiObject(IntPtr obj) { this.obj = obj; }

    public object? this[string name]
    {
        get
        {
            var v = new Variant();
            fixed (char* n = name)
            {
                var get = (delegate* unmanaged[Stdcall]<IntPtr, char*, int, Variant*, int*, int*, int>)Vtable.Slot(obj, 4);
                Vtable.Check(get(obj, n, 0, &v, null, null));
            }
            try { return v.Value(); }
            finally { VariantClear(&v); }
        }
    }

    public void Dispose()
    {
        if (obj != IntPtr.Zero) Marshal.Release(obj);
        obj = IntPtr.Zero;
    }

    [DllImport("oleaut32.dll")] static extern int VariantClear(Variant* v);
}

// A VARIANT, as far as WMI needs it here: 4-byte and 1-byte integers, strings, arrays of 4-byte
// integers.
[StructLayout(LayoutKind.Explicit, Size = 24)]
unsafe struct Variant
{
    [FieldOffset(0)] public ushort Type;
    [FieldOffset(8)] public int Int;
    [FieldOffset(8)] public byte Byte;
    [FieldOffset(8)] public IntPtr Pointer;

    const ushort I4 = 3, Bstr = 8, UI1 = 17, Array = 0x2000;

    public static Variant FromInt(int value) => new() { Type = I4, Int = value };
    public static Variant FromByte(byte value) => new() { Type = UI1, Byte = value };

    public object? Value()
    {
        switch (Type)
        {
            case I4: return Int;
            case UI1: return (int)Byte;
            case Bstr: return Marshal.PtrToStringBSTR(Pointer);
            case Array | I4:
                SafeArrayGetLBound(Pointer, 1, out int low);
                SafeArrayGetUBound(Pointer, 1, out int high);
                var values = new int[Math.Max(0, high - low + 1)];
                for (int i = low; i <= high; i++) { int x; SafeArrayGetElement(Pointer, &i, &x); values[i - low] = x; }
                return values;
            default: return null; // empty, null, or a type nothing here asks for
        }
    }

    [DllImport("oleaut32.dll")] static extern int SafeArrayGetLBound(IntPtr array, uint dim, out int bound);
    [DllImport("oleaut32.dll")] static extern int SafeArrayGetUBound(IntPtr array, uint dim, out int bound);
    [DllImport("oleaut32.dll")] static extern int SafeArrayGetElement(IntPtr array, int* index, void* value);
}

// Calling a COM interface by its vtable, for the few the native build (Native AOT) can't reach
// through built-in COM: Wmi.cs, AppsFolder.cs, AppIcons.cs.
static unsafe class Vtable
{
    // The function in this slot of the object's vtable (slots as the SDK header lists them).
    public static IntPtr Slot(IntPtr com, int index) => (*(IntPtr**)com)[index];

    public static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }
}
