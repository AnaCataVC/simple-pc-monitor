using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using SimplePCMonitor.Models;

namespace SimplePCMonitor.Modules
{
    public class TaskCollector
    {
        public List<TaskItem> Sample()
        {
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
            }
            catch { }

            return items;
        }
    }
}
