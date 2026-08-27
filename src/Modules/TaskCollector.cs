using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using SimplePCMonitor.Models;

namespace SimplePCMonitor.Modules
{
    public class TaskCollector
    {
        private List<TaskItem> _cachedItems = new List<TaskItem>();
        private DateTime _lastSampleTime = DateTime.MinValue;
        private readonly TimeSpan _cacheDuration = TimeSpan.FromSeconds(60);

        public List<TaskItem> Sample(bool forceRefresh = false)
        {
            if (!forceRefresh && _cachedItems.Count > 0 && (DateTime.UtcNow - _lastSampleTime) < _cacheDuration)
            {
                return _cachedItems;
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
                    _cachedItems = items;
                    _lastSampleTime = DateTime.UtcNow;
                }
            }
            catch { }

            return items.Count > 0 ? items : _cachedItems;
        }
    }
}
