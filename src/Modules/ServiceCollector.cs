using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using SimplePCMonitor.Core;
using SimplePCMonitor.Models;

namespace SimplePCMonitor.Modules
{
    public class ServiceCollector
    {
        private static readonly string[] CriticalNames = new[] { "wuauserv", "Spooler", "WinDefend", "Dnscache", "lanmanserver" };

        private readonly TimedCache<ServiceMetric> _cache = new TimedCache<ServiceMetric>(TimeSpan.FromSeconds(30));

        public ServiceMetric Sample(bool forceRefresh = false)
        {
            ServiceMetric cached;
            if (_cache.TryGetFresh(forceRefresh, out cached))
            {
                return cached;
            }

            var metric = new ServiceMetric();
            try
            {
                ServiceController[] services = ServiceController.GetServices();
                metric.TotalServices = services.Length;

                foreach (var svc in services)
                {
                    try
                    {
                        if (svc.Status == ServiceControllerStatus.Running)
                            metric.RunningCount++;
                        else if (svc.Status == ServiceControllerStatus.Stopped)
                            metric.StoppedCount++;
                        else
                            metric.OtherCount++;

                        if (CriticalNames.Contains(svc.ServiceName, StringComparer.OrdinalIgnoreCase))
                        {
                            metric.CriticalServices.Add(new ServiceItem
                            {
                                ServiceName = svc.ServiceName,
                                DisplayName = svc.DisplayName,
                                Status      = svc.Status.ToString(),
                                IsRunning   = svc.Status == ServiceControllerStatus.Running
                            });
                        }
                    }
                    catch { }
                    finally
                    {
                        svc.Dispose();
                    }
                }
            }
            catch { }

            return _cache.Store(metric);
        }
    }
}
