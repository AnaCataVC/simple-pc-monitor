using System;
using System.Runtime.InteropServices;

namespace SimplePCMonitor.Core
{
    public enum PowerSchemeMode
    {
        Balanced,
        HighPerformance,
        PowerSaver
    }

    public static class PowerPlanManager
    {
        public static readonly Guid GuidHighPerformance = new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
        public static readonly Guid GuidBalanced        = new Guid("381b4222-f694-41f0-9685-ff5bb260df2e");
        public static readonly Guid GuidPowerSaver      = new Guid("a1841308-3541-4fab-bc81-f71556f20b4a");

        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LocalFree(IntPtr hMem);

        public static bool SetScheme(PowerSchemeMode mode)
        {
            try
            {
                Guid targetGuid;
                switch (mode)
                {
                    case PowerSchemeMode.HighPerformance:
                        targetGuid = GuidHighPerformance;
                        break;
                    case PowerSchemeMode.PowerSaver:
                        targetGuid = GuidPowerSaver;
                        break;
                    case PowerSchemeMode.Balanced:
                    default:
                        targetGuid = GuidBalanced;
                        break;
                }

                uint result = PowerSetActiveScheme(IntPtr.Zero, ref targetGuid);
                return result == 0;
            }
            catch
            {
                return false;
            }
        }

        public static PowerSchemeMode GetActiveScheme(out string schemeName)
        {
            schemeName = "Balanced";
            try
            {
                IntPtr pGuid;
                uint result = PowerGetActiveScheme(IntPtr.Zero, out pGuid);
                if (result == 0 && pGuid != IntPtr.Zero)
                {
                    Guid activeGuid = (Guid)Marshal.PtrToStructure(pGuid, typeof(Guid));
                    LocalFree(pGuid);

                    if (activeGuid == GuidHighPerformance)
                    {
                        schemeName = "High Performance";
                        return PowerSchemeMode.HighPerformance;
                    }
                    if (activeGuid == GuidPowerSaver)
                    {
                        schemeName = "Power Saver";
                        return PowerSchemeMode.PowerSaver;
                    }
                    if (activeGuid == GuidBalanced)
                    {
                        schemeName = "Balanced";
                        return PowerSchemeMode.Balanced;
                    }
                    schemeName = "Custom Scheme";
                }
            }
            catch
            {
                schemeName = "Balanced";
            }
            return PowerSchemeMode.Balanced;
        }
    }
}
