using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SimplePCMonitor.Core
{
    public class EngineLoadData
    {
        public double TotalLoad { get; set; }
        public double Engine3D { get; set; }
        public double Compute { get; set; }
        public double VideoDecode { get; set; }
        public double VideoProcessing { get; set; }
        public double Copy { get; set; }
    }

    public class WindowsAcceleratorEngine : IDisposable
    {
        private IntPtr _hQuery = IntPtr.Zero;
        private IntPtr _hCounter = IntPtr.Zero;
        private IntPtr _buffer = IntPtr.Zero;
        private uint _bufferSize = 65536; // 64 KB initial buffer
        private bool _initialized;
        private bool _isCounterAvailable = true;

        public WindowsAcceleratorEngine()
        {
            InitializeQuery();
        }

        private void InitializeQuery()
        {
            CloseQuery();

            try
            {
                uint status = NativeMethods.PdhOpenQuery(null, IntPtr.Zero, out _hQuery);
                if (status != NativeMethods.ERROR_SUCCESS || _hQuery == IntPtr.Zero)
                {
                    _isCounterAvailable = false;
                    return;
                }

                status = NativeMethods.PdhAddEnglishCounterW(
                    _hQuery,
                    @"\\GPU Engine(*)\\Utilization Percentage",
                    IntPtr.Zero,
                    out _hCounter
                );

                if (status != NativeMethods.ERROR_SUCCESS || _hCounter == IntPtr.Zero)
                {
                    _isCounterAvailable = false;
                    CloseQuery();
                    return;
                }

                if (_buffer == IntPtr.Zero)
                {
                    _buffer = Marshal.AllocHGlobal((int)_bufferSize);
                }

                // Initial prime sample
                NativeMethods.PdhCollectQueryData(_hQuery);
                _initialized = true;
                _isCounterAvailable = true;
            }
            catch
            {
                _isCounterAvailable = false;
                CloseQuery();
            }
        }

        public Dictionary<string, EngineLoadData> SampleAllEngines()
        {
            var result = new Dictionary<string, EngineLoadData>(StringComparer.OrdinalIgnoreCase);

            if (!_isCounterAvailable)
            {
                return result;
            }

            if (!_initialized || _hQuery == IntPtr.Zero)
            {
                InitializeQuery();
                if (!_initialized) return result;
            }

            try
            {
                uint status = NativeMethods.PdhCollectQueryData(_hQuery);
                if (status != NativeMethods.ERROR_SUCCESS)
                {
                    // Query might be invalid due to display sleep / TDR reset
                    InitializeQuery();
                    return result;
                }

                uint itemCount = 0;
                uint currentBufferSize = _bufferSize;

                status = NativeMethods.PdhGetFormattedCounterArrayW(
                    _hCounter,
                    NativeMethods.PDH_FMT_DOUBLE,
                    ref currentBufferSize,
                    ref itemCount,
                    _buffer
                );

                if (status == NativeMethods.PDH_MORE_DATA)
                {
                    Marshal.FreeHGlobal(_buffer);
                    _bufferSize = currentBufferSize + 4096;
                    _buffer = Marshal.AllocHGlobal((int)_bufferSize);

                    status = NativeMethods.PdhGetFormattedCounterArrayW(
                        _hCounter,
                        NativeMethods.PDH_FMT_DOUBLE,
                        ref _bufferSize,
                        ref itemCount,
                        _buffer
                    );
                }

                if (status != NativeMethods.ERROR_SUCCESS || itemCount == 0)
                {
                    return result;
                }

                int itemSize = Marshal.SizeOf(typeof(NativeMethods.PDH_FMT_COUNTERVALUE_ITEM));
                long currentPtr = _buffer.ToInt64();

                for (uint i = 0; i < itemCount; i++)
                {
                    IntPtr itemAddress = new IntPtr(currentPtr + (i * itemSize));
                    var item = (NativeMethods.PDH_FMT_COUNTERVALUE_ITEM)Marshal.PtrToStructure(
                        itemAddress,
                        typeof(NativeMethods.PDH_FMT_COUNTERVALUE_ITEM)
                    );

                    if (item.szName != IntPtr.Zero && item.Value.CStatus == NativeMethods.ERROR_SUCCESS)
                    {
                        double val = item.Value.doubleValue;
                        if (val > 0.001)
                        {
                            string instanceName = Marshal.PtrToStringUni(item.szName) ?? "";
                            ParseAndAccumulate(instanceName, val, result);
                        }
                    }
                }
            }
            catch
            {
                // Isolate transient exceptions
            }

            return result;
        }

        private static void ParseAndAccumulate(string instanceName, double val, Dictionary<string, EngineLoadData> map)
        {
            // Format: pid_1234_luid_0x00000000_0x00017522_phys_0_eng_0_engtype_3D
            int luidIdx = instanceName.IndexOf("luid_0x", StringComparison.OrdinalIgnoreCase);
            if (luidIdx < 0) return;

            int nextUnderscore = instanceName.IndexOf("_phys_", luidIdx, StringComparison.OrdinalIgnoreCase);
            if (nextUnderscore < 0) nextUnderscore = instanceName.IndexOf("_eng_", luidIdx, StringComparison.OrdinalIgnoreCase);
            if (nextUnderscore < 0) return;

            string luidStr = instanceName.Substring(luidIdx, nextUnderscore - luidIdx).ToLowerInvariant();

            EngineLoadData data;
            if (!map.TryGetValue(luidStr, out data))
            {
                data = new EngineLoadData();
                map[luidStr] = data;
            }

            int engTypeIdx = instanceName.IndexOf("engtype_", StringComparison.OrdinalIgnoreCase);
            string engType = engTypeIdx >= 0 ? instanceName.Substring(engTypeIdx + 8) : "";

            if (engType.StartsWith("3D", StringComparison.OrdinalIgnoreCase))
            {
                data.Engine3D += val;
            }
            else if (engType.StartsWith("Compute", StringComparison.OrdinalIgnoreCase))
            {
                data.Compute += val;
            }
            else if (engType.StartsWith("VideoDecode", StringComparison.OrdinalIgnoreCase))
            {
                data.VideoDecode += val;
            }
            else if (engType.StartsWith("VideoProcessing", StringComparison.OrdinalIgnoreCase))
            {
                data.VideoProcessing += val;
            }
            else if (engType.StartsWith("Copy", StringComparison.OrdinalIgnoreCase))
            {
                data.Copy += val;
            }

            // Task Manager aggregate logic: Max active engine or sum
            data.TotalLoad += val;
        }

        private void CloseQuery()
        {
            _initialized = false;
            if (_hCounter != IntPtr.Zero)
            {
                _hCounter = IntPtr.Zero;
            }
            if (_hQuery != IntPtr.Zero)
            {
                try { NativeMethods.PdhCloseQuery(_hQuery); } catch { }
                _hQuery = IntPtr.Zero;
            }
        }

        public void Dispose()
        {
            CloseQuery();
            if (_buffer != IntPtr.Zero)
            {
                try { Marshal.FreeHGlobal(_buffer); } catch { }
                _buffer = IntPtr.Zero;
            }
        }
    }
}
