using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Omawin.Windows;

// Windows' event logs, read through the documented Event Log API (winevt.h, wevtapi.dll): a query by
// XPath, each event rendered as XML, parsed by HealthEvents.Parse. Used directly rather than
// .NET's EventLogReader, which is a separate package and not AOT-safe.
public static unsafe partial class EventLog
{
    const uint EvtQueryChannelPath = 0x1, EvtQueryReverseDirection = 0x200, EvtRenderEventXml = 1;
    const int ERROR_EVT_CHANNEL_NOT_FOUND = 15007;

    [LibraryImport("wevtapi.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr EvtQuery(IntPtr session, string path, string query, uint flags);
    [LibraryImport("wevtapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EvtNext(IntPtr resultSet, uint size, IntPtr* events, uint timeoutMs, uint flags, out uint returned);
    [LibraryImport("wevtapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EvtRender(IntPtr context, IntPtr fragment, uint flags, uint bufferBytes, char* buffer, out uint usedBytes, out uint propertyCount);
    [LibraryImport("wevtapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EvtClose(IntPtr handle);

    // Newest first, at most `max`. A log this account may not read throws UnauthorizedAccessException
    // (the report then says so for that part); a log this Windows doesn't have gives nothing.
    public static List<LogEvent> Query(string channel, string xpath, int max = 500)
    {
        var results = new List<LogEvent>();
        var q = EvtQuery(IntPtr.Zero, channel, xpath, EvtQueryChannelPath | EvtQueryReverseDirection);
        if (q == IntPtr.Zero)
        {
            int e = Marshal.GetLastPInvokeError();
            if (e == Win32.ERROR_ACCESS_DENIED) throw new UnauthorizedAccessException(channel);
            if (e == ERROR_EVT_CHANNEL_NOT_FOUND) return results;
            throw new Win32Exception(e);
        }
        try
        {
            var batch = stackalloc IntPtr[32];
            var buffer = new char[16 * 1024];
            while (results.Count < max)
            {
                if (!EvtNext(q, 32, batch, 1000, 0, out uint got))
                {
                    int e = Marshal.GetLastPInvokeError();
                    if (e == Win32.ERROR_NO_MORE_ITEMS) break;
                    if (e == Win32.ERROR_ACCESS_DENIED) throw new UnauthorizedAccessException(channel);
                    throw new Win32Exception(e);
                }
                for (uint i = 0; i < got; i++)
                {
                    try
                    {
                        if (results.Count < max && Render(batch[i], ref buffer) is { } xml) results.Add(HealthEvents.Parse(xml));
                    }
                    catch (System.Xml.XmlException) { } // one malformed event never spoils the rest
                    catch (FormatException) { }
                    finally { EvtClose(batch[i]); }
                }
            }
        }
        finally { EvtClose(q); }
        return results;
    }

    // The event's XML; the buffer grows when an event is bigger than it.
    static string? Render(IntPtr evt, ref char[] buffer)
    {
        uint used;
        fixed (char* b = buffer)
            if (EvtRender(IntPtr.Zero, evt, EvtRenderEventXml, (uint)buffer.Length * 2, b, out used, out _))
                return Text(buffer, used);
        if (Marshal.GetLastPInvokeError() != Win32.ERROR_INSUFFICIENT_BUFFER) return null;
        buffer = new char[used / 2 + 1];
        fixed (char* b = buffer)
            return EvtRender(IntPtr.Zero, evt, EvtRenderEventXml, (uint)buffer.Length * 2, b, out used, out _) ? Text(buffer, used) : null;
    }

    // usedBytes counts the closing NUL.
    static string Text(char[] buffer, uint usedBytes) => new(buffer, 0, Math.Max(0, (int)(usedBytes / 2) - 1));
}
