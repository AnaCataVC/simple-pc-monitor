using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SystemCoreMonitor.Core;
using SystemCoreMonitor.Models;

namespace SystemCoreMonitor.Modules
{
    /// <summary>
    /// Enumerates Windows services via native advapi32.dll P/Invoke.
    /// Replaces System.ServiceProcess.ServiceController to maintain zero NuGet dependencies.
    /// Results are cached for 30 seconds to avoid expensive re-enumeration on every tick.
    /// </summary>
    public class ServiceCollector
    {
        private static readonly HashSet<string> CriticalNames = new HashSet<string>(
            new[] { "wuauserv", "Spooler", "WinDefend", "Dnscache", "lanmanserver" },
            StringComparer.OrdinalIgnoreCase);

        private readonly TimedCache<ServiceMetric> _cache =
            new TimedCache<ServiceMetric>(TimeSpan.FromSeconds(30));

        public ServiceMetric Sample(bool forceRefresh = false)
        {
            if (_cache.TryGetFresh(forceRefresh, out ServiceMetric cached))
                return cached;

            var metric = new ServiceMetric();
            IntPtr hScm = IntPtr.Zero;

            try
            {
                hScm = NativeMethods.OpenSCManagerW(
                    null,
                    null,
                    NativeMethods.SC_MANAGER_ENUMERATE_SERVICE | NativeMethods.SC_MANAGER_CONNECT);

                if (hScm == IntPtr.Zero)
                    return _cache.Store(metric);

                EnumerateServices(hScm, metric);
            }
            catch { }
            finally
            {
                if (hScm != IntPtr.Zero)
                    NativeMethods.CloseServiceHandle(hScm);
            }

            return _cache.Store(metric);
        }

        private static void EnumerateServices(IntPtr hScm, ServiceMetric metric)
        {
            uint bytesNeeded = 0;
            uint servicesReturned = 0;

            // First call to determine required buffer size
            NativeMethods.EnumServicesStatusExW(
                hScm,
                NativeMethods.SC_ENUM_PROCESS_INFO,
                NativeMethods.SERVICE_WIN32,
                NativeMethods.SERVICE_STATE_ALL,
                IntPtr.Zero,
                0,
                out bytesNeeded,
                out servicesReturned,
                IntPtr.Zero,
                null);

            if (bytesNeeded == 0)
                return;

            IntPtr buffer = Marshal.AllocHGlobal((int)bytesNeeded);
            try
            {
                bool ok = NativeMethods.EnumServicesStatusExW(
                    hScm,
                    NativeMethods.SC_ENUM_PROCESS_INFO,
                    NativeMethods.SERVICE_WIN32,
                    NativeMethods.SERVICE_STATE_ALL,
                    buffer,
                    bytesNeeded,
                    out bytesNeeded,
                    out servicesReturned,
                    IntPtr.Zero,
                    null);

                if (!ok) return;

                metric.TotalServices = (int)servicesReturned;
                int entrySize = Marshal.SizeOf<NativeMethods.ENUM_SERVICE_STATUS_PROCESS>();

                for (int i = 0; i < (int)servicesReturned; i++)
                {
                    try
                    {
                        IntPtr entryPtr = IntPtr.Add(buffer, i * entrySize);
                        var entry = Marshal.PtrToStructure<NativeMethods.ENUM_SERVICE_STATUS_PROCESS>(entryPtr);

                        string serviceName = Marshal.PtrToStringUni(entry.lpServiceName) ?? string.Empty;
                        string displayName = Marshal.PtrToStringUni(entry.lpDisplayName) ?? string.Empty;
                        uint state = entry.ServiceStatusProcess.dwCurrentState;

                        if (state == NativeMethods.SERVICE_RUNNING)
                            metric.RunningCount++;
                        else if (state == NativeMethods.SERVICE_STOPPED)
                            metric.StoppedCount++;
                        else
                            metric.OtherCount++;

                        if (CriticalNames.Contains(serviceName))
                        {
                            metric.CriticalServices.Add(new ServiceItem
                            {
                                ServiceName = serviceName,
                                DisplayName = displayName,
                                Status      = ServiceStateToString(state),
                                IsRunning   = state == NativeMethods.SERVICE_RUNNING
                            });
                        }
                    }
                    catch { }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }


        public static bool StartService(string serviceName)
        {
            if (string.IsNullOrEmpty(serviceName)) return false;
            IntPtr hScm = NativeMethods.OpenSCManagerW(null, null, NativeMethods.SC_MANAGER_CONNECT);
            if (hScm == IntPtr.Zero) return false;
            try
            {
                IntPtr hSvc = NativeMethods.OpenServiceW(hScm, serviceName, NativeMethods.SERVICE_START);
                if (hSvc == IntPtr.Zero) return false;
                try
                {
                    return NativeMethods.StartServiceW(hSvc, 0, IntPtr.Zero);
                }
                finally
                {
                    NativeMethods.CloseServiceHandle(hSvc);
                }
            }
            finally
            {
                NativeMethods.CloseServiceHandle(hScm);
            }
        }

        public static bool StopService(string serviceName)
        {
            if (string.IsNullOrEmpty(serviceName)) return false;
            IntPtr hScm = NativeMethods.OpenSCManagerW(null, null, NativeMethods.SC_MANAGER_CONNECT);
            if (hScm == IntPtr.Zero) return false;
            try
            {
                IntPtr hSvc = NativeMethods.OpenServiceW(hScm, serviceName, NativeMethods.SERVICE_STOP);
                if (hSvc == IntPtr.Zero) return false;
                try
                {
                    return NativeMethods.ControlService(hSvc, (uint)NativeMethods.SERVICE_CONTROL_STOP, out _);
                }
                finally
                {
                    NativeMethods.CloseServiceHandle(hSvc);
                }
            }
            finally
            {
                NativeMethods.CloseServiceHandle(hScm);
            }
        }

        public static bool RestartService(string serviceName)
        {
            StopService(serviceName);
            System.Threading.Thread.Sleep(500);
            return StartService(serviceName);
        }

        private static string ServiceStateToString(uint state) => state switch
        {
            NativeMethods.SERVICE_RUNNING          => "Running",
            NativeMethods.SERVICE_STOPPED          => "Stopped",
            NativeMethods.SERVICE_PAUSED           => "Paused",
            NativeMethods.SERVICE_START_PENDING    => "StartPending",
            NativeMethods.SERVICE_STOP_PENDING     => "StopPending",
            NativeMethods.SERVICE_CONTINUE_PENDING => "ContinuePending",
            NativeMethods.SERVICE_PAUSE_PENDING    => "PausePending",
            _                                      => "Unknown"
        };
    }
}