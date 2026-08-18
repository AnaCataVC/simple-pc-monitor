using System;
using System.Diagnostics;

namespace SimplePCMonitor.Core
{
    public static class ToolLauncher
    {
        public static void StartTaskManager()
        {
            try { Process.Start("taskmgr.exe"); } catch { }
        }

        public static void StartResourceMonitor()
        {
            try { Process.Start("resmon.exe"); } catch { }
        }

        public static void StartReliabilityMonitor()
        {
            try
            {
                var psi = new ProcessStartInfo("perfmon.exe", "/rel") { UseShellExecute = true };
                Process.Start(psi);
            }
            catch { }
        }

        public static void StartPCManager()
        {
            try
            {
                var psi = new ProcessStartInfo("ms-pcmanager:") { UseShellExecute = true };
                Process.Start(psi);
            }
            catch
            {
                try
                {
                    var psi = new ProcessStartInfo("ms-settings:storagesense") { UseShellExecute = true };
                    Process.Start(psi);
                }
                catch
                {
                    try { Process.Start("cleanmgr.exe"); } catch { }
                }
            }
        }

        public static void StartServicesConsole()
        {
            try { Process.Start("services.msc"); } catch { }
        }

        public static void StartTaskScheduler()
        {
            try { Process.Start("taskschd.msc"); } catch { }
        }
    }
}
