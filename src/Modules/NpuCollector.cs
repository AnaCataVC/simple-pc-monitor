using System;
using System.Collections.Generic;
using SimplePCMonitor.Core;
using SimplePCMonitor.Models;

namespace SimplePCMonitor.Modules
{
    public class NpuCollector
    {
        private NpuDeviceInfo _npuInfo;
        private string _targetLuid;
        private bool _scanned;

        public NpuCollector()
        {
            ScanNpu();
        }

        public void ScanNpu()
        {
            try
            {
                var devices = SetupApiHelper.GetNpuDevices();
                if (devices.Count > 0)
                {
                    _npuInfo = devices[0];
                }
                else
                {
                    _npuInfo = null;
                }
                _scanned = true;
            }
            catch
            {
                _npuInfo = null;
            }
        }

        public NpuMetric Sample(Dictionary<string, EngineLoadData> engineLoads, string gpuLuid)
        {
            if (!_scanned)
            {
                ScanNpu();
            }

            var metric = new NpuMetric();

            if (_npuInfo != null && _npuInfo.IsPresent)
            {
                metric.IsPresent = true;
                metric.Name = _npuInfo.Name;

                if (metric.Name.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0)
                    metric.Vendor = "Intel";
                else if (metric.Name.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0)
                    metric.Vendor = "AMD";
                else if (metric.Name.IndexOf("Snapdragon", StringComparison.OrdinalIgnoreCase) >= 0 || metric.Name.IndexOf("Qualcomm", StringComparison.OrdinalIgnoreCase) >= 0)
                    metric.Vendor = "Qualcomm";
                else
                    metric.Vendor = "AI Accelerator";

                // Resolve NPU LUID: match non-GPU LUID that has Compute engine load
                if (engineLoads != null)
                {
                    foreach (var kvp in engineLoads)
                    {
                        string luid = kvp.Key;
                        if (!string.Equals(luid, gpuLuid, StringComparison.OrdinalIgnoreCase))
                        {
                            // NPU / MCDM compute accelerators typically execute on Compute engine
                            _targetLuid = luid;
                            metric.LuidString = luid;
                            metric.LoadPercent = Math.Round(Math.Min(100.0, kvp.Value.Compute > 0 ? kvp.Value.Compute : kvp.Value.TotalLoad), 1);
                            break;
                        }
                    }
                }

                metric.Status = metric.LoadPercent > 0.5 ? "Active" : "Idle";
            }
            else
            {
                metric.IsPresent = false;
                metric.Status = "Not Detected";
            }

            return metric;
        }
    }
}
