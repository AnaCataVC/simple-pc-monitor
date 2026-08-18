using System;
using System.Collections.Generic;
using System.IO;
using SimplePCMonitor.Models;

namespace SimplePCMonitor.Modules
{
    public class DiskCollector
    {
        public List<DiskMetric> Sample()
        {
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

                        double totalGB = Math.Round((double)d.TotalSize / (1024.0 * 1024.0 * 1024.0), 1);
                        double freeGB  = Math.Round((double)d.TotalFreeSpace / (1024.0 * 1024.0 * 1024.0), 1);
                        double usedGB  = Math.Round(totalGB - freeGB, 1);
                        double percent = totalGB > 0 ? Math.Round((usedGB * 100.0) / totalGB, 1) : 0.0;

                        string label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "Local Disk" : d.VolumeLabel;
                        string status = percent >= 90.0 ? "Crit" : (percent >= 80.0 ? "Warn" : "Ok");

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

            return results;
        }
    }
}
