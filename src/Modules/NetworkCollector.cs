using System;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using SimplePCMonitor.Models;

namespace SimplePCMonitor.Modules
{
    public class NetworkCollector
    {
        private long _prevRxBytes;
        private long _prevTxBytes;
        private DateTime _prevTimestamp;
        private bool _initialized;

        public NetworkCollector()
        {
            _prevTimestamp = DateTime.UtcNow;
            Sample();
        }

        public NetworkMetric Sample()
        {
            DateTime now = DateTime.UtcNow;
            double elapsedSec = (now - _prevTimestamp).TotalSeconds;
            if (elapsedSec <= 0) elapsedSec = 1.0;

            long totalRx = 0;
            long totalTx = 0;
            string primaryAdapter = "Disconnected";
            string primaryIp = "N/A";

            try
            {
                NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var nic in interfaces)
                {
                    if (nic.OperationalStatus != OperationalStatus.Up ||
                        nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;

                    try
                    {
                        IPInterfaceStatistics stats = nic.GetIPStatistics();
                        totalRx += stats.BytesReceived;
                        totalTx += stats.BytesSent;

                        if (primaryAdapter == "Disconnected")
                        {
                            primaryAdapter = nic.Name;
                            var ipProps = nic.GetIPProperties();
                            foreach (var u in ipProps.UnicastAddresses)
                            {
                                if (u.Address.AddressFamily == AddressFamily.InterNetwork)
                                {
                                    primaryIp = u.Address.ToString();
                                    break;
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            if (!_initialized)
            {
                _prevRxBytes = totalRx;
                _prevTxBytes = totalTx;
                _prevTimestamp = now;
                _initialized = true;

                return new NetworkMetric
                {
                    DownloadSpeedKbps = 0.0,
                    UploadSpeedKbps   = 0.0,
                    DownloadDisplay   = "0 KB/s",
                    UploadDisplay     = "0 KB/s",
                    TotalRxGB         = Math.Round((double)totalRx / (1024.0 * 1024.0 * 1024.0), 2),
                    TotalTxGB         = Math.Round((double)totalTx / (1024.0 * 1024.0 * 1024.0), 2),
                    AdapterName       = primaryAdapter,
                    IPv4Address       = primaryIp
                };
            }

            long deltaRx = Math.Max(0L, totalRx - _prevRxBytes);
            long deltaTx = Math.Max(0L, totalTx - _prevTxBytes);

            _prevRxBytes = totalRx;
            _prevTxBytes = totalTx;
            _prevTimestamp = now;

            double rxBytesPerSec = deltaRx / elapsedSec;
            double txBytesPerSec = deltaTx / elapsedSec;

            double rxKbps = Math.Round(rxBytesPerSec / 1024.0, 1);
            double txKbps = Math.Round(txBytesPerSec / 1024.0, 1);

            string rxDisplay = rxKbps >= 1024.0 ? string.Format("{0:N1} MB/s", rxKbps / 1024.0) : string.Format("{0:N0} KB/s", rxKbps);
            string txDisplay = txKbps >= 1024.0 ? string.Format("{0:N1} MB/s", txKbps / 1024.0) : string.Format("{0:N0} KB/s", txKbps);

            return new NetworkMetric
            {
                DownloadSpeedKbps = rxKbps,
                UploadSpeedKbps   = txKbps,
                DownloadDisplay   = rxDisplay,
                UploadDisplay     = txDisplay,
                TotalRxGB         = Math.Round((double)totalRx / (1024.0 * 1024.0 * 1024.0), 2),
                TotalTxGB         = Math.Round((double)totalTx / (1024.0 * 1024.0 * 1024.0), 2),
                AdapterName       = primaryAdapter,
                IPv4Address       = primaryIp
            };
        }
    }
}
