using System;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace SimplePCMonitor.Core
{
    public static class StartupHelper
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "SimplePCMonitor";

        public static bool IsRunAtStartupEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null) return false;
                    var val = key.GetValue(AppName);
                    return val != null;
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool SetRunAtStartup(bool enable, bool startMinimized = true)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key == null) return false;

                    if (enable)
                    {
                        string exePath = Assembly.GetExecutingAssembly().Location;
                        string command = string.Format("\"{0}\"{1}", exePath, startMinimized ? " --tray" : string.Empty);
                        key.SetValue(AppName, command);
                    }
                    else
                    {
                        key.DeleteValue(AppName, false);
                    }

                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
