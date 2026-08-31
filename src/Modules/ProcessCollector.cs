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
        private readonly Dictionary<int, Tuple<TimeSpan, DateTime>> _prevCpuSamples = new Dictionary<int, Tuple<TimeSpan, DateTime>>();
        private readonly object _syncLock = new object();
        private readonly int _processorCount = Environment.ProcessorCount > 0 ? Environment.ProcessorCount : 1;

        public List<ProcessMetric> Sample(int topCount, double totalRamGB, bool sortByCpu = false, string searchFilter = "")
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
                            TimeSpan totalProcTime = p.TotalProcessorTime;
                            Tuple<TimeSpan, DateTime> prev;
                            lock (_syncLock)
                            {
                                if (_prevCpuSamples.TryGetValue(p.Id, out prev))
                                {
                                    double cpuDeltaMs = (totalProcTime - prev.Item1).TotalMilliseconds;
                                    double timeDeltaMs = (now - prev.Item2).TotalMilliseconds;
                                    if (timeDeltaMs > 100 && cpuDeltaMs >= 0)
                                    {
                                        cpuPct = Math.Round((cpuDeltaMs / (timeDeltaMs * _processorCount)) * 100.0, 1);
                                        if (cpuPct > 100.0) cpuPct = 100.0;
                                    }
                                }
                                _prevCpuSamples[p.Id] = Tuple.Create(totalProcTime, now);
                            }
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
                }

                // Cleanup dead PIDs from cache
                lock (_syncLock)
                {
                    var deadPids = _prevCpuSamples.Keys.Where(k => !activePids.Contains(k)).ToList();
                    foreach (var dead in deadPids)
                    {
                        _prevCpuSamples.Remove(dead);
                    }
                }
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
