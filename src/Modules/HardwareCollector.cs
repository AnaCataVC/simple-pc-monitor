using System;
using Microsoft.Win32;
using SimplePCMonitor.Core;
using SimplePCMonitor.Models;

namespace SimplePCMonitor.Modules
{
    public class HardwareCollector
    {
        private string _computerName;
        private string _osName;
        private string _osBuild;
        private string _cpuModel;
        private string _gpuModel;
        private string _npuModel;
        private bool _staticLoaded;

        public HardwareCollector()
        {
            _computerName = "";
            _osName = "";
            _osBuild = "";
            _cpuModel = "";
            _gpuModel = "";
            _npuModel = "";
            LoadStaticInfo();
        }

        private void LoadStaticInfo()
        {
            if (_staticLoaded) return;

            _computerName = Environment.MachineName;
            _osName = Environment.OSVersion.VersionString;

            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (key != null)
                    {
                        var prodName = key.GetValue("ProductName") as string;
                        var build = key.GetValue("CurrentBuildNumber") as string;
                        if (!string.IsNullOrEmpty(prodName)) _osName = prodName;
                        if (!string.IsNullOrEmpty(build)) _osBuild = build;
                    }
                }
            }
            catch { }

            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                {
                    if (key != null)
                    {
                        var cpu = key.GetValue("ProcessorNameString") as string;
                        if (!string.IsNullOrEmpty(cpu)) _cpuModel = cpu.Trim();
                    }
                }
            }
            catch { }

            try
            {
                var adapters = DxgiHelper.GetAdapters();
                foreach (var a in adapters)
                {
                    if (!a.IsSoftware)
                    {
                        _gpuModel = a.Description;
                        break;
                    }
                }
            }
            catch { }

            try
            {
                var npus = SetupApiHelper.GetNpuDevices();
                if (npus.Count > 0)
                {
                    _npuModel = npus[0].Name;
                }
            }
            catch { }

            if (string.IsNullOrEmpty(_cpuModel)) _cpuModel = "x64 Processor";
            if (string.IsNullOrEmpty(_gpuModel)) _gpuModel = "Integrated / Discrete GPU";
            if (string.IsNullOrEmpty(_npuModel)) _npuModel = "None (Not Detected)";

            _staticLoaded = true;
        }

        public HardwareMetric Sample()
        {
            bool hasBattery = false;
            int batteryPercent = 100;
            bool isCharging = false;
            string powerSource = "AC Power";

            NativeMethods.SYSTEM_POWER_STATUS pwr;
            if (NativeMethods.GetSystemPowerStatus(out pwr))
            {
                if (pwr.BatteryLifePercent != 255 && pwr.BatteryFlag != 128)
                {
                    hasBattery = true;
                    batteryPercent = (int)pwr.BatteryLifePercent;
                    isCharging = pwr.ACLineStatus == 1;
                    powerSource = isCharging ? "AC Charging" : "Battery";
                }
            }

            ulong ticks = NativeMethods.GetTickCount64();
            TimeSpan uptime = TimeSpan.FromMilliseconds(ticks);
            string uptimeDisplay = string.Format("{0}d {1}h {2}m", uptime.Days, uptime.Hours, uptime.Minutes);

            return new HardwareMetric
            {
                ComputerName   = _computerName,
                OsName         = _osName,
                OsBuild        = _osBuild,
                CpuModel       = _cpuModel,
                GpuModel       = _gpuModel,
                NpuModel       = _npuModel,
                HasBattery     = hasBattery,
                BatteryPercent = batteryPercent,
                IsCharging     = isCharging,
                PowerSource    = powerSource,
                UptimeDisplay  = uptimeDisplay
            };
        }
    }
}
