using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SystemCoreMonitor.Core;
using SystemCoreMonitor.Models;

namespace SystemCoreMonitor.Modules
{
    public class ProcessCollector
    {
        private readonly CpuUsageTracker _cpuTracker = new CpuUsageTracker();

        public List<ProcessMetric> Sample(int topCount = 50, double totalRamGB = 16.0, bool sortByCpu = false, string searchFilter = "")
        {
            var rawList = new List<ProcessMetric>();
            double totalRamBytes = totalRamGB * 1024.0 * 1024.0 * 1024.0;
            if (totalRamBytes <= 0) totalRamBytes = 16.0 * 1024.0 * 1024.0 * 1024.0;

            DateTime now = DateTime.UtcNow;
            var activePids = new HashSet<int>();

            try
            {
                var processes = Process.GetProcesses();
                foreach (var p in processes)
                {
                    try
                    {
                        if (p.Id <= 4 || string.IsNullOrEmpty(p.ProcessName)) continue;
                        activePids.Add(p.Id);

                        long ws = 0;
                        try { ws = p.WorkingSet64; } catch { continue; }

                        double memMB = Math.Round((double)ws / (1024.0 * 1024.0), 1);
                        double memPercent = Math.Round(((double)ws * 100.0) / totalRamBytes, 1);

                        // CPU% Delta
                        double cpuPct = 0.0;
                        try
                        {
                            cpuPct = _cpuTracker.CalculateDelta(p.Id, p.TotalProcessorTime, now);
                        }
                        catch { }

                        int threads = 0;
                        try { threads = p.Threads.Count; } catch { }

                        bool isProtected = ProcessManager.IsProtected(p.ProcessName);
                        bool isHeavy = memPercent >= 15.0 || cpuPct >= 20.0;

                        bool isResponding = true;
                        try { isResponding = p.Responding; } catch { }

                        string priority = "Normal";
                        try { priority = p.PriorityClass.ToString(); } catch { }

                        var meta = ProcessMetadataCache.GetMetadata(p.Id, p.ProcessName);
                        string winTitle = string.Empty;
                        try { winTitle = p.MainWindowTitle; } catch { }

                        rawList.Add(new ProcessMetric
                        {
                            Id              = p.Id,
                            Name            = p.ProcessName,
                            FriendlyName    = meta.FriendlyName,
                            CompanyName     = meta.CompanyName,
                            ExecutablePath  = meta.ExecutablePath,
                            WindowTitle     = winTitle,
                            CpuPercent      = cpuPct,
                            CpuDisplay      = string.Format("{0:N1}%", cpuPct),
                            MemoryMB        = memMB,
                            MemoryDisplay   = string.Format("{0:N1} MB", memMB),
                            MemoryPercent   = memPercent,
                            Threads         = threads,
                            IsProtected     = isProtected,
                            IsHeavyConsumer = isHeavy,
                            IsResponding    = isResponding,
                            PriorityClass   = priority
                        });
                    }
                    catch { }
                    finally
                    {
                        p.Dispose();
                    }
                }

                // Cleanup dead PIDs from cache
                _cpuTracker.RemoveDeadPids(activePids);
            }
            catch { }

            IEnumerable<ProcessMetric> query = rawList;
            if (!string.IsNullOrEmpty(searchFilter))
            {
                query = query.Where(x =>
                    x.Name.IndexOf(searchFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    x.FriendlyName.IndexOf(searchFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    x.Id.ToString().Contains(searchFilter)
                );
            }

            if (sortByCpu)
            {
                return query.OrderByDescending(x => x.CpuPercent).ThenByDescending(x => x.MemoryMB).Take(topCount).ToList();
            }
            else
            {
                return query.OrderByDescending(x => x.MemoryMB).ThenByDescending(x => x.CpuPercent).Take(topCount).ToList();
            }
        }
    }
}
