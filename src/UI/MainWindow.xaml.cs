using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
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
        private readonly GpuCollector _gpu;
        private readonly NpuCollector _npu;
        private readonly WindowsAcceleratorEngine _accelEngine;
        private readonly MemoryCollector _mem;
        private readonly DiskCollector _disk;
        private readonly NetworkCollector _net;
        private readonly HardwareCollector _hw;
        private readonly ProcessCollector _proc;
        private readonly ServiceCollector _svc;
        private readonly StartupCollector _startup;
        private readonly TaskCollector _tasks;
        private readonly TrayManager _trayManager;

        private readonly List<double> _cpuHistory;
        private readonly List<double> _netHistory;
        private const int MaxHistoryPoints = 30;

        private readonly PointCollection _cpuLinePoints = new PointCollection();
        private readonly PointCollection _cpuPolyPoints = new PointCollection();
        private readonly PointCollection _netLinePoints = new PointCollection();
        private readonly PointCollection _netPolyPoints = new PointCollection();

        private AppConfig _config;
        private CancellationTokenSource _cts;
        private int _cycleCount;
        private uint _wmTaskbarCreated;
        private HwndSource _hwndSource;
        private bool _isTrayMode;
        private bool _isExiting;
        private WindowState _lastWindowState = WindowState.Normal;

        // Cached telemetry for snapshots
        private CpuMetric _lastCpu;
        private GpuMetric _lastGpu;
        private NpuMetric _lastNpu;
        private MemoryMetric _lastMem;
        private List<DiskMetric> _lastDisks;
        private NetworkMetric _lastNet;
        private HardwareMetric _lastHw;
        private List<ProcessMetric> _lastProcs;
        private ServiceMetric _lastSvc;
        private List<StartupItem> _lastStartup;
        private List<TaskItem> _lastTasks;

        private DispatcherTimer _toastTimer;
        private Rect? _lastFullBounds;

        public MainWindow()
        {
            InitializeComponent();

            _cpu = new CpuCollector();
            _gpu = new GpuCollector();
            _npu = new NpuCollector();
            _accelEngine = new WindowsAcceleratorEngine();
            _mem = new MemoryCollector();
            _disk = new DiskCollector();
            _net = new NetworkCollector();
            _hw = new HardwareCollector();
            _proc = new ProcessCollector();
            _svc = new ServiceCollector();
            _startup = new StartupCollector();
            _tasks = new TaskCollector();
            _trayManager = new TrayManager();

            _cpuHistory = new List<double>();
            _netHistory = new List<double>();

            _config = ConfigManager.Load();

            ApplyTheme(_config.Theme);
            ApplyLanguage(_config.Language);
            LoadAppIcon();

            TxtInterval.Text = string.Format("{0}s", _config.RefreshIntervalSeconds);
            ApplyViewMode(_config.ViewMode);

            if (_config.AlwaysOnTop)
            {
                Topmost = true;
                PathPin.Fill = (Brush)FindResource("AccentCpu");
                PathWidgetPin.Fill = (Brush)FindResource("AccentCpu");
            }

            string activeScheme;
            PowerPlanManager.GetActiveScheme(out activeScheme);
            TxtActivePowerPlan.Text = activeScheme;
            UpdatePowerButtonsHighlight(activeScheme);

            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
            StateChanged += MainWindow_StateChanged;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                var helper = new WindowInteropHelper(this);
                var hwnd = helper.Handle;

                _hwndSource = HwndSource.FromHwnd(hwnd);
                if (_hwndSource != null)
                {
                    _hwndSource.AddHook(HwndMessageHook);
                }

                _wmTaskbarCreated = NativeMethods.RegisterWindowMessage("TaskbarCreated");
                _trayManager.Initialize(hwnd, "Simple PC Monitor");

                // Check command line arguments for start in tray
                string[] args = Environment.GetCommandLineArgs();
                bool startInTray = false;
                for (int i = 1; i < args.Length; i++)
                {
                    if (string.Equals(args[i], "--tray", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(args[i], "--minimized", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(args[i], "-tray", StringComparison.OrdinalIgnoreCase))
                    {
                        startInTray = true;
                        break;
                    }
                }

                if (startInTray || _config.StartMinimizedToTray)
                {
                    HideToTray();
                }
            }
            catch { }
        }

        private IntPtr HwndMessageHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_TRAYICON)
            {
                int eventId = lParam.ToInt32() & 0xFFFF;
                switch (eventId)
                {
                    case NativeMethods.WM_LBUTTONUP:
                        if (_isTrayMode || !IsVisible)
                        {
                            RestoreFromTray();
                        }
                        else
                        {
                            HideToTray();
                        }
                        handled = true;
                        break;

                    case NativeMethods.WM_LBUTTONDBLCLK:
                        RestoreFromTray();
                        handled = true;
                        break;

                    case NativeMethods.WM_RBUTTONUP:
                    case NativeMethods.WM_CONTEXTMENU:
                        OpenTrayContextMenu(hwnd);
                        handled = true;
                        break;
                }
            }
            else if (msg != 0 && (uint)msg == _wmTaskbarCreated)
            {
                _trayManager.Recreate();
                handled = true;
            }

            return IntPtr.Zero;
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
            }
            if (_accelEngine != null)
            {
                _accelEngine.Dispose();
            }
            if (_hwndSource != null)
            {
                try { _hwndSource.RemoveHook(HwndMessageHook); } catch { }
                _hwndSource = null;
            }
            if (_trayManager != null)
            {
                _trayManager.Dispose();
            }
        }

        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                PathMaximize.Data = (Geometry)FindResource("IconRestore");
            }
            else
            {
                PathMaximize.Data = (Geometry)FindResource("IconMaximize");
            }

            if (WindowState != WindowState.Minimized)
            {
                _lastWindowState = WindowState;
            }
        }

        private void LoadAppIcon()
        {
            try
            {
                string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.png");
                if (File.Exists(iconPath))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(iconPath, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();

                    ImgAppLogo.Source = bitmap;
                    ImgWidgetLogo.Source = bitmap;
                    Icon = bitmap;
                }
                else
                {
                    var uri = new Uri("pack://application:,,,/icon.png", UriKind.Absolute);
                    var bitmap = new BitmapImage(uri);
                    ImgAppLogo.Source = bitmap;
                    ImgWidgetLogo.Source = bitmap;
                    Icon = bitmap;
                }
            }
            catch { }
        }

        // =========================================================================
        // BACKGROUND TELEMETRY LOOP
        // =========================================================================

        private void StartTelemetryLoop()
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            Task.Factory.StartNew(async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            if (_isTrayMode)
                            {
                                // Lightweight pulse: CPU and RAM only (< 0.01ms CPU time)
                                var cpuLite = _cpu.Sample();
                                var memLite = _mem.Sample();

                                string tip = string.Format("Simple PC Monitor\nCPU: {0:F0}% | RAM: {1:F0}%",
                                    cpuLite.LoadPercent, memLite != null ? memLite.LoadPercent : 0.0);
                                _trayManager.UpdateTooltip(tip);

                                // Sleep 5 seconds in background mode to save CPU/battery
                                await Task.Delay(5000, token).ConfigureAwait(false);
                                continue;
                            }

                            var cpu = _cpu.Sample();
                            var engineLoads = _accelEngine.SampleAllEngines();
                            var gpu = _gpu.Sample(engineLoads);
                            var npu = _npu.Sample(engineLoads, gpu.LuidString);
                            var mem = _mem.Sample();
                            var disks = _disk.Sample();
                            var net = _net.Sample();
                            var hw = _hw.Sample();

                            List<ProcessMetric> procs = null;
                            ServiceMetric svc = null;
                            List<TaskItem> tasks = null;
                            List<StartupItem> startup = null;

                            if (_cycleCount % 3 == 0)
                            {
                                procs = _proc.Sample(15, mem != null ? mem.TotalGB : 16.0);
                                svc = _svc.Sample();
                                tasks = _tasks.Sample();
                                startup = _startup.Sample();
                            }
                            _cycleCount++;

                            string activeTip = string.Format("Simple PC Monitor\nCPU: {0:F0}% | RAM: {1:F0}% | GPU: {2:F0}%",
                                cpu.LoadPercent, mem != null ? mem.LoadPercent : 0.0, gpu.LoadPercent);
                            _trayManager.UpdateTooltip(activeTip);

                            await Dispatcher.InvokeAsync(() =>
                            {
                                UpdateUI(cpu, gpu, npu, mem, disks, net, hw, procs, svc, tasks, startup);
                            }, DispatcherPriority.Background);
                        }
                        catch { }

                        int intervalMs = Math.Max(1000, _config.RefreshIntervalSeconds * 1000);
                        await Task.Delay(intervalMs, token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) { }
            }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        // =========================================================================
        // UI BINDING & UPDATE
        // =========================================================================

        private void UpdateUI(
            CpuMetric cpu,
            GpuMetric gpu,
            NpuMetric npu,
            MemoryMetric mem,
            List<DiskMetric> disks,
            NetworkMetric net,
            HardwareMetric hw,
            List<ProcessMetric> procs,
            ServiceMetric svc,
            List<TaskItem> tasks,
            List<StartupItem> startup)
        {
            _lastCpu = cpu;
            _lastGpu = gpu;
            _lastNpu = npu;
            _lastMem = mem;
            _lastDisks = disks;
            _lastNet = net;
            _lastHw = hw;
            if (procs != null) _lastProcs = procs;
            if (svc != null) _lastSvc = svc;
            if (tasks != null) _lastTasks = tasks;
            if (startup != null) _lastStartup = startup;

            // 1. CPU
            if (cpu != null)
            {
                TxtCpuVal.Text = string.Format("{0:N1}%", cpu.LoadPercent);
                ProgressCpu.Value = Math.Max(0, Math.Min(100, cpu.LoadPercent));
                TxtCpuCores.Text = string.Format("{0} Cores", cpu.ProcessorCount);
                TxtCpuCoresBadge.Text = string.Format("{0} Cores", cpu.ProcessorCount);
                TxtCpuSub.Text = hw != null && !string.IsNullOrEmpty(hw.CpuModel) ? hw.CpuModel : "Direct Win32 Kernel";

                // Widget CPU
                TxtWidgetCpu.Text = string.Format("{0:N0}%", cpu.LoadPercent);
                ProgressWidgetCpu.Value = Math.Max(0, Math.Min(100, cpu.LoadPercent));

                // Sparkline
                _cpuHistory.Add(cpu.LoadPercent);
                if (_cpuHistory.Count > MaxHistoryPoints) _cpuHistory.RemoveAt(0);
                double cpuPeak = _cpuHistory.Count > 0 ? _cpuHistory.Max() : 0.0;
                TxtCpuLivePeak.Text = string.Format("{0}: {1:N0}%", LocalizationManager.Get("PeakLabel"), cpuPeak);
                RenderSparkline(LineCpuStroke, PolyCpuArea, _cpuHistory, CanvasCpuGraph, 100.0, _cpuLinePoints, _cpuPolyPoints);
            }

            // 2. GPU
            if (gpu != null && gpu.IsPresent)
            {
                TxtGpuVal.Text = string.Format("{0:N1}%", gpu.LoadPercent);
                ProgressGpu.Value = Math.Max(0, Math.Min(100, gpu.LoadPercent));
                TxtGpuSub.Text = !string.IsNullOrEmpty(gpu.Name) ? gpu.Name : "Graphics Adapter";
                TxtGpuTypeBadge.Text = gpu.IsDiscrete ? LocalizationManager.Get("GpuDiscreteBadge") : LocalizationManager.Get("GpuDirect3DBadge");

                // Accelerators Tab GPU
                TxtGpuTabLoad.Text = string.Format("{0:N1}%", gpu.LoadPercent);
                TxtGpuTabName.Text = gpu.Name;
                TxtGpu3D.Text = string.Format("{0:N1}%", gpu.Engines.Engine3DPercent);
                TxtGpuCompute.Text = string.Format("{0:N1}%", gpu.Engines.ComputePercent);
                TxtGpuVideo.Text = string.Format("{0:N1}%", gpu.Engines.VideoDecodePercent);
                TxtGpuCopy.Text = string.Format("{0:N1}%", gpu.Engines.CopyPercent);
                TxtGpuVram.Text = string.Format("{0:N0} MB / {1:N0} MB", gpu.DedicatedVramUsedMB, gpu.DedicatedVramTotalMB);
                ProgressGpuVram.Value = gpu.DedicatedVramTotalMB > 0 ? Math.Min(100, (gpu.DedicatedVramUsedMB / gpu.DedicatedVramTotalMB) * 100.0) : 0;

                // Widget GPU
                TxtWidgetGpu.Text = string.Format("{0:N0}%", gpu.LoadPercent);
                ProgressWidgetGpu.Value = Math.Max(0, Math.Min(100, gpu.LoadPercent));
            }

            // 3. NPU (AI)
            if (npu != null && npu.IsPresent)
            {
                CardNpu.Visibility = Visibility.Visible;
                TxtNpuVal.Text = string.Format("{0:N1}%", npu.LoadPercent);
                ProgressNpu.Value = Math.Max(0, Math.Min(100, npu.LoadPercent));
                TxtNpuStatusBadge.Text = npu.LoadPercent > 0.5 ? LocalizationManager.Get("NpuActiveBadge") : LocalizationManager.Get("NpuIdleBadge");
                TxtNpuSub.Text = !string.IsNullOrEmpty(npu.Name) ? npu.Name : "Intel AI Boost";

                // Accelerators Tab NPU
                TxtNpuTabStatus.Text = string.Format("{0} ({1:N1}%)", npu.LoadPercent > 0.5 ? LocalizationManager.Get("NpuActiveBadge") : LocalizationManager.Get("NpuIdleBadge"), npu.LoadPercent);
                TxtNpuTabName.Text = npu.Name;
                TxtNpuTabLoad.Text = string.Format("{0:N1}%", npu.LoadPercent);
                ProgressNpuTab.Value = Math.Max(0, Math.Min(100, npu.LoadPercent));
            }
            else
            {
                TxtNpuVal.Text = "0.0%";
                ProgressNpu.Value = 0;
                TxtNpuStatusBadge.Text = "N/A";
                TxtNpuSub.Text = LocalizationManager.Get("NpuNotDetected");
                TxtNpuTabStatus.Text = LocalizationManager.Get("NpuNotDetected");
                TxtNpuTabLoad.Text = "0.0%";
                ProgressNpuTab.Value = 0;
            }

            // 4. RAM
            if (mem != null)
            {
                TxtRamVal.Text = string.Format("{0:N0}%", mem.LoadPercent);
                ProgressRam.Value = Math.Max(0, Math.Min(100, mem.LoadPercent));
                TxtRamTotalBadge.Text = string.Format("{0:N0} GB", mem.TotalGB);
                TxtRamSub.Text = string.Format(LocalizationManager.Get("RamUsedLabel"), mem.UsedGB, mem.TotalGB);

                // Widget RAM
                TxtWidgetRam.Text = string.Format("{0:N0}%", mem.LoadPercent);
                ProgressWidgetRam.Value = Math.Max(0, Math.Min(100, mem.LoadPercent));
            }

            // 5. DISK
            if (disks != null && disks.Count > 0)
            {
                var primaryDisk = disks[0];
                TxtDiskVal.Text = string.Format("{0:N0}%", primaryDisk.PercentUsed);
                ProgressDisk.Value = Math.Max(0, Math.Min(100, primaryDisk.PercentUsed));
                TxtDiskTotalBadge.Text = primaryDisk.Name;
                TxtDiskSub.Text = string.Format(LocalizationManager.Get("DiskFreeLabel"), primaryDisk.FreeGB);

                ListDrives.ItemsSource = disks;
            }

            // 6. NETWORK
            if (net != null)
            {
                TxtNetVal.Text = net.DownloadDisplay;
                TxtNetPingBadge.Text = net.PingDisplay;
                TxtNetSub.Text = string.Format("↓ {0}  ↑ {1}", net.DownloadDisplay, net.UploadDisplay);

                // Widget Net
                TxtWidgetNet.Text = net.DownloadDisplay;
                double netProgress = Math.Min(100, (net.DownloadSpeedKbps / 5000.0) * 100.0);
                ProgressWidgetNet.Value = Math.Max(0, netProgress);

                // Sparkline
                _netHistory.Add(net.DownloadSpeedKbps);
                if (_netHistory.Count > MaxHistoryPoints) _netHistory.RemoveAt(0);
                double netPeak = _netHistory.Count > 0 ? _netHistory.Max() : 0.0;
                TxtNetLivePeak.Text = string.Format("{0}: {1:N0} KB/s", LocalizationManager.Get("PeakLabel"), netPeak);
                RenderSparkline(LineNetStroke, PolyNetArea, _netHistory, CanvasNetGraph, Math.Max(500.0, netPeak * 1.1), _netLinePoints, _netPolyPoints);
            }

            // 7. HARDWARE OVERVIEW
            if (hw != null)
            {
                TxtHwCpu.Text = !string.IsNullOrEmpty(hw.CpuModel) ? hw.CpuModel : "x64 Processor";
                TxtHwGpu.Text = !string.IsNullOrEmpty(hw.GpuModel) ? hw.GpuModel : (gpu != null ? gpu.Name : "GPU");
                TxtHwNpu.Text = !string.IsNullOrEmpty(hw.NpuModel) ? hw.NpuModel : (npu != null && npu.IsPresent ? npu.Name : LocalizationManager.Get("NpuNotDetected"));
                TxtHwOs.Text = !string.IsNullOrEmpty(hw.OsName) ? hw.OsName : "Windows";
                TxtBatteryState.Text = hw.PowerSource;
                TxtUptime.Text = string.Format("{0}: {1}", LocalizationManager.Get("UptimeLabel"), hw.UptimeDisplay);
            }

            // 8. PROCESSES, SERVICES, TASKS, STARTUP (periodic)
            if (procs != null)
            {
                ListProcesses.ItemsSource = procs;
            }
            if (svc != null && svc.CriticalServices != null)
            {
                ListServices.ItemsSource = svc.CriticalServices;
            }
            if (tasks != null)
            {
                ListTasks.ItemsSource = tasks;
            }
            if (startup != null)
            {
                ListStartup.ItemsSource = startup;
            }
        }

        // =========================================================================
        // ZERO-ALLOCATION SPARKLINE GRAPH RENDERING
        // =========================================================================

        private void RenderSparkline(
            Polyline line,
            Polygon poly,
            List<double> history,
            Canvas canvas,
            double maxVal,
            PointCollection linePts,
            PointCollection polyPts)
        {
            if (canvas == null || history == null || history.Count < 2) return;

            double width = canvas.ActualWidth > 10 ? canvas.ActualWidth : canvas.Width;
            double height = canvas.ActualHeight > 10 ? canvas.ActualHeight : canvas.Height;
            if (double.IsNaN(width) || width <= 0) width = 340.0;
            if (double.IsNaN(height) || height <= 0) height = 50.0;

            int count = history.Count;
            double stepX = width / Math.Max(1.0, count - 1);
            double effectiveMax = Math.Max(1.0, maxVal);

            linePts.Clear();
            polyPts.Clear();

            polyPts.Add(new Point(0, height));
            for (int i = 0; i < count; i++)
            {
                double normalized = Math.Max(0.0, Math.Min(1.0, history[i] / effectiveMax));
                double x = i * stepX;
                double y = height - (normalized * (height - 8.0)) - 4.0;
                var pt = new Point(Math.Round(x, 1), Math.Round(y, 1));
                linePts.Add(pt);
                polyPts.Add(pt);
            }
            polyPts.Add(new Point(width, height));

            line.Points = linePts;
            poly.Points = polyPts;
        }

        // =========================================================================
        // VIEW MODE SWITCHER & DYNAMIC WINDOW SIZING
        // =========================================================================

        private void BtnToggleView_Click(object sender, RoutedEventArgs e)
        {
            MenuViewModes.PlacementTarget = BtnToggleView;
            MenuViewModes.IsOpen = true;
        }

        private void MenuViewFull_Click(object sender, RoutedEventArgs e)
        {
            _config.ViewMode = "Full";
            ApplyViewMode(_config.ViewMode);
            ConfigManager.Save(_config);
            ShowToast(LocalizationManager.Get("MenuFullDesc"));
        }

        private void MenuViewHero_Click(object sender, RoutedEventArgs e)
        {
            _config.ViewMode = "Hero";
            ApplyViewMode(_config.ViewMode);
            ConfigManager.Save(_config);
            ShowToast(LocalizationManager.Get("MenuHeroDesc"));
        }

        private void MenuViewWidget_Click(object sender, RoutedEventArgs e)
        {
            _config.ViewMode = "Widget";
            ApplyViewMode(_config.ViewMode);
            ConfigManager.Save(_config);
            ShowToast(LocalizationManager.Get("MenuWidgetDesc"));
        }

        private void Widget_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && e.ChangedButton == MouseButton.Left)
            {
                MenuViewFull_Click(sender, e);
            }
        }

        private void MenuWidgetSnap_Click(object sender, RoutedEventArgs e)
        {
            WindowPlacementHelper.SnapToBottomRight(this);
            ShowToast(LocalizationManager.Get("ToastWidgetDocked"));
        }

        private void ApplyViewMode(string mode)
        {
            if (WindowState == WindowState.Maximized && (mode == "Hero" || mode == "Widget"))
            {
                WindowState = WindowState.Normal;
            }

            if (mode == "Widget")
            {
                // Remember Full/Hero bounds before shrinking
                if (ContainerMainView != null && ContainerMainView.Visibility == Visibility.Visible && Width > 460)
                {
                    _lastFullBounds = new Rect(Left, Top, Width, Height);
                }

                TxtViewMode.Text = LocalizationManager.Get("ViewWidget");

                // 1. Relax minimum constraints first
                MinWidth = 320;
                MinHeight = 80;
                MaxWidth = 460;
                MaxHeight = 120;

                // 2. Adjust visibility
                ContainerMainView.Visibility = Visibility.Collapsed;
                ContainerWidgetView.Visibility = Visibility.Visible;

                // 3. Set exact physical dimensions
                Width = 380;
                Height = 88;
                ResizeMode = ResizeMode.NoResize;

                // 4. Snap to bottom-right of current screen
                WindowPlacementHelper.SnapToBottomRight(this);
            }
            else if (mode == "Hero")
            {
                TxtViewMode.Text = LocalizationManager.Get("ViewHero");

                // 1. Set constraints
                MinWidth = 760;
                MinHeight = 320;
                MaxWidth = double.PositiveInfinity;
                MaxHeight = double.PositiveInfinity;

                // 2. Adjust visibility
                ContainerMainView.Visibility = Visibility.Visible;
                ContainerWidgetView.Visibility = Visibility.Collapsed;
                ContainerRibbon.Visibility = Visibility.Visible;
                ContainerHeroGrid.Visibility = Visibility.Visible;
                ContainerLiveWaves.Visibility = Visibility.Visible;
                ContainerDeepDive.Visibility = Visibility.Collapsed;
                ContainerFooter.Visibility = Visibility.Collapsed;

                // 3. Set exact physical dimensions and safe monitor clamp
                WindowPlacementHelper.ClampWindowToMonitor(this, 840, 360, _lastFullBounds);
                Width = 840;
                Height = 360;
                ResizeMode = ResizeMode.CanResize;
            }
            else
            {
                TxtViewMode.Text = LocalizationManager.Get("ViewFull");

                // 1. Set constraints
                MinWidth = 920;
                MinHeight = 620;
                MaxWidth = double.PositiveInfinity;
                MaxHeight = double.PositiveInfinity;

                // 2. Adjust visibility
                ContainerMainView.Visibility = Visibility.Visible;
                ContainerWidgetView.Visibility = Visibility.Collapsed;
                ContainerRibbon.Visibility = Visibility.Visible;
                ContainerHeroGrid.Visibility = Visibility.Visible;
                ContainerLiveWaves.Visibility = Visibility.Visible;
                ContainerDeepDive.Visibility = Visibility.Visible;
                ContainerFooter.Visibility = Visibility.Visible;

                // 3. Set exact physical dimensions and safe monitor clamp
                WindowPlacementHelper.ClampWindowToMonitor(this, 1040, 720, _lastFullBounds);
                Width = 1040;
                Height = 720;
                ResizeMode = ResizeMode.CanResize;
            }
        }

        // =========================================================================
        // TAB SWITCHING IN DEEP DIVE WITH ACTIVE HIGHLIGHT
        // =========================================================================

        private void TabBtnProcesses_Click(object sender, RoutedEventArgs e)
        {
            ShowTab(ViewProcesses, TabBtnProcesses);
        }

        private void TabBtnAccelerators_Click(object sender, RoutedEventArgs e)
        {
            ShowTab(ViewAccelerators, TabBtnAccelerators);
        }

        private void TabBtnServices_Click(object sender, RoutedEventArgs e)
        {
            ShowTab(ViewServices, TabBtnServices);
        }

        private void TabBtnTasks_Click(object sender, RoutedEventArgs e)
        {
            ShowTab(ViewTasks, TabBtnTasks);
        }

        private void TabBtnStartup_Click(object sender, RoutedEventArgs e)
        {
            ShowTab(ViewStartup, TabBtnStartup);
        }

        private void ShowTab(Grid tabView, Button activeBtn)
        {
            ViewProcesses.Visibility = Visibility.Collapsed;
            ViewAccelerators.Visibility = Visibility.Collapsed;
            ViewServices.Visibility = Visibility.Collapsed;
            ViewTasks.Visibility = Visibility.Collapsed;
            ViewStartup.Visibility = Visibility.Collapsed;

            tabView.Visibility = Visibility.Visible;
            UpdateActiveTabHighlight(activeBtn);
        }

        private void UpdateActiveTabHighlight(Button activeBtn)
        {
            var defaultStyle = (Style)FindResource("TabHeaderButtonStyle");
            var activeStyle = (Style)FindResource("ActiveTabHeaderButtonStyle");

            if (TabBtnProcesses != null) TabBtnProcesses.Style = defaultStyle;
            if (TabBtnAccelerators != null) TabBtnAccelerators.Style = defaultStyle;
            if (TabBtnServices != null) TabBtnServices.Style = defaultStyle;
            if (TabBtnTasks != null) TabBtnTasks.Style = defaultStyle;
            if (TabBtnStartup != null) TabBtnStartup.Style = defaultStyle;

            if (activeBtn != null)
            {
                activeBtn.Style = activeStyle;
            }
        }

        // =========================================================================
        // LANGUAGE LOCALIZATION CONTROLLER (ES / EN)
        // =========================================================================

        private void BtnToggleLang_Click(object sender, RoutedEventArgs e)
        {
            MenuLanguages.PlacementTarget = BtnToggleLang;
            MenuLanguages.IsOpen = true;
        }

        private void MenuLangEs_Click(object sender, RoutedEventArgs e)
        {
            SetLanguage("es");
        }

        private void MenuLangEn_Click(object sender, RoutedEventArgs e)
        {
            SetLanguage("en");
        }

        private void SetLanguage(string lang)
        {
            _config.Language = lang;
            ApplyLanguage(lang);
            ConfigManager.Save(_config);
            ShowToast(lang == "es" ? "Idioma: Español" : "Language: English");
        }

        private void ApplyLanguage(string lang)
        {
            if (string.IsNullOrEmpty(lang)) lang = "es";
            _config.Language = lang;
            LocalizationManager.CurrentLanguage = lang;

            if (TxtCurrentLang != null) TxtCurrentLang.Text = lang.ToUpper();

            // Ribbon & Actions
            if (TxtBtnOptimize != null) TxtBtnOptimize.Text = LocalizationManager.Get("TrimRam");
            if (BtnOptimize != null) BtnOptimize.ToolTip = LocalizationManager.Get("TrimRamTooltip");
            if (TxtBtnCleanTemp != null) TxtBtnCleanTemp.Text = LocalizationManager.Get("CleanTemp");
            if (BtnCleanTemp != null) BtnCleanTemp.ToolTip = LocalizationManager.Get("CleanTempTooltip");
            if (TxtBtnSnapshot != null) TxtBtnSnapshot.Text = LocalizationManager.Get("Snapshot");
            if (BtnSnapshotTop != null) BtnSnapshotTop.ToolTip = LocalizationManager.Get("SnapshotTooltip");

            // Power Plans
            if (BtnPlanSaver != null) { BtnPlanSaver.Content = LocalizationManager.Get("PowerPlanSaver"); BtnPlanSaver.ToolTip = LocalizationManager.Get("PowerPlanSaverTooltip"); }
            if (BtnPlanBalanced != null) { BtnPlanBalanced.Content = LocalizationManager.Get("PowerPlanBalanced"); BtnPlanBalanced.ToolTip = LocalizationManager.Get("PowerPlanBalancedTooltip"); }
            if (BtnPlanHighPerf != null) { BtnPlanHighPerf.Content = LocalizationManager.Get("PowerPlanHighPerf"); BtnPlanHighPerf.ToolTip = LocalizationManager.Get("PowerPlanHighPerfTooltip"); }

            // Tools Menu
            if (TxtBtnTools != null) TxtBtnTools.Text = LocalizationManager.Get("Tools") + " ▾";
            if (BtnToolsMenu != null) BtnToolsMenu.ToolTip = LocalizationManager.Get("ToolsTooltip");
            if (MenuToolsTaskMgr != null) MenuToolsTaskMgr.Header = LocalizationManager.Get("ToolTaskMgr");
            if (MenuToolsResMon != null) MenuToolsResMon.Header = LocalizationManager.Get("ToolResMon");
            if (MenuToolsStorageSense != null) MenuToolsStorageSense.Header = LocalizationManager.Get("ToolStorageSense");
            if (MenuToolsServices != null) MenuToolsServices.Header = LocalizationManager.Get("ToolServices");

            // View Modes
            if (BtnToggleView != null) BtnToggleView.ToolTip = LocalizationManager.Get("ViewModeTooltip");
            if (MenuViewFullItem != null) MenuViewFullItem.Header = LocalizationManager.Get("MenuFullDesc");
            if (MenuViewHeroItem != null) MenuViewHeroItem.Header = LocalizationManager.Get("MenuHeroDesc");
            if (MenuViewWidgetItem != null) MenuViewWidgetItem.Header = LocalizationManager.Get("MenuWidgetDesc");
            if (TxtViewMode != null)
            {
                if (_config.ViewMode == "Widget") TxtViewMode.Text = LocalizationManager.Get("ViewWidget");
                else if (_config.ViewMode == "Hero") TxtViewMode.Text = LocalizationManager.Get("ViewHero");
                else TxtViewMode.Text = LocalizationManager.Get("ViewFull");
            }

            // Themes Menu
            if (BtnToggleTheme != null) BtnToggleTheme.ToolTip = LocalizationManager.Get("ThemeTooltip");
            if (MenuThemeDarkItem != null) MenuThemeDarkItem.Header = LocalizationManager.Get("ThemeDark");
            if (MenuThemeLightItem != null) MenuThemeLightItem.Header = LocalizationManager.Get("ThemeLight");
            if (MenuThemeNeonItem != null) MenuThemeNeonItem.Header = LocalizationManager.Get("ThemeNeon");
            if (MenuThemeRoseItem != null) MenuThemeRoseItem.Header = LocalizationManager.Get("ThemeRose");

            // Language, Interval & Pin
            if (BtnToggleLang != null) BtnToggleLang.ToolTip = LocalizationManager.Get("LangTooltip");
            if (BtnToggleInterval != null) BtnToggleInterval.ToolTip = LocalizationManager.Get("IntervalTooltip");
            if (BtnPinTop != null) BtnPinTop.ToolTip = Topmost ? LocalizationManager.Get("Unpin") : LocalizationManager.Get("PinAlwaysOnTop");
            if (BorderUptime != null) BorderUptime.ToolTip = LocalizationManager.Get("UptimeTooltip");

            // Bento Cards
            if (TxtCardCpuTitle != null) TxtCardCpuTitle.Text = LocalizationManager.Get("CardCpuTitle");
            if (TxtCardGpuTitle != null) TxtCardGpuTitle.Text = LocalizationManager.Get("CardGpuTitle");
            if (TxtCardNpuTitle != null) TxtCardNpuTitle.Text = LocalizationManager.Get("CardNpuTitle");
            if (TxtCardRamTitle != null) TxtCardRamTitle.Text = LocalizationManager.Get("CardRamTitle");
            if (TxtCardDiskTitle != null) TxtCardDiskTitle.Text = LocalizationManager.Get("CardDiskTitle");
            if (TxtCardNetTitle != null) TxtCardNetTitle.Text = LocalizationManager.Get("CardNetTitle");

            // Live Wave Sparklines
            if (TxtWaveCpuTitle != null) TxtWaveCpuTitle.Text = LocalizationManager.Get("WaveCpuTitle");
            if (TxtWaveNetTitle != null) TxtWaveNetTitle.Text = LocalizationManager.Get("WaveNetTitle");

            // Hardware & Storage Deck
            if (TxtHwDeckTitle != null) TxtHwDeckTitle.Text = LocalizationManager.Get("CardHardwareTitle", "SISTEMA Y HARDWARE");
            if (TxtHwCpuLabel != null) TxtHwCpuLabel.Text = LocalizationManager.Get("HwProcessor", "PROCESADOR");
            if (TxtHwGpuLabel != null) TxtHwGpuLabel.Text = LocalizationManager.Get("HwGraphics", "ADAPTADOR GRÁFICO");
            if (TxtHwNpuLabel != null) TxtHwNpuLabel.Text = LocalizationManager.Get("HwNpu", "MOTOR IA (NPU)");
            if (TxtHwOsLabel != null) TxtHwOsLabel.Text = LocalizationManager.Get("HwOsLabel", "SISTEMA OPERATIVO");
            if (TxtHwPowerSchemeLabel != null) TxtHwPowerSchemeLabel.Text = LocalizationManager.Get("HwPowerSchemeLabel", "PLAN DE ENERGÍA");
            if (TxtStorageDeckTitle != null) TxtStorageDeckTitle.Text = LocalizationManager.Get("StorageVolumes", "UNIDADES DE ALMACENAMIENTO");

            // Deep Dive Tabs
            if (TabBtnProcesses != null) TabBtnProcesses.Content = LocalizationManager.Get("TabProcesses");
            if (TabBtnAccelerators != null) TabBtnAccelerators.Content = LocalizationManager.Get("TabAccelerators");
            if (TabBtnServices != null) TabBtnServices.Content = LocalizationManager.Get("TabServices");
            if (TabBtnTasks != null) TabBtnTasks.Content = LocalizationManager.Get("TabTasks");
            if (TabBtnStartup != null) TabBtnStartup.Content = LocalizationManager.Get("TabStartup");

            // Table & Column Headers
            if (TxtColPid != null) TxtColPid.Text = LocalizationManager.Get("ColPid", "PID");
            if (TxtColApp != null) TxtColApp.Text = LocalizationManager.Get("ColApp", "APLICACIÓN");
            if (TxtColWorkingSet != null) TxtColWorkingSet.Text = LocalizationManager.Get("ColWorkingSet", "MEMORIA");
            if (TxtColRamPercent != null) TxtColRamPercent.Text = LocalizationManager.Get("ColRamPercent", "% RAM");
            if (TxtColActions != null) TxtColActions.Text = LocalizationManager.Get("ColActions", "ACCIONES");

            if (TxtColServiceName != null) TxtColServiceName.Text = LocalizationManager.Get("ColServiceName", "SERVICIO DE WINDOWS");
            if (TxtColServiceStatus != null) TxtColServiceStatus.Text = LocalizationManager.Get("ColServiceStatus", "ESTADO");

            if (TxtColTaskName != null) TxtColTaskName.Text = LocalizationManager.Get("ColTaskName", "TAREA PROGRAMADA");
            if (TxtColTaskState != null) TxtColTaskState.Text = LocalizationManager.Get("ColTaskState", "ESTADO");

            if (TxtColStartupApp != null) TxtColStartupApp.Text = LocalizationManager.Get("ColStartupApp", "APLICACIÓN / PROGRAMA");
            if (TxtColStartupLocation != null) TxtColStartupLocation.Text = LocalizationManager.Get("ColStartupLocation", "ORIGEN");
            if (TxtColStartupStatus != null) TxtColStartupStatus.Text = LocalizationManager.Get("ColStartupStatus", "ESTADO");
            if (TxtColStartupActions != null) TxtColStartupActions.Text = LocalizationManager.Get("ColStartupActions", "ACCIONES");

            // Accelerators Tab
            if (TxtGpuDeckTitle != null) TxtGpuDeckTitle.Text = LocalizationManager.Get("GpuDeckTitle");
            if (TxtGpu3DTitle != null) TxtGpu3DTitle.Text = LocalizationManager.Get("Gpu3DRendering");
            if (TxtGpuComputeTitle != null) TxtGpuComputeTitle.Text = LocalizationManager.Get("GpuComputeML");
            if (TxtGpuVideoTitle != null) TxtGpuVideoTitle.Text = LocalizationManager.Get("GpuVideoDecode");
            if (TxtGpuCopyTitle != null) TxtGpuCopyTitle.Text = LocalizationManager.Get("GpuCopyEngine");
            if (TxtGpuVramTitle != null) TxtGpuVramTitle.Text = LocalizationManager.Get("GpuVramTitle");
            if (TxtNpuDeckTitle != null) TxtNpuDeckTitle.Text = LocalizationManager.Get("NpuDeckTitle");
            if (TxtNpuDeckDesc != null) TxtNpuDeckDesc.Text = LocalizationManager.Get("NpuDeckDesc");
            if (TxtNpuComputeTitle != null) TxtNpuComputeTitle.Text = LocalizationManager.Get("NpuComputeUtilization");

            // Widget View
            if (MenuWidgetRestore != null) MenuWidgetRestore.Header = LocalizationManager.Get("WidgetRestore");
            if (MenuWidgetHero != null) MenuWidgetHero.Header = LocalizationManager.Get("WidgetSwitchHero");
            if (MenuWidgetSnap != null) MenuWidgetSnap.Header = LocalizationManager.Get("WidgetSnapBottomRight");
            if (MenuWidgetPin != null) MenuWidgetPin.Header = LocalizationManager.Get("WidgetPinAlways");
            if (MenuWidgetClose != null) MenuWidgetClose.Header = LocalizationManager.Get("WidgetClose");
            if (ContainerWidgetView != null) ContainerWidgetView.ToolTip = LocalizationManager.Get("WidgetTooltip");
            if (BtnWidgetExpand != null) BtnWidgetExpand.ToolTip = LocalizationManager.Get("WidgetRestore");
            if (BtnWidgetPin != null) BtnWidgetPin.ToolTip = LocalizationManager.Get("PinAlwaysOnTop");
            if (BtnWidgetClose != null) BtnWidgetClose.ToolTip = LocalizationManager.Get("Close");
        }

        // =========================================================================
        // QUICK ACTIONS (TRIM RAM, CLEAN TEMP, SNAPSHOT)
        // =========================================================================

        private void BtnOptimize_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int trimmedCount;
                double freedMB = MemoryOptimizer.OptimizeWorkingSet(out trimmedCount);
                ShowToast(string.Format("⚡ RAM Optimized! ({0} processes trimmed)", trimmedCount));
            }
            catch (Exception ex)
            {
                ShowToast("Error trimming RAM: " + ex.Message);
            }
        }

        private void BtnCleanTemp_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var res = SafeTempCleaner.CleanTempFiles();
                ShowToast(string.Format("🧹 Cleaned {0} Temp Files ({1} items)", res.HumanSize, res.FilesDeleted));
            }
            catch (Exception ex)
            {
                ShowToast("Error cleaning temp: " + ex.Message);
            }
        }

        private void BtnSnapshot_Click(object sender, RoutedEventArgs e)
        {
            bool ok = SnapshotExporter.CopySnapshotToClipboard(
                _lastCpu,
                _lastGpu,
                _lastNpu,
                _lastMem,
                _lastDisks,
                _lastNet,
                _lastHw,
                _lastProcs,
                _lastSvc
            );

            if (ok)
            {
                ShowToast("📸 Diagnostic Snapshot Copied to Clipboard!");
            }
            else
            {
                ShowToast("Failed to copy snapshot");
            }
        }

        private void ShowToast(string message)
        {
            if (TxtFooterLeft != null)
            {
                TxtFooterLeft.Text = message;
                TxtFooterLeft.Foreground = (Brush)FindResource("AccentCpu");
            }

            if (_toastTimer == null)
            {
                _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                _toastTimer.Tick += (s, ev) =>
                {
                    _toastTimer.Stop();
                    if (TxtFooterLeft != null)
                    {
                        TxtFooterLeft.Text = "⚡ Native Zero-Dependency Telemetry";
                        TxtFooterLeft.Foreground = (Brush)FindResource("TextMuted");
                    }
                };
            }
            _toastTimer.Stop();
            _toastTimer.Start();
        }

        // =========================================================================
        // POWER PLAN CONTROLLER
        // =========================================================================

        private void BtnPlanSaver_Click(object sender, RoutedEventArgs e)
        {
            SetPowerPlan(PowerSchemeMode.PowerSaver, "Power Saver");
        }

        private void BtnPlanBalanced_Click(object sender, RoutedEventArgs e)
        {
            SetPowerPlan(PowerSchemeMode.Balanced, "Balanced");
        }

        private void BtnPlanHighPerf_Click(object sender, RoutedEventArgs e)
        {
            SetPowerPlan(PowerSchemeMode.HighPerformance, "High Performance");
        }

        private void SetPowerPlan(PowerSchemeMode mode, string name)
        {
            try
            {
                if (PowerPlanManager.SetScheme(mode))
                {
                    TxtActivePowerPlan.Text = name;
                    UpdatePowerButtonsHighlight(name);
                    ShowToast("⚡ Power Plan: " + name);
                }
            }
            catch (Exception ex)
            {
                ShowToast("Error switching plan: " + ex.Message);
            }
        }

        private void UpdatePowerButtonsHighlight(string scheme)
        {
            var activeBrush = (Brush)FindResource("BgControlActive");
            var normalBrush = Brushes.Transparent;

            BtnPlanSaver.Background = scheme.IndexOf("Saver", StringComparison.OrdinalIgnoreCase) >= 0 ? activeBrush : normalBrush;
            BtnPlanBalanced.Background = scheme.IndexOf("Balanced", StringComparison.OrdinalIgnoreCase) >= 0 ? activeBrush : normalBrush;
            BtnPlanHighPerf.Background = (scheme.IndexOf("High", StringComparison.OrdinalIgnoreCase) >= 0 || scheme.IndexOf("Ultimate", StringComparison.OrdinalIgnoreCase) >= 0) ? activeBrush : normalBrush;
        }

        // =========================================================================
        // TOOLS & UTILITY LAUNCHERS
        // =========================================================================

        private void BtnToolsMenu_Click(object sender, RoutedEventArgs e)
        {
            MenuWindowsTools.PlacementTarget = BtnToolsMenu;
            MenuWindowsTools.IsOpen = true;
        }

        private void BtnTaskMgr_Click(object sender, RoutedEventArgs e)
        {
            try { Process.Start("taskmgr.exe"); } catch { }
        }

        private void BtnResMon_Click(object sender, RoutedEventArgs e)
        {
            try { Process.Start("resmon.exe"); } catch { }
        }

        private void BtnPCMgr_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:storagesense") { UseShellExecute = true });
            }
            catch
            {
                try { Process.Start("cleanmgr.exe"); } catch { }
            }
        }

        private void MenuOpenServices_Click(object sender, RoutedEventArgs e)
        {
            try { Process.Start("services.msc"); } catch { }
        }

        // =========================================================================
        // THEME CONTROLLER (DARK / LIGHT / NEON / ROSE)
        // =========================================================================

        private void BtnToggleTheme_Click(object sender, RoutedEventArgs e)
        {
            MenuThemes.PlacementTarget = BtnToggleTheme;
            MenuThemes.IsOpen = true;
        }

        private void MenuThemeDark_Click(object sender, RoutedEventArgs e)
        {
            SetTheme("Dark");
        }

        private void MenuThemeLight_Click(object sender, RoutedEventArgs e)
        {
            SetTheme("Light");
        }

        private void MenuThemeNeon_Click(object sender, RoutedEventArgs e)
        {
            SetTheme("Neon");
        }

        private void MenuThemeRose_Click(object sender, RoutedEventArgs e)
        {
            SetTheme("Rose");
        }

        private void SetTheme(string themeName)
        {
            _config.Theme = themeName;
            ApplyTheme(themeName);
            ConfigManager.Save(_config);
            ShowToast("🎨 Tema: " + themeName);
        }

        private void ApplyTheme(string themeName)
        {
            try
            {
                App.SetTheme(themeName);
                if (TxtCurrentTheme != null)
                {
                    TxtCurrentTheme.Text = themeName;
                }
            }
            catch { }
        }

        private void BtnToggleInterval_Click(object sender, RoutedEventArgs e)
        {
            _config.RefreshIntervalSeconds = _config.RefreshIntervalSeconds == 3 ? 5 : 3;
            TxtInterval.Text = string.Format("{0}s", _config.RefreshIntervalSeconds);
            ConfigManager.Save(_config);
            ShowToast(string.Format("Interval: {0}s", _config.RefreshIntervalSeconds));
        }

        // =========================================================================
        // WINDOW CAPTION CONTROLS & PINNING
        // =========================================================================

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void BtnPinTop_Click(object sender, RoutedEventArgs e)
        {
            Topmost = !Topmost;
            _config.AlwaysOnTop = Topmost;
            ConfigManager.Save(_config);

            var pinBrush = Topmost ? (Brush)FindResource("AccentCpu") : (Brush)FindResource("TextMuted");
            PathPin.Fill = pinBrush;
            PathWidgetPin.Fill = pinBrush;

            ShowToast(Topmost ? "📌 Pinned Always on Top" : "📌 Unpinned");
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            if (_config.MinimizeToTray)
            {
                HideToTray();
            }
            else
            {
                WindowState = WindowState.Minimized;
            }
        }

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            if (_config.CloseToTray)
            {
                HideToTray();
            }
            else
            {
                _isExiting = true;
                Close();
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_isExiting && _config.CloseToTray)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }

            base.OnClosing(e);
        }

        public void HideToTray()
        {
            _lastWindowState = WindowState;
            _isTrayMode = true;
            Hide();
            ShowInTaskbar = false;

            // Trim memory aggressively when hidden in tray
            try
            {
                GC.Collect(2, GCCollectionMode.Forced);
                GC.WaitForPendingFinalizers();
                NativeMethods.SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, (IntPtr)(-1), (IntPtr)(-1));
            }
            catch { }
        }

        public void RestoreFromTray()
        {
            _isTrayMode = false;
            Show();
            ShowInTaskbar = true;
            WindowState = (_lastWindowState == WindowState.Minimized) ? WindowState.Normal : _lastWindowState;

            var helper = new WindowInteropHelper(this);
            NativeMethods.SetForegroundWindow(helper.Handle);
            Activate();
        }

        private void OpenTrayContextMenu(IntPtr hwnd)
        {
            var menu = FindResource("TrayContextMenu") as ContextMenu;
            if (menu == null) return;

            // Sync menu checkboxes with current config
            var itemSettings = menu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Name == "TrayMenuSettings");
            if (itemSettings != null)
            {
                var minToTray = itemSettings.Items.OfType<MenuItem>().FirstOrDefault(m => m.Name == "TrayMenuMinToTray");
                if (minToTray != null) minToTray.IsChecked = _config.MinimizeToTray;

                var closeToTray = itemSettings.Items.OfType<MenuItem>().FirstOrDefault(m => m.Name == "TrayMenuCloseToTray");
                if (closeToTray != null) closeToTray.IsChecked = _config.CloseToTray;

                var runStartup = itemSettings.Items.OfType<MenuItem>().FirstOrDefault(m => m.Name == "TrayMenuRunAtStartup");
                if (runStartup != null) runStartup.IsChecked = StartupHelper.IsRunAtStartupEnabled();

                var alwaysTop = itemSettings.Items.OfType<MenuItem>().FirstOrDefault(m => m.Name == "TrayMenuAlwaysOnTop");
                if (alwaysTop != null) alwaysTop.IsChecked = Topmost;
            }

            NativeMethods.SetForegroundWindow(hwnd);
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            menu.IsOpen = true;
        }

        private void TrayMenuOpen_Click(object sender, RoutedEventArgs e)
        {
            RestoreFromTray();
        }

        private void TrayMenuExit_Click(object sender, RoutedEventArgs e)
        {
            _isExiting = true;
            if (_trayManager != null)
            {
                _trayManager.Dispose();
            }
            Close();
            Application.Current.Shutdown();
        }

        private void TrayMenuMinToTray_Click(object sender, RoutedEventArgs e)
        {
            var mi = sender as MenuItem;
            if (mi != null)
            {
                _config.MinimizeToTray = mi.IsChecked;
                ConfigManager.Save(_config);
                UpdateSettingsMenuItemsState();
                ShowToast(_config.MinimizeToTray ? "📥 Minimizar a la bandeja activado" : "Minimizar a barra de tareas");
            }
        }

        private void TrayMenuCloseToTray_Click(object sender, RoutedEventArgs e)
        {
            var mi = sender as MenuItem;
            if (mi != null)
            {
                _config.CloseToTray = mi.IsChecked;
                ConfigManager.Save(_config);
                UpdateSettingsMenuItemsState();
                ShowToast(_config.CloseToTray ? "❌ Cerrar a la bandeja activado" : "Cerrar finaliza la aplicación");
            }
        }

        private void TrayMenuRunAtStartup_Click(object sender, RoutedEventArgs e)
        {
            var mi = sender as MenuItem;
            if (mi != null)
            {
                bool newState = mi.IsChecked;
                StartupHelper.SetRunAtStartup(newState, true);
                _config.RunAtStartup = newState;
                ConfigManager.Save(_config);
                UpdateSettingsMenuItemsState();
                ShowToast(newState ? "🚀 Inicio con Windows activado" : "Inicio automático desactivado");
            }
        }

        private void BtnSettingsMenu_Click(object sender, RoutedEventArgs e)
        {
            UpdateSettingsMenuItemsState();
            MenuAppSettings.PlacementTarget = BtnSettingsMenu;
            MenuAppSettings.IsOpen = true;
        }

        private void UpdateSettingsMenuItemsState()
        {
            if (MenuSettingMinToTray != null) MenuSettingMinToTray.IsChecked = _config.MinimizeToTray;
            if (MenuSettingCloseToTray != null) MenuSettingCloseToTray.IsChecked = _config.CloseToTray;
            if (MenuSettingStartup != null) MenuSettingStartup.IsChecked = StartupHelper.IsRunAtStartupEnabled();
            if (MenuSettingAlwaysOnTop != null) MenuSettingAlwaysOnTop.IsChecked = Topmost;
        }

        // =========================================================================
        // PROCESS ACTIONS & CONTEXT MENUS
        // =========================================================================

        private void ProcessRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                var border = sender as Border;
                if (border != null)
                {
                    var proc = border.Tag as ProcessMetric;
                    if (proc != null)
                    {
                        OpenProcessDetails(proc);
                    }
                }
            }
        }

        private void BtnProcessDetails_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null)
            {
                var proc = btn.Tag as ProcessMetric;
                if (proc != null)
                {
                    OpenProcessDetails(proc);
                }
            }
        }

        private void MenuProcessDetails_Click(object sender, RoutedEventArgs e)
        {
            var item = sender as MenuItem;
            if (item != null)
            {
                var proc = item.Tag as ProcessMetric;
                if (proc != null)
                {
                    OpenProcessDetails(proc);
                }
            }
        }

        private void OpenProcessDetails(ProcessMetric proc)
        {
            try
            {
                var win = new ProcessDetailsWindow(proc);
                win.Owner = this;
                win.ShowDialog();
            }
            catch { }
        }

        private void BtnProcessKill_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null)
            {
                var proc = btn.Tag as ProcessMetric;
                if (proc != null)
                {
                    KillProcess(proc.Id, proc.Name);
                }
            }
        }

        private void MenuProcessKill_Click(object sender, RoutedEventArgs e)
        {
            var item = sender as MenuItem;
            if (item != null)
            {
                var proc = item.Tag as ProcessMetric;
                if (proc != null)
                {
                    KillProcess(proc.Id, proc.Name);
                }
            }
        }

        private void KillProcess(int pid, string name)
        {
            var result = MessageBox.Show(
                string.Format("Are you sure you want to end process '{0}' (PID: {1})?", name, pid),
                "End Process",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    var p = Process.GetProcessById(pid);
                    p.Kill();
                    ShowToast("🔴 Ended: " + name);
                }
                catch (Exception ex)
                {
                    ShowToast("Could not kill process: " + ex.Message);
                }
            }
        }

        private void MenuProcessOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var item = sender as MenuItem;
            if (item != null)
            {
                var proc = item.Tag as ProcessMetric;
                if (proc != null && !string.IsNullOrEmpty(proc.ExecutablePath))
                {
                    try
                    {
                        Process.Start("explorer.exe", string.Format("/select,\"{0}\"", proc.ExecutablePath));
                    }
                    catch { }
                }
            }
        }

        private void MenuProcessSearch_Click(object sender, RoutedEventArgs e)
        {
            var item = sender as MenuItem;
            if (item != null)
            {
                var proc = item.Tag as ProcessMetric;
                if (proc != null)
                {
                    try
                    {
                        string url = string.Format("https://www.google.com/search?q={0}+process+windows", Uri.EscapeDataString(proc.Name));
                        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    }
                    catch { }
                }
            }
        }

        private void BtnDriveOpen_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null)
            {
                var disk = btn.Tag as DiskMetric;
                if (disk != null && !string.IsNullOrEmpty(disk.Name))
                {
                    try
                    {
                        Process.Start("explorer.exe", disk.Name);
                    }
                    catch { }
                }
            }
        }

        // =========================================================================
        // SERVICES TAB ACTIONS
        // =========================================================================

        private void MenuServiceStart_Click(object sender, RoutedEventArgs e)
        {
            var mi = sender as MenuItem;
            var svcItem = mi != null ? mi.Tag as ServiceItem : null;
            if (svcItem == null) return;
            try
            {
                using (var sc = new ServiceController(svcItem.ServiceName))
                {
                    if (sc.Status != ServiceControllerStatus.Running && sc.Status != ServiceControllerStatus.StartPending)
                    {
                        sc.Start();
                        ShowToast(string.Format(LocalizationManager.Get("ToastServiceStarted"), svcItem.DisplayName));
                    }
                }
            }
            catch (Exception ex)
            {
                ShowToast("Error: " + ex.Message);
            }
        }

        private void MenuServiceStop_Click(object sender, RoutedEventArgs e)
        {
            var mi = sender as MenuItem;
            var svcItem = mi != null ? mi.Tag as ServiceItem : null;
            if (svcItem == null) return;
            try
            {
                using (var sc = new ServiceController(svcItem.ServiceName))
                {
                    if (sc.Status != ServiceControllerStatus.Stopped && sc.Status != ServiceControllerStatus.StopPending)
                    {
                        sc.Stop();
                        ShowToast(string.Format(LocalizationManager.Get("ToastServiceStopped"), svcItem.DisplayName));
                    }
                }
            }
            catch (Exception ex)
            {
                ShowToast("Error: " + ex.Message);
            }
        }

        private void MenuServiceRestart_Click(object sender, RoutedEventArgs e)
        {
            var mi = sender as MenuItem;
            var svcItem = mi != null ? mi.Tag as ServiceItem : null;
            if (svcItem == null) return;
            try
            {
                using (var sc = new ServiceController(svcItem.ServiceName))
                {
                    if (sc.Status == ServiceControllerStatus.Running)
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(5));
                    }
                    sc.Start();
                    ShowToast(string.Format(LocalizationManager.Get("ToastServiceStarted"), svcItem.DisplayName));
                }
            }
            catch (Exception ex)
            {
                ShowToast("Error: " + ex.Message);
            }
        }

        private void MenuServiceSearch_Click(object sender, RoutedEventArgs e)
        {
            var mi = sender as MenuItem;
            var svcItem = mi != null ? mi.Tag as ServiceItem : null;
            if (svcItem == null) return;
            try
            {
                string url = "https://www.google.com/search?q=" + Uri.EscapeDataString(svcItem.ServiceName + " windows service");
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { }
        }

        private void MenuServiceCopy_Click(object sender, RoutedEventArgs e)
        {
            var mi = sender as MenuItem;
            var svcItem = mi != null ? mi.Tag as ServiceItem : null;
            if (svcItem != null)
            {
                try
                {
                    Clipboard.SetText(svcItem.ServiceName);
                    ShowToast("📋 " + svcItem.ServiceName);
                }
                catch { }
            }
        }

        // =========================================================================
        // TASKS TAB ACTIONS
        // =========================================================================

        private void MenuTaskRun_Click(object sender, RoutedEventArgs e)
        {
            var mi = sender as MenuItem;
            var task = mi != null ? mi.Tag as TaskItem : null;
            if (task == null) return;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = string.Format("/run /tn \"{0}\"", task.TaskPath),
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                ShowToast(string.Format(LocalizationManager.Get("ToastTaskExecuted"), task.TaskName));
            }
            catch (Exception ex)
            {
                ShowToast("Error: " + ex.Message);
            }
        }

        private void MenuTaskEnd_Click(object sender, RoutedEventArgs e)
        {
            var mi = sender as MenuItem;
            var task = mi != null ? mi.Tag as TaskItem : null;
            if (task == null) return;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = string.Format("/end /tn \"{0}\"", task.TaskPath),
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                ShowToast("⏹️ Tarea finalizada: " + task.TaskName);
            }
            catch (Exception ex)
            {
                ShowToast("Error: " + ex.Message);
            }
        }

        private void MenuOpenTaskSchd_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("taskschd.msc") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ShowToast("Error: " + ex.Message);
            }
        }

        private void MenuTaskSearch_Click(object sender, RoutedEventArgs e)
        {
            var mi = sender as MenuItem;
            var task = mi != null ? mi.Tag as TaskItem : null;
            if (task == null) return;
            try
            {
                string url = "https://www.google.com/search?q=" + Uri.EscapeDataString(task.TaskName + " windows scheduled task");
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { }
        }

        private void MenuTaskCopy_Click(object sender, RoutedEventArgs e)
        {
            var mi = sender as MenuItem;
            var task = mi != null ? mi.Tag as TaskItem : null;
            if (task != null)
            {
                try
                {
                    Clipboard.SetText(task.TaskPath);
                    ShowToast("📋 " + task.TaskPath);
                }
                catch { }
            }
        }

        // =========================================================================
        // STARTUP APPS TAB ACTIONS
        // =========================================================================

        private void BtnStartupFolder_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as FrameworkElement;
            var item = btn != null ? btn.Tag as StartupItem : null;
            OpenStartupItemLocation(item);
        }

        private void MenuStartupOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var mi = sender as MenuItem;
            var item = mi != null ? mi.Tag as StartupItem : null;
            OpenStartupItemLocation(item);
        }

        private void OpenStartupItemLocation(StartupItem item)
        {
            if (item == null) return;
            try
            {
                string target = item.ExecutablePath;
                if (!string.IsNullOrEmpty(target) && File.Exists(target))
                {
                    Process.Start("explorer.exe", string.Format("/select,\"{0}\"", target));
                }
                else if (!string.IsNullOrEmpty(target) && Directory.Exists(target))
                {
                    Process.Start("explorer.exe", string.Format("\"{0}\"", target));
                }
                else
                {
                    string folder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                    Process.Start("explorer.exe", folder);
                }
            }
            catch (Exception ex)
            {
                ShowToast("Error: " + ex.Message);
            }
        }

        private void BtnStartupSearch_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as FrameworkElement;
            var item = btn != null ? btn.Tag as StartupItem : null;
            SearchStartupItemOnline(item);
        }

        private void MenuStartupSearch_Click(object sender, RoutedEventArgs e)
        {
            var mi = sender as MenuItem;
            var item = mi != null ? mi.Tag as StartupItem : null;
            SearchStartupItemOnline(item);
        }

        private void SearchStartupItemOnline(StartupItem item)
        {
            if (item == null) return;
            try
            {
                string query = !string.IsNullOrEmpty(item.DisplayName) ? item.DisplayName : item.Name;
                string url = "https://www.google.com/search?q=" + Uri.EscapeDataString(query + " startup application windows");
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { }
        }

        private void MenuStartupCopyCmd_Click(object sender, RoutedEventArgs e)
        {
            var mi = sender as MenuItem;
            var item = mi != null ? mi.Tag as StartupItem : null;
            if (item != null)
            {
                try
                {
                    string textToCopy = !string.IsNullOrEmpty(item.ExecutablePath) ? item.ExecutablePath : item.Command;
                    Clipboard.SetText(textToCopy);
                    ShowToast(LocalizationManager.Get("ToastStartupCopied"));
                }
                catch { }
            }
        }

        private void MenuStartupSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true });
            }
            catch
            {
                ToolLauncher.StartTaskManager();
            }
        }
    }
}
