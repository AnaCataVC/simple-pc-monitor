using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ComTypes = System.Runtime.InteropServices.ComTypes;
using SystemCoreMonitor.Core;
using SystemCoreMonitor.Models;

namespace SystemCoreMonitor.Modules
{
    public class CpuCollector
    {
        // A core counts as "in use" when it was busy for at least this share of the sample interval;
        // below it the load is background noise (timer ticks, DPCs) rather than real work.
        public const double ActiveCoreThresholdPercent = 10.0;

        private ComTypes.FILETIME _prevIdle;
        private ComTypes.FILETIME _prevKernel;
        private ComTypes.FILETIME _prevUser;
        private bool _initialized;
        private readonly int _processorCount;
        private NativeMethods.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[] _prevCoreTimes;

        public CpuCollector()
        {
            _processorCount = Environment.ProcessorCount;
            Sample();
        }

        public CpuMetric Sample()
        {
            List<double> coreLoads = SampleCoreLoads();

            ComTypes.FILETIME idle, kernel, user;
            if (!NativeMethods.GetSystemTimes(out idle, out kernel, out user))
            {
                return BuildMetric(0.0, coreLoads);
            }

            if (!_initialized)
            {
                _prevIdle = idle;
                _prevKernel = kernel;
                _prevUser = user;
                _initialized = true;
                return BuildMetric(0.0, coreLoads);
            }

            ulong uIdle = NativeMethods.FileTimeToUInt64(idle) - NativeMethods.FileTimeToUInt64(_prevIdle);
            ulong uKernel = NativeMethods.FileTimeToUInt64(kernel) - NativeMethods.FileTimeToUInt64(_prevKernel);
            ulong uUser = NativeMethods.FileTimeToUInt64(user) - NativeMethods.FileTimeToUInt64(_prevUser);
            ulong uTotal = uKernel + uUser;

            _prevIdle = idle;
            _prevKernel = kernel;
            _prevUser = user;

            return BuildMetric(BusyPercent(uTotal, uIdle), coreLoads);
        }

        private CpuMetric BuildMetric(double percent, List<double> coreLoads)
        {
            int active = 0;
            foreach (double load in coreLoads)
            {
                if (load >= ActiveCoreThresholdPercent) active++;
            }

            return new CpuMetric
            {
                LoadPercent = percent,
                ProcessorCount = _processorCount,
                CoreLoads = coreLoads,
                ActiveCoreCount = active,
                Status = MetricFormatting.ClassifyStatus(percent, 75.0)
            };
        }

        private static double BusyPercent(ulong total, ulong idle)
        {
            if (total == 0 || idle > total) return 0.0;
            double raw = ((double)(total - idle) * 100.0) / (double)total;
            return Math.Round(Math.Max(0.0, Math.Min(100.0, raw)), 1);
        }

        /// <summary>
        /// Per-logical-processor busy % since the previous call. Empty on the first call or when the
        /// query fails. Covers the calling thread's processor group only (at most 64 logical processors).
        /// </summary>
        private List<double> SampleCoreLoads()
        {
            var loads = new List<double>();
            var current = QueryCoreTimes();
            if (current == null) return loads;

            var previous = _prevCoreTimes;
            _prevCoreTimes = current;
            if (previous == null || previous.Length != current.Length) return loads;

            for (int i = 0; i < current.Length; i++)
            {
                ulong idle = (ulong)(current[i].IdleTime - previous[i].IdleTime);
                ulong total = (ulong)((current[i].KernelTime - previous[i].KernelTime) + (current[i].UserTime - previous[i].UserTime));
                loads.Add(BusyPercent(total, idle));
            }
            return loads;
        }

        private NativeMethods.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[] QueryCoreTimes()
        {
            int entrySize = Marshal.SizeOf<NativeMethods.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION>();
            int capacity = Math.Max(_processorCount, 1);
            IntPtr buffer = Marshal.AllocHGlobal(entrySize * capacity);
            try
            {
                int status = NativeMethods.NtQuerySystemInformation(
                    NativeMethods.SystemProcessorPerformanceInformation, buffer, (uint)(entrySize * capacity), out uint returned);
                if (status != 0) return null;

                int count = (int)(returned / (uint)entrySize);
                var result = new NativeMethods.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[count];
                for (int i = 0; i < count; i++)
                {
                    result[i] = Marshal.PtrToStructure<NativeMethods.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION>(buffer + i * entrySize);
                }
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }
}
