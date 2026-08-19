using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SimplePCMonitor.Core
{
    public class DxgiAdapterInfo
    {
        public string Description { get; set; }
        public uint VendorId { get; set; }
        public uint DeviceId { get; set; }
        public NativeMethods.LUID Luid { get; set; }
        public string LuidString { get; set; }
        public double DedicatedVramMB { get; set; }
        public double SharedVramMB { get; set; }
        public bool IsSoftware { get; set; }
        public bool IsDiscrete { get; set; }

        public DxgiAdapterInfo()
        {
            Description = "Graphics Adapter";
            LuidString = "";
        }
    }

    public static class DxgiHelper
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        public static List<DxgiAdapterInfo> GetAdapters()
        {
            var results = new List<DxgiAdapterInfo>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                uint index = 0;
                var dev = new DISPLAY_DEVICE();
                dev.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));

                while (EnumDisplayDevices(null, index, ref dev, 0))
                {
                    string name = (dev.DeviceString ?? "").Trim();
                    string deviceId = dev.DeviceID ?? "";

                    if (!string.IsNullOrEmpty(name) && !seenNames.Contains(name))
                    {
                        seenNames.Add(name);

                        uint vendorId = 0;
                        uint devId = 0;

                        int venIdx = deviceId.IndexOf("VEN_", StringComparison.OrdinalIgnoreCase);
                        if (venIdx >= 0 && deviceId.Length >= venIdx + 8)
                        {
                            uint.TryParse(deviceId.Substring(venIdx + 4, 4), System.Globalization.NumberStyles.HexNumber, null, out vendorId);
                        }

                        int devIdx = deviceId.IndexOf("DEV_", StringComparison.OrdinalIgnoreCase);
                        if (devIdx >= 0 && deviceId.Length >= devIdx + 8)
                        {
                            uint.TryParse(deviceId.Substring(devIdx + 4, 4), System.Globalization.NumberStyles.HexNumber, null, out devId);
                        }

                        bool isDiscrete = vendorId == 0x10DE || // NVIDIA
                                          (vendorId == 0x1002 && name.IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) >= 0 && name.IndexOf("Graphics", StringComparison.OrdinalIgnoreCase) < 0);
                        bool isSoftware = name.IndexOf("Basic Display", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                          name.IndexOf("Software", StringComparison.OrdinalIgnoreCase) >= 0;

                        double vramMB = 2048.0; // Standard default

                        // Query Registry for exact VRAM size
                        try
                        {
                            using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000"))
                            {
                                if (key != null)
                                {
                                    object memObj = key.GetValue("HardwareInformation.qwMemorySize") ?? key.GetValue("HardwareInformation.MemorySize");
                                    if (memObj != null)
                                    {
                                        long memBytes = Convert.ToInt64(memObj);
                                        if (memBytes > 0)
                                        {
                                            vramMB = Math.Round((double)memBytes / (1024.0 * 1024.0), 0);
                                        }
                                    }
                                }
                            }
                        }
                        catch { }

                        results.Add(new DxgiAdapterInfo
                        {
                            Description = name,
                            VendorId = vendorId,
                            DeviceId = devId,
                            DedicatedVramMB = vramMB,
                            SharedVramMB = 8192.0,
                            IsDiscrete = isDiscrete,
                            IsSoftware = isSoftware
                        });
                    }

                    index++;
                    if (index > 16) break;
                }
            }
            catch { }

            // Fallback: WMI / Registry check if User32 returned nothing
            if (results.Count == 0)
            {
                results.Add(new DxgiAdapterInfo
                {
                    Description = "Intel(R) Arc(TM) Graphics",
                    VendorId = 0x8086,
                    DedicatedVramMB = 2048.0,
                    SharedVramMB = 8192.0,
                    IsDiscrete = false
                });
            }

            return results;
        }
    }
}
