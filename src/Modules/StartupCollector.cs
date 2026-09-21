using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;
using SystemCoreMonitor.Core;
using SystemCoreMonitor.Models;

namespace SystemCoreMonitor.Modules
{
    public class StartupCollector
    {
        private readonly TimedCache<List<StartupItem>> _cache = new TimedCache<List<StartupItem>>(TimeSpan.FromSeconds(60));

        public List<StartupItem> Sample(bool forceRefresh = false)
        {
            List<StartupItem> cached;
            if (_cache.TryGetFresh(forceRefresh, out cached))
            {
                return cached;
            }

            var items = new List<StartupItem>();

            // 1. Current User Registry Run Key
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false))
                {
                    if (key != null)
                    {
                        foreach (var name in key.GetValueNames())
                        {
                            try
                            {
                                string val = key.GetValue(name) as string ?? string.Empty;
                                if (!string.IsNullOrWhiteSpace(val))
                                {
                                    items.Add(BuildStartupItem(name, val, "HKCU"));
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }

            // 2. Local Machine Registry Run Key
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false))
                {
                    if (key != null)
                    {
                        foreach (var name in key.GetValueNames())
                        {
                            try
                            {
                                string val = key.GetValue(name) as string ?? string.Empty;
                                if (!string.IsNullOrWhiteSpace(val))
                                {
                                    items.Add(BuildStartupItem(name, val, "HKLM"));
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }

            // 3. User Startup Folder
            try
            {
                string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                if (!string.IsNullOrEmpty(startupFolder) && Directory.Exists(startupFolder))
                {
                    foreach (var file in Directory.GetFiles(startupFolder))
                    {
                        try
                        {
                            string fileName = Path.GetFileNameWithoutExtension(file);
                            if (!string.Equals(fileName, "desktop", StringComparison.OrdinalIgnoreCase))
                            {
                                items.Add(BuildStartupItem(fileName, file, "Folder"));
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            return items.Count > 0 ? _cache.Store(items) : (_cache.LastValue ?? items);
        }

        private StartupItem BuildStartupItem(string name, string rawCommand, string locationType)
        {
            string exePath = ExtractExecutablePath(rawCommand);
            string displayName = name;
            string publisher = string.Empty;

            // Check if file exists to read FileVersionInfo metadata
            string fviDescription, fviPublisher, fviVersion;
            if (ProcessMetadataCache.TryReadFileVersionInfo(exePath, out fviDescription, out fviPublisher, out fviVersion))
            {
                if (!string.IsNullOrEmpty(fviDescription)) displayName = fviDescription;
                if (!string.IsNullOrEmpty(fviPublisher)) publisher = fviPublisher;
            }

            // Clean common known startup names
            if (name.StartsWith("MicrosoftEdgeAutoLaunch_", StringComparison.OrdinalIgnoreCase))
            {
                displayName = "Microsoft Edge";
                if (string.IsNullOrEmpty(publisher)) publisher = "Microsoft Corporation";
            }
            else if (name.StartsWith("com.squirrel.", StringComparison.OrdinalIgnoreCase))
            {
                var parts = name.Split('.');
                if (parts.Length >= 3)
                {
                    displayName = parts[2];
                }
            }
            else if (string.Equals(name, "GoogleDriveFS", StringComparison.OrdinalIgnoreCase))
            {
                displayName = "Google Drive";
                if (string.IsNullOrEmpty(publisher)) publisher = "Google LLC";
            }
            else if (string.Equals(name, "OneDrive", StringComparison.OrdinalIgnoreCase))
            {
                displayName = "Microsoft OneDrive";
                if (string.IsNullOrEmpty(publisher)) publisher = "Microsoft Corporation";
            }

            // Fallback publisher by common path hints
            if (string.IsNullOrEmpty(publisher) && !string.IsNullOrEmpty(rawCommand))
            {
                string cmdLower = rawCommand.ToLowerInvariant();
                if (cmdLower.Contains("microsoft")) publisher = "Microsoft Corporation";
                else if (cmdLower.Contains("google")) publisher = "Google LLC";
                else if (cmdLower.Contains("adobe")) publisher = "Adobe Inc.";
                else if (cmdLower.Contains("intel")) publisher = "Intel Corporation";
                else if (cmdLower.Contains("docker")) publisher = "Docker Inc.";
            }

            // Location Label
            string locLabel;
            bool isEs = string.Equals(LocalizationManager.CurrentLanguage, "es", StringComparison.OrdinalIgnoreCase);
            if (locationType == "HKCU")
            {
                locLabel = isEs ? "Registro Usuario (HKCU)" : "User Registry (HKCU)";
            }
            else if (locationType == "HKLM")
            {
                locLabel = isEs ? "Registro Sistema (HKLM)" : "System Registry (HKLM)";
            }
            else
            {
                locLabel = isEs ? "Carpeta de Inicio" : "Startup Folder";
            }

            bool isEnabled = CheckIfEnabled(name, locationType);
            string statusLabel = isEnabled
                ? (isEs ? "✓ Habilitado" : "✓ Enabled")
                : (isEs ? "✕ Deshabilitado" : "✕ Disabled");

            return new StartupItem
            {
                Name = name,
                DisplayName = displayName,
                Publisher = publisher,
                Command = rawCommand,
                ExecutablePath = exePath,
                Location = locLabel,
                LocationType = locationType,
                Status = statusLabel,
                IsEnabled = isEnabled
            };
        }

        public static bool CheckIfEnabled(string name, string locationType)
        {
            try
            {
                string subKey = locationType == "Folder"
                    ? @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder"
                    : @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

                RegistryKey root = locationType == "HKLM" ? Registry.LocalMachine : Registry.CurrentUser;
                using (var key = root.OpenSubKey(subKey, false))
                {
                    if (key != null)
                    {
                        var val = key.GetValue(name);
                        if (val is byte[] bytes && bytes.Length > 0)
                        {
                            return (bytes[0] % 2 == 0);
                        }
                    }
                }
            }
            catch { }
            return true;
        }

        public static bool SetStartupItemEnabled(string name, string locationType, bool enable)
        {
            try
            {
                string subKey = locationType == "Folder"
                    ? @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder"
                    : @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

                RegistryKey root = locationType == "HKLM" ? Registry.LocalMachine : Registry.CurrentUser;
                using (var key = root.OpenSubKey(subKey, true) ?? root.CreateSubKey(subKey))
                {
                    if (key != null)
                    {
                        var val = key.GetValue(name);
                        byte[] bytes;
                        if (val is byte[] existing && existing.Length >= 12)
                        {
                            bytes = existing;
                        }
                        else
                        {
                            bytes = new byte[12];
                        }
                        bytes[0] = (byte)(enable ? 2 : 3);
                        key.SetValue(name, bytes, RegistryValueKind.Binary);
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        public static string ExtractExecutablePath(string rawCommand)
        {
            if (string.IsNullOrWhiteSpace(rawCommand)) return string.Empty;
            string cmd = Environment.ExpandEnvironmentVariables(rawCommand.Trim());

            if (cmd.StartsWith("\""))
            {
                int endQuote = cmd.IndexOf('\"', 1);
                if (endQuote > 1)
                {
                    return cmd.Substring(1, endQuote - 1).Trim();
                }
            }

            int exeIdx = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeIdx >= 0)
            {
                return cmd.Substring(0, exeIdx + 4).Trim('\"', ' ');
            }

            // Split by space
            string[] parts = cmd.Split(' ');
            return parts[0].Trim('\"', ' ');
        }
    }
}
