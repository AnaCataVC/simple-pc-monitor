using System;
using System.Collections.Generic;
using SimplePCMonitor.Core;
using SimplePCMonitor.Models;

namespace SimplePCMonitor.Modules
{
    public class GpuCollector
    {
        private DxgiAdapterInfo _primaryAdapter;
        private string _targetLuid;
        private bool _adaptersScanned;

        public GpuCollector()
        {
            ScanAdapters();
        }

        public void ScanAdapters()
        {
            try
            {
                var adapters = DxgiHelper.GetAdapters();
                _primaryAdapter = null;

                // Priority 1: Discrete GPU (NVIDIA / AMD Radeon)
                foreach (var a in adapters)
                {
                    if (!a.IsSoftware && a.IsDiscrete)
                    {
                        _primaryAdapter = a;
                        break;
                    }
                }

                // Priority 2: Integrated physical GPU (Intel Arc / Iris Xe / Radeon Graphics)
                if (_primaryAdapter == null)
                {
                    foreach (var a in adapters)
                    {
                        if (!a.IsSoftware)
                        {
                            _primaryAdapter = a;
                            break;
                        }
                    }
                }

                if (_primaryAdapter != null)
                {
                    _targetLuid = _primaryAdapter.LuidString;
                }
                else
                {
                    _targetLuid = "";
                }

                _adaptersScanned = true;
            }
            catch
            {
                _primaryAdapter = null;
                _targetLuid = "";
            }
        }

        public GpuMetric Sample(Dictionary<string, EngineLoadData> engineLoads)
        {
            if (!_adaptersScanned || _primaryAdapter == null)
            {
                ScanAdapters();
            }

            var metric = new GpuMetric();

            if (_primaryAdapter != null)
            {
                metric.Name = _primaryAdapter.Description;
                metric.LuidString = _primaryAdapter.LuidString;
                metric.IsDiscrete = _primaryAdapter.IsDiscrete;
                metric.DedicatedVramTotalMB = _primaryAdapter.DedicatedVramMB;
                metric.SharedVramTotalMB = _primaryAdapter.SharedVramMB;

                if (metric.Name.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0)
                    metric.Vendor = "Intel";
                else if (metric.Name.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0)
                    metric.Vendor = "NVIDIA";
                else if (metric.Name.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0 || metric.Name.IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) >= 0)
                    metric.Vendor = "AMD";
                else
                    metric.Vendor = "Display Adapter";

                // Match engine telemetry by LUID
                if (engineLoads != null && engineLoads.Count > 0)
                {
                    EngineLoadData data = null;

                    if (!string.IsNullOrEmpty(_targetLuid) && engineLoads.TryGetValue(_targetLuid, out data))
                    {
                        // Found by known LUID
                    }
                    else
                    {
                        // Fallback: pick the primary graphics LUID (the one with 3D or VideoDecode or max engines)
                        foreach (var kvp in engineLoads)
                        {
                            if (kvp.Value.Engine3D > 0 || kvp.Value.VideoDecode > 0 || kvp.Value.Copy > 0 || kvp.Value.VideoProcessing > 0)
                            {
                                _targetLuid = kvp.Key;
                                data = kvp.Value;
                                break;
                            }
                        }

                        // If all idle, pick first non-empty LUID
                        if (data == null)
                        {
                            foreach (var kvp in engineLoads)
                            {
                                _targetLuid = kvp.Key;
                                data = kvp.Value;
                                break;
                            }
                        }
                    }

                    if (data != null)
                    {
                        metric.LuidString = _targetLuid;
                        metric.Engines.Engine3DPercent = Math.Round(Math.Min(100.0, data.Engine3D), 1);
                        metric.Engines.ComputePercent = Math.Round(Math.Min(100.0, data.Compute), 1);
                        metric.Engines.VideoDecodePercent = Math.Round(Math.Min(100.0, data.VideoDecode), 1);
                        metric.Engines.VideoProcessingPercent = Math.Round(Math.Min(100.0, data.VideoProcessing), 1);
                        metric.Engines.CopyPercent = Math.Round(Math.Min(100.0, data.Copy), 1);

                        // Task Manager standard: Peak active engine load or composite sum
                        double maxEngine = Math.Max(data.Engine3D, Math.Max(data.Compute, Math.Max(data.VideoDecode, data.VideoProcessing)));
                        metric.LoadPercent = Math.Round(Math.Min(100.0, maxEngine > 0 ? maxEngine : data.TotalLoad), 1);
                    }
                }
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
