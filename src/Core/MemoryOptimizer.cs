using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SimplePCMonitor.Core
{
    public static class MemoryOptimizer
    {
        [DllImport("psapi.dll")]
        private static extern int EmptyWorkingSet(IntPtr hwProc);

        [DllImport("kernel32.dll")]
        private static extern bool SetProcessWorkingSetSize(IntPtr proc, int min, int max);

        public static double OptimizeWorkingSet(out int processesTrimmed)
        {
            processesTrimmed = 0;
            long beforeBytes = GC.GetTotalMemory(false);

            try
            {
                // Force CLR Garbage Collection
                GC.Collect(2, GCCollectionMode.Forced);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced);

                // Trim self
                SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1);
                processesTrimmed++;

                // Safely trim eligible user processes
                var procs = Process.GetProcesses();
                foreach (var p in procs)
                {
                    try
                    {
                        if (p.Id > 4 && !ProcessManager.IsProtected(p.ProcessName))
                        {
                            EmptyWorkingSet(p.Handle);
                            processesTrimmed++;
                        }
                    }
                    catch { }
                    finally
                    {
                        p.Dispose();
                    }
                }
            }
            catch { }

            long afterBytes = GC.GetTotalMemory(false);
            double freedMB = Math.Max(0.0, (double)(beforeBytes - afterBytes) / (1024.0 * 1024.0));
            return freedMB;
        }
    }
}
