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
        private readonly SustainedLoadTracker _loadTracker = new SustainedLoadTracker();
        private volatile List<RunawayProcess> _lastRunaway = new List<RunawayProcess>();

        /// <summary>Runaway processes detected during the most recent <see cref="Sample"/> pass.</summary>
        public List<RunawayProcess> LastRunawayProcesses => _lastRunaway;

        private class ProcessCandidate
        {
            public Process Process { get; set; } = null!;
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public long WorkingSet { get; set; }
            public double MemoryMB { get; set; }
            public double MemoryPercent { get; set; }
            public double CpuPercent { get; set; }
        }

        public List<ProcessMetric> Sample(int topCount = 100, double totalRamGB = 16.0, bool sortByCpu = false, string searchFilter = "")
        {
            var resultList = new List<ProcessMetric>();
            double totalRamBytes = totalRamGB * 1024.0 * 1024.0 * 1024.0;
            if (totalRamBytes <= 0) totalRamBytes = 16.0 * 1024.0 * 1024.0 * 1024.0;

            DateTime now = DateTime.UtcNow;
            var activePids = new HashSet<int>();
            var candidates = new List<ProcessCandidate>();

            try
            {
                var processes = Process.GetProcesses();
                foreach (var p in processes)
                {
                    try
                    {
                        if (p.Id <= 4 || string.IsNullOrEmpty(p.ProcessName))
                        {
                            p.Dispose();
                            continue;
                        }
                        activePids.Add(p.Id);

                        long ws = 0;
                        try { ws = p.WorkingSet64; } catch { p.Dispose(); continue; }

                        double memMB = Math.Round((double)ws / (1024.0 * 1024.0), 1);
                        double memPercent = Math.Round(((double)ws * 100.0) / totalRamBytes, 1);

                        // CPU% Delta
                        double cpuPct = 0.0;
                        try
                        {
                            cpuPct = _cpuTracker.CalculateDelta(p.Id, p.TotalProcessorTime, now);
                        }
                        catch { }

                        candidates.Add(new ProcessCandidate
                        {
                            Process = p,
                            Id = p.Id,
                            Name = p.ProcessName,
                            WorkingSet = ws,
                            MemoryMB = memMB,
                            MemoryPercent = memPercent,
                            CpuPercent = cpuPct
                        });
                    }
                    catch
                    {
                        p.Dispose();
                    }
                }

                // Cleanup dead PIDs from CPU cache
                _cpuTracker.RemoveDeadPids(activePids);
                _loadTracker.RemoveDeadPids(activePids);

                // Runs over every candidate, not only the top list: a disk-wide scan is often small in RAM and CPU.
                _lastRunaway = DetectRunaway(candidates, now);

                IEnumerable<ProcessCandidate> query = candidates;
                if (!string.IsNullOrEmpty(searchFilter))
                {
                    query = query.Where(x =>
                        x.Name.IndexOf(searchFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        x.Id.ToString().Contains(searchFilter)
                    );
                }

                var topList = (sortByCpu
                    ? query.OrderByDescending(x => x.CpuPercent).ThenByDescending(x => x.MemoryMB)
                    : query.OrderByDescending(x => x.MemoryMB).ThenByDescending(x => x.CpuPercent))
                    .Take(topCount)
                    .ToList();

                var topSet = new HashSet<ProcessCandidate>(topList);

                // Phase 2: Enrich only the selected top candidates; dispose the rest immediately
                foreach (var cand in candidates)
                {
                    if (!topSet.Contains(cand))
                    {
                        cand.Process.Dispose();
                        continue;
                    }

                    var p = cand.Process;
                    try
                    {
                        int threads = 0;
                        try { threads = p.Threads.Count; } catch { }

                        bool isProtected = ProcessManager.IsProtected(cand.Name);
                        bool isHeavy = cand.MemoryPercent >= 15.0 || cand.CpuPercent >= 20.0;

                        bool isResponding = true;
                        string winTitle = string.Empty;
                        try
                        {
                            IntPtr hMain = p.MainWindowHandle;
                            if (hMain != IntPtr.Zero)
                            {
                                // Non-blocking Win32 check avoiding 5s SendMessageTimeout
                                bool isHung = NativeMethods.IsHungAppWindow(hMain);
                                isResponding = !isHung;
                                if (!isHung)
                                {
                                    winTitle = p.MainWindowTitle;
                                }
                            }
                        }
                        catch { }

                        string priority = "Normal";
                        try { priority = p.PriorityClass.ToString(); } catch { }

                        var meta = ProcessMetadataCache.GetMetadata(cand.Id, cand.Name);

                        resultList.Add(new ProcessMetric
                        {
                            Id              = cand.Id,
                            Name            = cand.Name,
                            FriendlyName    = meta.FriendlyName,
                            CompanyName     = meta.CompanyName,
                            ExecutablePath  = meta.ExecutablePath,
                            WindowTitle     = winTitle,
                            CpuPercent      = cand.CpuPercent,
                            CpuDisplay      = string.Format("{0:N1}%", cand.CpuPercent),
                            MemoryMB        = cand.MemoryMB,
                            MemoryDisplay   = string.Format("{0:N1} MB", cand.MemoryMB),
                            MemoryPercent   = cand.MemoryPercent,
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
            }
            catch { }

            return resultList;
        }

        private List<RunawayProcess> DetectRunaway(List<ProcessCandidate> candidates, DateTime now)
        {
            var found = new List<RunawayProcess>();
            foreach (var cand in candidates)
            {
                try
                {
                    bool aboveThreshold = cand.CpuPercent >= RunawayProcessDetector.CpuThresholdPercent;
                    bool scanCandidate = RunawayProcessDetector.IsScanCandidate(cand.Name);
                    if (!aboveThreshold && !scanCandidate)
                    {
                        // Below the threshold the start time is irrelevant: the tracker closes the window by PID.
                        _loadTracker.Observe(cand.Id, default(DateTime), cand.CpuPercent, now);
                        continue;
                    }

                    // (PID, StartTime) is the identity passed to the kill path (AGENTS rule 12).
                    DateTime startTime;
                    try { startTime = cand.Process.StartTime; } catch { continue; }

                    TimeSpan? sustained = _loadTracker.Observe(cand.Id, startTime, cand.CpuPercent, now);
                    string? scanTarget = null;
                    string commandLine = string.Empty;
                    if (scanCandidate)
                    {
                        string raw = ProcessManager.GetProcessCommandLine(cand.Id);
                        scanTarget = RunawayProcessDetector.ClassifyScan(cand.Name, raw);
                        commandLine = ProcessManager.SanitizeCommandLine(raw);
                    }

                    if (scanTarget == null && sustained == null) continue;
                    if (!ProcessManager.IsSafeToControl(cand.Id, cand.Name)) continue;

                    if (!scanCandidate)
                    {
                        commandLine = ProcessManager.SanitizeCommandLine(ProcessManager.GetProcessCommandLine(cand.Id));
                    }

                    bool isScan = scanTarget != null;
                    found.Add(new RunawayProcess
                    {
                        Pid         = cand.Id,
                        StartTime   = startTime,
                        Name        = cand.Name,
                        Kind        = isScan ? RunawayKind.Scan : RunawayKind.SustainedLoad,
                        KindDisplay = LocalizationManager.Get(isScan ? "RunawayKindScan" : "RunawayKindSustained"),
                        Reason      = isScan
                            ? string.Format(LocalizationManager.Get("RunawayReasonScan"), ProcessManager.SanitizeCommandLine(scanTarget!))
                            : string.Format(LocalizationManager.Get("RunawayReasonSustained"), RunawayProcessDetector.CpuThresholdPercent, MetricFormatting.FormatAge(sustained!.Value)),
                        AgeDisplay  = MetricFormatting.FormatAge(DateTime.Now - startTime),
                        CpuPercent  = cand.CpuPercent,
                        CommandLine = commandLine
                    });
                }
                catch { }
            }
            return found.OrderByDescending(r => r.CpuPercent).ToList();
        }
    }
}
