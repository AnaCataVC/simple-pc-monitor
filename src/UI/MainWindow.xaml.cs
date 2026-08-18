using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using SimplePCMonitor.Core;
using SimplePCMonitor.Models;
using SimplePCMonitor.Modules;

namespace SimplePCMonitor.UI
{
    public partial class MainWindow : Window
    {
        private readonly CpuCollector _cpu;
        private readonly MemoryCollector _mem;
        private readonly DiskCollector _disk;
        private readonly NetworkCollector _net;
        private readonly HardwareCollector _hw;
        private readonly ProcessCollector _proc;
        private readonly ServiceCollector _svc;

        private readonly List<double> _cpuHistory;
        private readonly List<double> _netHistory;
        private const int MaxHistoryPoints = 30;

        private AppConfig _config;
        private CancellationTokenSource _cts;
        private int _cycleCount;

        public MainWindow()
        {
            InitializeComponent();

            _cpu = new CpuCollector();
            _mem = new MemoryCollector();
            _disk = new DiskCollector();
            _net = new NetworkCollector();
            _hw = new HardwareCollector();
            _proc = new ProcessCollector();
            _svc = new ServiceCollector();

            _cpuHistory = new List<double>();
            _netHistory = new List<double>();

            _config = ConfigManager.Load();

            ApplyTheme(_config.Theme);
            LoadAppIcon();

            TxtInterval.Text = string.Format("{0}s", _config.RefreshIntervalSeconds);
            ApplyViewMode(_config.ViewMode);

            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            StartTelemetryLoop();
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }
        }

