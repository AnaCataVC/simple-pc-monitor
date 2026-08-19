using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using SimplePCMonitor.Core;
using SimplePCMonitor.Models;

namespace SimplePCMonitor.Modules
{
    public class StartupCollector
    {
        public List<StartupItem> Sample()
        {
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

            return items;
        }

        private StartupItem BuildStartupItem(string name, string rawCommand, string locationType)
        {
            string exePath = ExtractExecutablePath(rawCommand);
            string displayName = name;
            string publisher = string.Empty;

            // Check if file exists to read FileVersionInfo metadata
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                try
                {
                    var vi = FileVersionInfo.GetVersionInfo(exePath);
                    if (!string.IsNullOrWhiteSpace(vi.FileDescription))
                    {
                        displayName = vi.FileDescription.Trim();
                    }
                    if (!string.IsNullOrWhiteSpace(vi.CompanyName))
                    {
                        publisher = vi.CompanyName.Trim();
                    }
                }
                catch { }
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

            string statusLabel = isEs ? "✓ Habilitado" : "✓ Enabled";

            return new StartupItem
            {
                Name = name,
                DisplayName = displayName,
                Publisher = publisher,
                Command = rawCommand,
                ExecutablePath = exePath,
                Location = locLabel,
                Status = statusLabel
            };
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
