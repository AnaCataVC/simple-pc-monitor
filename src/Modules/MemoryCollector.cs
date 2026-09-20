using System;
using SystemCoreMonitor.Core;
using SystemCoreMonitor.Models;

namespace SystemCoreMonitor.Modules
{
    public class MemoryCollector
    {
        private NativeMethods.MEMORYSTATUSEX _buffer = NativeMethods.MEMORYSTATUSEX.Create();

        public MemoryMetric Sample()
        {
            if (!NativeMethods.GlobalMemoryStatusEx(ref _buffer))
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