        private void LoadAppIcon()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string iconPath = System.IO.Path.Combine(baseDir, "icon.png");
                if (!File.Exists(iconPath))
                {
                    iconPath = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "icon.png");
                }

                if (File.Exists(iconPath))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(iconPath, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();

                    this.Icon = bmp;
                    ImgAppLogo.Source = bmp;
                }
            }
            catch { }
        }

        private void StartTelemetryLoop()
        {
            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;

            Task.Factory.StartNew(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    _cycleCount++;

                    var cpuData = _cpu.Sample();
                    var memData = _mem.Sample();
                    var diskData = _disk.Sample();
                    var netData = _net.Sample();
                    var hwData = _hw.Sample();
                    var procData = _proc.Sample(8, memData.TotalGB);

                    ServiceMetric svcData = null;
                    if (_cycleCount % 3 == 1)
                    {
                        svcData = _svc.Sample();
                    }

                    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                    {
                        UpdateUI(cpuData, memData, diskData, netData, hwData, procData, svcData);
                    }));

                    int sleepMs = Math.Max(500, _config.RefreshIntervalSeconds * 1000);
                    try
                    {
                        await Task.Delay(sleepMs, token);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                }
            }, token);
        }

        private void UpdateUI(
            CpuMetric cpu,
            MemoryMetric mem,
            List<DiskMetric> disks,
            NetworkMetric net,
            HardwareMetric hw,
            List<ProcessMetric> procs,
            ServiceMetric svc)
        {
            try
            {
                // CPU
                if (cpu != null)
                {
                    TxtCpuVal.Text = string.Format("{0:N1}%", cpu.LoadPercent);
                    TxtCpuCores.Text = string.Format("{0} Cores", cpu.ProcessorCount);
                    UpdateRadialGaugeArc(ArcCpuSeg, cpu.LoadPercent);

                    _cpuHistory.Add(cpu.LoadPercent);
                    while (_cpuHistory.Count > MaxHistoryPoints) _cpuHistory.RemoveAt(0);

                    if (_cpuHistory.Count > 1)
                    {
                        double peak = _cpuHistory.Max();
                        TxtCpuLivePeak.Text = string.Format("Peak: {0:N1}%", peak);
                        UpdateSparklineWave(LineCpuStroke, PolyCpuArea, _cpuHistory, 440.0, 65.0, 100.0);
                    }
                }

                // RAM
                if (mem != null)
                {
                    TxtRamVal.Text = string.Format("{0:N0}%", mem.LoadPercent);
                    TxtRamSub.Text = string.Format("Used: {0:N1} GB / Free: {1:N1} GB", mem.UsedGB, mem.FreeGB);
                    TxtRamTotalBadge.Text = string.Format("{0:N0} GB", mem.TotalGB);
                    UpdateRadialGaugeArc(ArcRamSeg, mem.LoadPercent);
                }

                // Primary Disk
                if (disks != null && disks.Count > 0)
                {
                    var primary = disks[0];
                    TxtDiskHeader.Text = string.Format("DISK ({0})", primary.Name);
                    TxtDiskVal.Text = string.Format("{0:N0}%", primary.PercentUsed);
                    TxtDiskSub.Text = string.Format("Free: {0:N0} GB / Total: {1:N0} GB", primary.FreeGB, primary.TotalGB);
                    TxtDiskTotalBadge.Text = primary.DriveFormat;
                    UpdateRadialGaugeArc(ArcDiskSeg, primary.PercentUsed);
                    ListDrives.ItemsSource = disks;
                }

                // Network
                if (net != null)
                {
                    TxtNetVal.Text = net.DownloadDisplay;
                    TxtNetSub.Text = string.Format("↓ {0}  ↑ {1}", net.DownloadDisplay, net.UploadDisplay);
                    TxtNetAdapter.Text = net.AdapterName;

                    double netPct = Math.Min(100.0, net.DownloadSpeedKbps / 102.4);
                    UpdateRadialGaugeArc(ArcNetSeg, netPct);

                    _netHistory.Add(net.DownloadSpeedKbps);
                    while (_netHistory.Count > MaxHistoryPoints) _netHistory.RemoveAt(0);

                    if (_netHistory.Count > 1)
                    {
                        double peak = _netHistory.Max();
                        string peakDisp = peak >= 1024.0 ? string.Format("{0:N1} MB/s", peak / 1024.0) : string.Format("{0:N0} KB/s", peak);
                        TxtNetLivePeak.Text = string.Format("Peak: {0}", peakDisp);
                        double dynamicMax = Math.Max(500.0, peak * 1.15);
                        UpdateSparklineWave(LineNetStroke, PolyNetArea, _netHistory, 440.0, 65.0, dynamicMax);
                    }
                }

                // Hardware
                if (hw != null)
                {
                    TxtHwCpu.Text = hw.CpuModel;
                    TxtHwGpu.Text = hw.GpuModel;
                    TxtHwOs.Text = string.Format("{0} ({1})", hw.OsName, hw.OsBuild);
                    TxtUptime.Text = string.Format("Uptime: {0}", hw.UptimeDisplay);

                    if (hw.HasBattery)
                    {
                        TxtBatteryState.Text = string.Format("{0}% ({1})", hw.BatteryPercent, hw.PowerSource);
                    }
                    else
                    {
                        TxtBatteryState.Text = "AC Desktop Power";
                    }
                }

                // Processes
                if (procs != null)
                {
                    ListProcesses.ItemsSource = procs;
                }

                // Services
                if (svc != null)
                {
                    TxtServicesStats.Text = string.Format("Services: {0} Running | {1} Stopped", svc.RunningCount, svc.StoppedCount);
                    ListServices.ItemsSource = svc.CriticalServices;
                }

                TxtStatusTimestamp.Text = "Updated: " + DateTime.Now.ToString("HH:mm:ss");
            }
            catch { }
        }

        private static void UpdateRadialGaugeArc(ArcSegment arc, double percentage, double radius = 38.0, double cx = 50.0, double cy = 50.0)
        {
            if (arc == null) return;
            double pct = Math.Max(0.1, Math.Min(99.99, percentage));
            double angleRad = (pct / 100.0) * 2.0 * Math.PI;
            double x = cx + radius * Math.Sin(angleRad);
            double y = cy - radius * Math.Cos(angleRad);

            arc.Point = new Point(Math.Round(x, 2), Math.Round(y, 2));
            arc.IsLargeArc = pct > 50.0;
        }

        private static void UpdateSparklineWave(Polyline line, Polygon poly, List<double> history, double width, double height, double maxVal)
        {
            if (line == null || poly == null || history == null || history.Count < 2) return;

            int count = history.Count;
            var linePts = new PointCollection(count);
            var polyPts = new PointCollection(count + 2);

            polyPts.Add(new Point(0, height));
            double stepX = width / Math.Max(1.0, (double)(count - 1));
            double effectiveMax = Math.Max(1.0, maxVal);

            for (int i = 0; i < count; i++)
            {
                double val = history[i];
                double normalized = Math.Max(0.0, Math.Min(1.0, val / effectiveMax));
                double x = i * stepX;
                double y = height - (normalized * (height - 12.0)) - 6.0;
                var pt = new Point(Math.Round(x, 1), Math.Round(y, 1));
                linePts.Add(pt);
                polyPts.Add(pt);
            }
            polyPts.Add(new Point(width, height));

            line.Points = linePts;
            poly.Points = polyPts;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnTaskMgr_Click(object sender, RoutedEventArgs e)
        {
            ToolLauncher.StartTaskManager();
        }

        private void BtnResMon_Click(object sender, RoutedEventArgs e)
        {
            ToolLauncher.StartResourceMonitor();
        }

        private void BtnRelMon_Click(object sender, RoutedEventArgs e)
        {
            ToolLauncher.StartReliabilityMonitor();
        }

        private void BtnPCMgr_Click(object sender, RoutedEventArgs e)
        {
            ToolLauncher.StartPCManager();
        }

        private void BtnOpenServicesMsc_Click(object sender, RoutedEventArgs e)
        {
            ToolLauncher.StartServicesConsole();
        }

        private void BtnOpenTaskSchd_Click(object sender, RoutedEventArgs e)
        {
            ToolLauncher.StartTaskScheduler();
        }

        private void BtnToggleTheme_Click(object sender, RoutedEventArgs e)
        {
            string newTheme = string.Equals(_config.Theme, "Dark", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark";
            _config.Theme = newTheme;
            ApplyTheme(newTheme);
            ConfigManager.Save(_config);
        }

        private void ApplyTheme(string theme)
        {
            App.SetTheme(theme);
        }

        private void BtnToggleInterval_Click(object sender, RoutedEventArgs e)
        {
            int newInterval = _config.RefreshIntervalSeconds == 3 ? 5 : 3;
            _config.RefreshIntervalSeconds = newInterval;
            TxtInterval.Text = string.Format("{0}s", newInterval);
            ConfigManager.Save(_config);
        }

        private void BtnToggleView_Click(object sender, RoutedEventArgs e)
        {
            if (_config.ViewMode == "Analytics")
                _config.ViewMode = "Hero";
            else if (_config.ViewMode == "Hero")
                _config.ViewMode = "Widget";
            else
                _config.ViewMode = "Analytics";

            ApplyViewMode(_config.ViewMode);
            ConfigManager.Save(_config);
        }

        private void ApplyViewMode(string mode)
        {
            if (mode == "Hero")
            {
                TxtViewMode.Text = "Hero Mode";
                ContainerLiveWaves.Visibility = Visibility.Visible;
                ContainerDeepDive.Visibility = Visibility.Collapsed;
                Height = 440;
                Width = 980;
            }
            else if (mode == "Widget")
            {
                TxtViewMode.Text = "Mini Widget";
                ContainerLiveWaves.Visibility = Visibility.Collapsed;
                ContainerDeepDive.Visibility = Visibility.Collapsed;
                Height = 220;
                Width = 980;
            }
            else
            {
                TxtViewMode.Text = "Full HUD";
                ContainerLiveWaves.Visibility = Visibility.Visible;
                ContainerDeepDive.Visibility = Visibility.Visible;
                Height = 720;
                Width = 980;
            }
        }

        private void TabBtnProcesses_Click(object sender, RoutedEventArgs e)
        {
            ViewProcesses.Visibility = Visibility.Visible;
            ViewServices.Visibility = Visibility.Collapsed;
            ViewTasks.Visibility = Visibility.Collapsed;
            TxtTabSummary.Text = "Top 8 RAM consumers";
        }

        private void TabBtnServices_Click(object sender, RoutedEventArgs e)
        {
            ViewProcesses.Visibility = Visibility.Collapsed;
            ViewServices.Visibility = Visibility.Visible;
            ViewTasks.Visibility = Visibility.Collapsed;
            TxtTabSummary.Text = "System Services Status";
        }

        private void TabBtnTasks_Click(object sender, RoutedEventArgs e)
        {
            ViewProcesses.Visibility = Visibility.Collapsed;
            ViewServices.Visibility = Visibility.Collapsed;
            ViewTasks.Visibility = Visibility.Visible;
            TxtTabSummary.Text = "Scheduled Tasks";
        }

        private void BtnManualRefresh_Click(object sender, RoutedEventArgs e)
        {
            var cpuData = _cpu.Sample();
            var memData = _mem.Sample();
            var diskData = _disk.Sample();
            var netData = _net.Sample();
            var hwData = _hw.Sample();
            var procData = _proc.Sample(8, memData.TotalGB);
            var svcData = _svc.Sample();

            UpdateUI(cpuData, memData, diskData, netData, hwData, procData, svcData);
        }
    }
}
