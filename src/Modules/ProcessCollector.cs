using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SimplePCMonitor.Core;
using SimplePCMonitor.Models;

namespace SimplePCMonitor.Modules
{
    public class ProcessCollector
    {
        public List<ProcessMetric> Sample(int topCount, double totalRamGB)
        {
            var results = new List<ProcessMetric>();
            double totalRamBytes = totalRamGB * 1024.0 * 1024.0 * 1024.0;
            if (totalRamBytes <= 0) totalRamBytes = 16.0 * 1024.0 * 1024.0 * 1024.0;

            try
            {
                var processes = Process.GetProcesses()
                    .Where(p => p.Id > 4 && !string.IsNullOrEmpty(p.ProcessName))
                    .OrderByDescending(p =>
                    {
                        try { return p.WorkingSet64; } catch { return 0L; }
                    })
                    .Take(topCount);

                foreach (var p in processes)
                {
                    try
                    {
                        long ws = p.WorkingSet64;
                        double memMB = Math.Round((double)ws / (1024.0 * 1024.0), 1);
                        double memPercent = Math.Round(((double)ws * 100.0) / totalRamBytes, 1);
                        int threads = 0;
                        try { threads = p.Threads.Count; } catch { }

                        bool isProtected = ProcessManager.IsProtected(p.ProcessName);
                        bool isHeavy = memPercent >= 15.0;

                        var meta = ProcessMetadataCache.GetMetadata(p.Id, p.ProcessName);
                        string winTitle = string.Empty;
                        try { winTitle = p.MainWindowTitle; } catch { }

                        results.Add(new ProcessMetric
                        {
                            Id              = p.Id,
                            Name            = p.ProcessName,
                            FriendlyName    = meta.FriendlyName,
                            CompanyName     = meta.CompanyName,
                            ExecutablePath  = meta.ExecutablePath,
                            WindowTitle     = winTitle,
                            MemoryMB        = memMB,
                            MemoryDisplay   = string.Format("{0:N1} MB", memMB),
                            MemoryPercent   = memPercent,
                            Threads         = threads,
                            IsProtected     = isProtected,
                            IsHeavyConsumer = isHeavy
                        });
                    }
                    catch { }
                }
            }
            catch { }

            return results;
        }
    }
}
