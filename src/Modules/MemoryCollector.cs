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

            double totalGB = MetricFormatting.BytesToGB(_buffer.ullTotalPhys);
            double freeGB  = MetricFormatting.BytesToGB(_buffer.ullAvailPhys);
            double usedGB  = Math.Round(totalGB - freeGB, 1);
            double percent = (double)_buffer.dwMemoryLoad;

            double pfTotal = MetricFormatting.BytesToGB(_buffer.ullTotalPageFile);
            double pfFree  = MetricFormatting.BytesToGB(_buffer.ullAvailPageFile);
            double pfUsed  = Math.Round(pfTotal - pfFree, 1);

            string status = MetricFormatting.ClassifyStatus(percent);

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
