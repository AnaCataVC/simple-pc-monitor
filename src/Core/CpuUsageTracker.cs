using System;
using System.Collections.Generic;

namespace SimplePCMonitor.Core
{
    public class CpuUsageTracker
    {
        private readonly Dictionary<int, Tuple<TimeSpan, DateTime>> _prevSamples = new Dictionary<int, Tuple<TimeSpan, DateTime>>();
        private readonly object _lock = new object();
        private readonly int _processorCount = Environment.ProcessorCount > 0 ? Environment.ProcessorCount : 1;

        public double CalculateDelta(int pid, TimeSpan totalProcessorTime, DateTime now)
        {
            double cpuPercent = 0.0;
            lock (_lock)
            {
                Tuple<TimeSpan, DateTime> prev;
                if (_prevSamples.TryGetValue(pid, out prev))
                {
                    double cpuDeltaMs = (totalProcessorTime - prev.Item1).TotalMilliseconds;
                    double timeDeltaMs = (now - prev.Item2).TotalMilliseconds;
                    if (timeDeltaMs > 100 && cpuDeltaMs >= 0)
                    {
                        cpuPercent = Math.Min(100.0, Math.Round((cpuDeltaMs / (timeDeltaMs * _processorCount)) * 100.0, 1));
                    }
                }
                _prevSamples[pid] = Tuple.Create(totalProcessorTime, now);
            }
            return cpuPercent;
        }

        public void RemoveDeadPids(ICollection<int> alivePids)
        {
            lock (_lock)
            {
                var dead = new List<int>();
                foreach (var pid in _prevSamples.Keys)
                {
                    if (!alivePids.Contains(pid)) dead.Add(pid);
                }
                foreach (var pid in dead) _prevSamples.Remove(pid);
            }
        }
    }
}
