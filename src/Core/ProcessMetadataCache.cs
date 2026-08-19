using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace SimplePCMonitor.Core
{
    public class ProcessMetadataInfo
    {
        public string FriendlyName { get; set; }
        public string CompanyName { get; set; }
        public string ExecutablePath { get; set; }
        public string FileVersion { get; set; }

        public ProcessMetadataInfo()
        {
            FriendlyName = string.Empty;
            CompanyName = string.Empty;
            ExecutablePath = string.Empty;
            FileVersion = string.Empty;
        }
    }

    public static class ProcessMetadataCache
    {
        private static readonly ConcurrentDictionary<string, ProcessMetadataInfo> Cache =
            new ConcurrentDictionary<string, ProcessMetadataInfo>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, ProcessMetadataInfo> PrebakedSystemProcesses =
            new Dictionary<string, ProcessMetadataInfo>(StringComparer.OrdinalIgnoreCase)
        {
            { "system", new ProcessMetadataInfo { FriendlyName = "Windows System Kernel", CompanyName = "Microsoft Corporation" } },
            { "idle", new ProcessMetadataInfo { FriendlyName = "System Idle Process", CompanyName = "Microsoft Corporation" } },
            { "smss", new ProcessMetadataInfo { FriendlyName = "Windows Session Manager", CompanyName = "Microsoft Corporation" } },
            { "csrss", new ProcessMetadataInfo { FriendlyName = "Client Server Runtime Process", CompanyName = "Microsoft Corporation" } },
            { "wininit", new ProcessMetadataInfo { FriendlyName = "Windows Start-Up Application", CompanyName = "Microsoft Corporation" } },
            { "services", new ProcessMetadataInfo { FriendlyName = "Services and Controller App", CompanyName = "Microsoft Corporation" } },
            { "lsass", new ProcessMetadataInfo { FriendlyName = "Local Security Authority Process", CompanyName = "Microsoft Corporation" } },
            { "svchost", new ProcessMetadataInfo { FriendlyName = "Host Process for Windows Services", CompanyName = "Microsoft Corporation" } },
            { "fontdrvhost", new ProcessMetadataInfo { FriendlyName = "Usermode Font Driver Host", CompanyName = "Microsoft Corporation" } },
            { "dwm", new ProcessMetadataInfo { FriendlyName = "Desktop Window Manager", CompanyName = "Microsoft Corporation" } },
            { "explorer", new ProcessMetadataInfo { FriendlyName = "Windows Explorer", CompanyName = "Microsoft Corporation" } },
            { "sihost", new ProcessMetadataInfo { FriendlyName = "Shell Infrastructure Host", CompanyName = "Microsoft Corporation" } },
            { "taskhostw", new ProcessMetadataInfo { FriendlyName = "Host Process for Windows Tasks", CompanyName = "Microsoft Corporation" } },
            { "runtimebroker", new ProcessMetadataInfo { FriendlyName = "Runtime Broker", CompanyName = "Microsoft Corporation" } },
            { "searchhost", new ProcessMetadataInfo { FriendlyName = "Windows Search Host", CompanyName = "Microsoft Corporation" } },
            { "startmenuexperiencehost", new ProcessMetadataInfo { FriendlyName = "Start Menu Experience Host", CompanyName = "Microsoft Corporation" } },
            { "shellexperiencehost", new ProcessMetadataInfo { FriendlyName = "Windows Shell Experience Host", CompanyName = "Microsoft Corporation" } },
            { "textinputhost", new ProcessMetadataInfo { FriendlyName = "Windows Text Input Host", CompanyName = "Microsoft Corporation" } },
            { "ctfmon", new ProcessMetadataInfo { FriendlyName = "CTF Loader", CompanyName = "Microsoft Corporation" } },
            { "spoolsv", new ProcessMetadataInfo { FriendlyName = "Print Spooler Service", CompanyName = "Microsoft Corporation" } },
            { "msedgewebview2", new ProcessMetadataInfo { FriendlyName = "Microsoft Edge WebView2", CompanyName = "Microsoft Corporation" } },
            { "mc-fw-host", new ProcessMetadataInfo { FriendlyName = "McAfee Core Firewall Host", CompanyName = "McAfee, LLC" } },
            { "mfevtps", new ProcessMetadataInfo { FriendlyName = "McAfee Process Validation Service", CompanyName = "McAfee, LLC" } },
            { "mfevtph", new ProcessMetadataInfo { FriendlyName = "McAfee Host Process", CompanyName = "McAfee, LLC" } },
            { "securityhealthservice", new ProcessMetadataInfo { FriendlyName = "Windows Security Health Service", CompanyName = "Microsoft Corporation" } },
            { "msedge", new ProcessMetadataInfo { FriendlyName = "Microsoft Edge", CompanyName = "Microsoft Corporation" } },
            { "chrome", new ProcessMetadataInfo { FriendlyName = "Google Chrome", CompanyName = "Google LLC" } },
            { "firefox", new ProcessMetadataInfo { FriendlyName = "Mozilla Firefox", CompanyName = "Mozilla Corporation" } },
            { "code", new ProcessMetadataInfo { FriendlyName = "Visual Studio Code", CompanyName = "Microsoft Corporation" } },
            { "devenv", new ProcessMetadataInfo { FriendlyName = "Microsoft Visual Studio", CompanyName = "Microsoft Corporation" } },
            { "claude", new ProcessMetadataInfo { FriendlyName = "Claude Desktop", CompanyName = "Anthropic PBC" } },
            { "slack", new ProcessMetadataInfo { FriendlyName = "Slack", CompanyName = "Slack Technologies" } },
            { "discord", new ProcessMetadataInfo { FriendlyName = "Discord", CompanyName = "Discord Inc." } },
            { "spotify", new ProcessMetadataInfo { FriendlyName = "Spotify", CompanyName = "Spotify AB" } },
            { "teams", new ProcessMetadataInfo { FriendlyName = "Microsoft Teams", CompanyName = "Microsoft Corporation" } }
        };

        public static ProcessMetadataInfo GetMetadata(int pid, string processName)
        {
            if (string.IsNullOrEmpty(processName))
            {
                return new ProcessMetadataInfo { FriendlyName = "Unknown Process" };
            }

            // Check Cache first by processName
            ProcessMetadataInfo cached;
            if (Cache.TryGetValue(processName, out cached))
            {
                return cached;
            }

            // Check pre-baked OS and standard software mapping
            ProcessMetadataInfo prebaked;
            if (PrebakedSystemProcesses.TryGetValue(processName, out prebaked))
            {
                var item = new ProcessMetadataInfo
                {
                    FriendlyName = prebaked.FriendlyName,
                    CompanyName = prebaked.CompanyName,
                    ExecutablePath = string.Empty,
                    FileVersion = string.Empty
                };

                // Attempt to resolve real path in background without failing
                try
                {
                    string path = ProcessManager.GetProcessExecutablePath(pid);
                    if (!string.IsNullOrEmpty(path))
                    {
                        item.ExecutablePath = path;
                        if (File.Exists(path))
                        {
                            var fvi = FileVersionInfo.GetVersionInfo(path);
                            if (!string.IsNullOrEmpty(fvi.FileVersion)) item.FileVersion = fvi.FileVersion;
                        }
                    }
                }
                catch { }

                Cache.TryAdd(processName, item);
                return item;
            }

            // Dynamically resolve via Win32 P/Invoke & FileVersionInfo
            var info = new ProcessMetadataInfo
            {
                FriendlyName = processName,
                CompanyName = string.Empty,
                ExecutablePath = string.Empty,
                FileVersion = string.Empty
            };

            try
            {
                string exePath = ProcessManager.GetProcessExecutablePath(pid);
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                {
                    info.ExecutablePath = exePath;
                    var fvi = FileVersionInfo.GetVersionInfo(exePath);

                    if (!string.IsNullOrWhiteSpace(fvi.FileDescription))
                    {
                        info.FriendlyName = fvi.FileDescription.Trim();
                    }

                    if (!string.IsNullOrWhiteSpace(fvi.CompanyName))
                    {
                        info.CompanyName = fvi.CompanyName.Trim();
                    }

                    if (!string.IsNullOrWhiteSpace(fvi.FileVersion))
                    {
                        info.FileVersion = fvi.FileVersion.Trim();
                    }
                }
            }
            catch { }

            // If friendly name was not found, make it clean capitalized
            if (string.IsNullOrEmpty(info.FriendlyName))
            {
                info.FriendlyName = processName;
            }

            Cache.TryAdd(processName, info);
            return info;
        }
    }
}
