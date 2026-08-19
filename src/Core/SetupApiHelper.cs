using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace SimplePCMonitor.Core
{
    public class NpuDeviceInfo
    {
        public string Name { get; set; }
        public string HardwareId { get; set; }
        public string Service { get; set; }
        public bool IsPresent { get; set; }

        public NpuDeviceInfo()
        {
            Name = "NPU (Neural Processing Unit)";
            HardwareId = "";
            Service = "";
            IsPresent = false;
        }
    }

    public static class SetupApiHelper
    {
        private static readonly Guid GUID_DEVCLASS_COMPUTEACCELERATOR = new Guid("{F01A9D53-3FF6-48D2-9F97-C8A7004BE10C}");

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVINFO_DATA
        {
            public uint cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        private const uint DIGCF_PRESENT = 0x00000002;
        private const uint SPDRP_DEVICEDESC = 0x00000000;
        private const uint SPDRP_HARDWAREID = 0x00000001;
        private const uint SPDRP_SERVICE    = 0x00000004;
        private const uint SPDRP_FRIENDLYNAME = 0x0000000C;

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(
            ref Guid ClassGuid,
            IntPtr Enumerator,
            IntPtr hwndParent,
            uint Flags
        );

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInfo(
            IntPtr DeviceInfoSet,
            uint MemberIndex,
            ref SP_DEVINFO_DATA DeviceInfoData
        );

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetupDiGetDeviceRegistryProperty(
            IntPtr DeviceInfoSet,
            ref SP_DEVINFO_DATA DeviceInfoData,
            uint Property,
            out uint PropertyRegDataType,
            StringBuilder PropertyBuffer,
            uint PropertyBufferSize,
            out uint RequiredSize
        );

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        public static List<NpuDeviceInfo> GetNpuDevices()
        {
            var results = new List<NpuDeviceInfo>();

            try
            {
                Guid classGuid = GUID_DEVCLASS_COMPUTEACCELERATOR;
                IntPtr hDevInfo = SetupDiGetClassDevs(ref classGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT);

                if (hDevInfo != IntPtr.Zero && hDevInfo.ToInt64() != -1)
                {
                    try
                    {
                        SP_DEVINFO_DATA devData = new SP_DEVINFO_DATA();
                        devData.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA));
                        uint index = 0;

                        while (SetupDiEnumDeviceInfo(hDevInfo, index, ref devData))
                        {
                            string friendlyName = GetDeviceProperty(hDevInfo, ref devData, SPDRP_FRIENDLYNAME);
                            string desc = GetDeviceProperty(hDevInfo, ref devData, SPDRP_DEVICEDESC);
                            string hwId = GetDeviceProperty(hDevInfo, ref devData, SPDRP_HARDWAREID);
                            string service = GetDeviceProperty(hDevInfo, ref devData, SPDRP_SERVICE);

                            string bestName = !string.IsNullOrEmpty(friendlyName) ? friendlyName : desc;
                            if (string.IsNullOrEmpty(bestName)) bestName = "Neural Processing Unit (NPU)";

                            results.Add(new NpuDeviceInfo
                            {
                                Name = bestName.Trim(),
                                HardwareId = hwId ?? "",
                                Service = service ?? "",
                                IsPresent = true
                            });

                            index++;
                        }
                    }
                    finally
                    {
                        SetupDiDestroyDeviceInfoList(hDevInfo);
                    }
                }
            }
            catch
            {
                // Fallback via Registry scan
            }

            // Fallback: Registry probe for Intel / AMD / Qualcomm NPU devices if SetupAPI returned 0
            if (results.Count == 0)
            {
                try
                {
                    using (var pciKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\PCI"))
                    {
                        if (pciKey != null)
                        {
                            foreach (var subName in pciKey.GetSubKeyNames())
                            {
                                // Intel Meteor Lake / Lunar Lake NPU (DEV_7D1D, DEV_643E)
                                if (subName.IndexOf("DEV_7D1D", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    subName.IndexOf("DEV_643E", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    results.Add(new NpuDeviceInfo
                                    {
                                        Name = "Intel(R) AI Boost",
                                        HardwareId = subName,
                                        Service = "npu",
                                        IsPresent = true
                                    });
                                    break;
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            return results;
        }

        private static string GetDeviceProperty(IntPtr hDevInfo, ref SP_DEVINFO_DATA devData, uint property)
        {
            uint regType;
            uint reqSize;
            StringBuilder sb = new StringBuilder(512);

            if (SetupDiGetDeviceRegistryProperty(hDevInfo, ref devData, property, out regType, sb, (uint)sb.Capacity, out reqSize))
            {
                return sb.ToString();
            }

            return "";
        }
    }
}
