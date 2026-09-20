using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using SystemCoreMonitor.Core;
using SystemCoreMonitor.Models;

namespace SystemCoreMonitor.Modules
{
    public class TaskCollector
    {
        private readonly TimedCache<List<TaskItem>> _cache = new TimedCache<List<TaskItem>>(TimeSpan.FromSeconds(60));

        public List<TaskItem> Sample(bool forceRefresh = false)
        {
            List<TaskItem> cached;
            if (_cache.TryGetFresh(forceRefresh, out cached))
            {
                return cached;
            }

            var items = new List<TaskItem>();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = "/query /fo CSV /nh",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using (var proc = Process.Start(psi))
                {
                    if (proc != null)
                    {
                        using (var reader = proc.StandardOutput)
                        {
                            string line;
                            int count = 0;
                            while ((line = reader.ReadLine()) != null && count < 30)
                            {
                                if (string.IsNullOrWhiteSpace(line)) continue;
                                var parts = line.Split(new[] { "\",\"" }, StringSplitOptions.None);
                                if (parts.Length >= 3)
                                {
                                    string fullPath = parts[0].Trim('\"', ' ');
                                    string status = parts[2].Trim('\"', ' ');

                                    string name = Path.GetFileName(fullPath);
                                    if (string.IsNullOrEmpty(name)) name = fullPath;

                                    items.Add(new TaskItem
                                    {
                                        TaskName = name,
                                        TaskPath = fullPath,
                                        State = status
                                    });
                                    count++;
                                }
                            }
                        }
                        proc.WaitForExit(1000);
                    }
                }

                if (items.Count > 0)
                {
                    _cache.Store(items);
                }
            }
            catch { }

            return items.Count > 0 ? items : (_cache.LastValue ?? items);
        }
    }
}
