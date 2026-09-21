using System;
using System.Collections.Generic;
using System.IO;
using SystemCoreMonitor.Core;
using SystemCoreMonitor.Models;

namespace SystemCoreMonitor.Modules
{
    public class DiskCollector
    {
        private readonly TimedCache<List<DiskMetric>> _cache = new TimedCache<List<DiskMetric>>(TimeSpan.FromSeconds(5));

        public List<DiskMetric> Sample(bool forceRefresh = false)
        {
            List<DiskMetric> cached;
            if (_cache.TryGetFresh(forceRefresh, out cached))
            {
                return cached;
            }

            var results = new List<DiskMetric>();
            try
            {
                DriveInfo[] drives = DriveInfo.GetDrives();
                foreach (var d in drives)
                {
                    try
                    {
                        if (!d.IsReady || (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable))
                            continue;

                        string label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "Local Disk" : d.VolumeLabel;

                        // Cloud mounts (Google Drive and similar) surface as fixed drives whose
                        // capacity mirrors the host volume or remote cloud storage. They do not
                        // consume real local storage, so omit them from physical disk reporting.
                        if (FileSystemSafety.IsLikelyVirtualVolume(d.DriveFormat, d.TotalSize, d.DriveType == DriveType.Fixed) ||
                            (!string.IsNullOrWhiteSpace(d.VolumeLabel) && (d.VolumeLabel.IndexOf("Google Drive", StringComparison.OrdinalIgnoreCase) >= 0 || d.VolumeLabel.IndexOf("Cloud", StringComparison.OrdinalIgnoreCase) >= 0)))
                        {
                            continue;
                        }

                        double totalGB = MetricFormatting.BytesToGB(d.TotalSize);
                        double freeGB  = MetricFormatting.BytesToGB(d.TotalFreeSpace);
                        double usedGB  = Math.Round(totalGB - freeGB, 1);
                        double percent = totalGB > 0 ? Math.Round((usedGB * 100.0) / totalGB, 1) : 0.0;

                        string status = MetricFormatting.ClassifyStatus(percent);

                        results.Add(new DiskMetric
                        {
                            Name        = d.Name.TrimEnd('\\'),
                            VolumeLabel = label,
                            DriveFormat = d.DriveFormat,
                            TotalGB     = totalGB,
                            UsedGB      = usedGB,
                            FreeGB      = freeGB,
                            PercentUsed = percent,
                            Status      = status
                        });
                    }
                    catch { }
                }
            }
            catch { }

            return results.Count > 0 ? _cache.Store(results) : (_cache.LastValue ?? results);
        }
    }
}
