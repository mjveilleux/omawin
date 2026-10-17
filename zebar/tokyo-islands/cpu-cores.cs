// cpu-cores: per-core CPU load for the Zebar bar (Zebar's own cpu provider only has the total).
//
// Prints one line per second on stdout: each logical core's load in percent, e.g.
//   12 3 45 0 7 ...
//
// Read with NtQuerySystemInformation(SystemProcessorPerformanceInformation), the same numbers
// Task Manager's per-core graphs use: load = 1 - idle time / (kernel + user time), between
// two samples.
//
// Built by install.ps1 into ~/.local/bin with the C# compiler that ships with Windows.
using System;
using System.Runtime.InteropServices;
using System.Threading;

static class CpuCores
{
    [StructLayout(LayoutKind.Sequential)]
    struct ProcessorTimes
    {
        public long Idle, Kernel, User, Dpc, Interrupt; // kernel time includes idle time
        public int InterruptCount;
    }

    [DllImport("ntdll.dll")]
    static extern int NtQuerySystemInformation(int infoClass, [Out] ProcessorTimes[] info, int length, out int returned);
    const int SystemProcessorPerformanceInformation = 8;

    static ProcessorTimes[] Sample(int cores)
    {
        var times = new ProcessorTimes[cores];
        int returned;
        NtQuerySystemInformation(SystemProcessorPerformanceInformation, times, cores * Marshal.SizeOf(typeof(ProcessorTimes)), out returned);
        return times;
    }

    // Quit when Zebar is gone (it was force-closed or restarted), so no copy lingers behind.
    static bool ZebarRunning()
    {
        return System.Diagnostics.Process.GetProcessesByName("zebar").Length > 0;
    }

    static void Main()
    {
        try { Run(); }
        catch (System.IO.IOException) { } // stdout closed: whoever was reading is gone
    }

    // The bar runs this only while its CPU card is open. Zebar can't stop a program it started
    // (on Windows), so this stops itself after 10 readings, and the bar starts another while the
    // card is still open: closed, it's gone within 10 s.
    const int Readings = 10;

    static void Run()
    {
        int cores = Environment.ProcessorCount;
        var output = Console.Out;
        var last = Sample(cores);
        int tick = 0;
        while (tick < Readings)
        {
            Thread.Sleep(1000);
            if (++tick % 3 == 0 && !ZebarRunning()) return;
            var now = Sample(cores);
            var loads = new string[cores];
            for (int i = 0; i < cores; i++)
            {
                long idle = now[i].Idle - last[i].Idle;
                long total = (now[i].Kernel - last[i].Kernel) + (now[i].User - last[i].User);
                double load = total > 0 ? 100.0 * (total - idle) / total : 0;
                loads[i] = ((int)Math.Round(Math.Max(0, Math.Min(100, load)))).ToString();
            }
            output.WriteLine(string.Join(" ", loads));
            output.Flush();
            last = now;
        }
    }
}
