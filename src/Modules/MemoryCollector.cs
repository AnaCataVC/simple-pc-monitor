using System;
using SimplePCMonitor.Core;
using SimplePCMonitor.Models;

namespace SimplePCMonitor.Modules
{
    public class MemoryCollector
    {
        private readonly NativeMethods.MEMORYSTATUSEX _buffer = new NativeMethods.MEMORYSTATUSEX();

        public MemoryMetric Sample()
        {
            if (!NativeMethods.GlobalMemoryStatusEx(_buffer))
            {
                return new MemoryMetric();
            }

            double totalGB = Math.Round((double)_buffer.ullTotalPhys / (1024.0 * 1024.0 * 1024.0), 1);
            double freeGB  = Math.Round((double)_buffer.ullAvailPhys / (1024.0 * 1024.0 * 1024.0), 1);
            double usedGB  = Math.Round(totalGB - freeGB, 1);
            double percent = (double)_buffer.dwMemoryLoad;

            double pfTotal = Math.Round((double)_buffer.ullTotalPageFile / (1024.0 * 1024.0 * 1024.0), 1);
            double pfFree  = Math.Round((double)_buffer.ullAvailPageFile / (1024.0 * 1024.0 * 1024.0), 1);
            double pfUsed  = Math.Round(pfTotal - pfFree, 1);

            string status = percent >= 90.0 ? "Crit" : (percent >= 80.0 ? "Warn" : "Ok");

            return new MemoryMetric
            {
                LoadPercent     = percent,
                TotalGB         = totalGB,
                UsedGB          = usedGB,
                FreeGB          = freeGB,
                PageFileTotalGB = pfTotal,
                PageFileUsedGB  = pfUsed,
                Status          = status
            };
        }
    }
}
