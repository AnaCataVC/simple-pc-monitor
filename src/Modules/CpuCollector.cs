using System;
using ComTypes = System.Runtime.InteropServices.ComTypes;
using SimplePCMonitor.Core;
using SimplePCMonitor.Models;

namespace SimplePCMonitor.Modules
{
    public class CpuCollector
    {
        private ComTypes.FILETIME _prevIdle;
        private ComTypes.FILETIME _prevKernel;
        private ComTypes.FILETIME _prevUser;
        private bool _initialized;
        private readonly int _processorCount;

        public CpuCollector()
        {
            _processorCount = Environment.ProcessorCount;
            Sample();
        }

        public CpuMetric Sample()
        {
            ComTypes.FILETIME idle, kernel, user;
            if (!NativeMethods.GetSystemTimes(out idle, out kernel, out user))
            {
                return new CpuMetric { LoadPercent = 0.0, ProcessorCount = _processorCount, Status = "Ok" };
            }

            if (!_initialized)
            {
                _prevIdle = idle;
                _prevKernel = kernel;
                _prevUser = user;
                _initialized = true;
                return new CpuMetric { LoadPercent = 0.0, ProcessorCount = _processorCount, Status = "Ok" };
            }

            ulong uIdle = NativeMethods.FileTimeToUInt64(idle) - NativeMethods.FileTimeToUInt64(_prevIdle);
            ulong uKernel = NativeMethods.FileTimeToUInt64(kernel) - NativeMethods.FileTimeToUInt64(_prevKernel);
            ulong uUser = NativeMethods.FileTimeToUInt64(user) - NativeMethods.FileTimeToUInt64(_prevUser);
            ulong uTotal = uKernel + uUser;

            _prevIdle = idle;
            _prevKernel = kernel;
            _prevUser = user;

            double percent = 0.0;
            if (uTotal > 0)
            {
                double raw = ((double)(uTotal - uIdle) * 100.0) / (double)uTotal;
                percent = Math.Round(Math.Max(0.0, Math.Min(100.0, raw)), 1);
            }

            string status = percent >= 90.0 ? "Crit" : (percent >= 75.0 ? "Warn" : "Ok");

            return new CpuMetric
            {
                LoadPercent = percent,
                ProcessorCount = _processorCount,
                Status = status
            };
        }
    }
}
