using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SystemCoreMonitor.Core;
using SystemCoreMonitor.Models;
using SystemCoreMonitor.Modules;
using SystemCoreMonitor.ViewModels;

namespace SystemCoreMonitor.UI
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private readonly CpuCollector _cpu = new();
        private readonly GpuCollector _gpu = new();
        private readonly NpuCollector _npu = new();
        private readonly WindowsAcceleratorEngine _accelEngine = new();
        private readonly MemoryCollector _mem = new();
        private readonly DiskCollector _disk = new();
        private readonly NetworkCollector _net = new();
        private readonly HardwareCollector _hw = new();
        private readonly ProcessCollector _proc = new();
        private readonly AiAgentCollector _ai = new();
        private readonly ServiceCollector _svc = new();
        private readonly StartupCollector _startup = new();

        private readonly DispatcherTimer _timer = new();
        private readonly TrayManager _trayManager = new();
        private AppConfig _config = new();
        private DispatcherTimer? _toastTimer;

        private bool _isClosing = false;
        private bool _isSampling = false;
        private bool _isPinnedTop = false;

        public MainWindow()
        {
            InitializeComponent();

            _viewModel = new MainViewModel();
            DataContext = _viewModel;

            _config = ConfigManager.Load();
            App.SetTheme(_config.Theme);
            LocalizationManager.CurrentLanguage = _config.Language;

            WireViewModelEvents();

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;

            _timer.Interval = TimeSpan.FromSeconds(Math.Max(1, _config.RefreshIntervalSeconds));
            _timer.Tick += async (s, e) => await SampleTelemetryTickAsync();
        }

        private void WireViewModelEvents()
        {
            _viewModel.Dashboard.ShowToastRequested += (msg, type) => ShowToast(msg, type);
            _viewModel.Dashboard.ShowProgressRequested += msg => ShowToast(msg, NotificationType.InProgress);

            _viewModel.Processes.ShowToastRequested += (msg, type) => ShowToast(msg, type);

            _viewModel.AiAgents.ShowToastRequested += (msg, type) => ShowToast(msg, type);
            _viewModel.AiAgents.ShowProgressRequested += msg => ShowToast(msg, NotificationType.InProgress);

            _viewModel.Storage.ShowToastRequested += (msg, type) => ShowToast(msg, type);
            _viewModel.Storage.ShowProgressRequested += msg => ShowToast(msg, NotificationType.InProgress);

            _viewModel.Services.ShowToastRequested += (msg, type) => ShowToast(msg, type);
            _viewModel.Startup.ShowToastRequested += (msg, type) => ShowToast(msg, type);

            _viewModel.Settings.ThemeChanged += theme =>
            {
                App.SetTheme(theme);
                string themeLabel = theme switch
                {
                    "Light" => "Pastel Light",
                    "Neon" => "Cyber Neon",
                    "Rose" => "Sakura Rose",
                    _ => "Pastel Dark"
                };
                bool isEs = LocalizationManager.CurrentLanguage == "es";
                ShowToast(isEs ? $"Tema visual aplicado: {themeLabel}" : $"Visual theme applied: {themeLabel}", NotificationType.Success);
            };
            _viewModel.Settings.LanguageChanged += lang =>
            {
                LocalizationManager.CurrentLanguage = lang;
                ShowToast(lang == "es" ? "Idioma cambiado a Español" : "Language set to English", NotificationType.Info);
            };

            _viewModel.Accelerators.IsEnabled = _config.EnableAcceleratorsMonitoring;
            _viewModel.SetAcceleratorsVisibility(_config.EnableAcceleratorsMonitoring);
            _viewModel.Settings.AcceleratorsMonitoringChanged += enabled =>
            {
                _config.EnableAcceleratorsMonitoring = enabled;
                _viewModel.Accelerators.IsEnabled = enabled;
                _viewModel.SetAcceleratorsVisibility(enabled);
                if (!enabled)
                {
                    _viewModel.Accelerators.Update(
                        new GpuMetric { Name = "Monitoreo Desactivado", Vendor = "Desactivado en Configuración" },
                        new NpuMetric { Status = "Disabled", Name = "Monitoreo desactivado" });
                    TxtWidgetGpu.Text = "Off";
                    ProgressWidgetGpu.Value = 0;
                }
                ShowToast(enabled 
                    ? (LocalizationManager.CurrentLanguage == "es" ? "Monitoreo de aceleradores activado" : "Accelerators monitoring enabled") 
                    : (LocalizationManager.CurrentLanguage == "es" ? "Monitoreo de aceleradores desactivado" : "Accelerators monitoring disabled"), 
                    NotificationType.Info);
            };

            _viewModel.Settings.IntervalChanged += sec =>
            {
                _config.RefreshIntervalSeconds = sec;
                _timer.Interval = TimeSpan.FromSeconds(Math.Max(1, sec));
                bool isEs = LocalizationManager.CurrentLanguage == "es";
                string msg = isEs
                    ? $"Frecuencia de telemetría: cada {sec} segundo{(sec == 1 ? "" : "s")}"
                    : $"Telemetry sampling rate: every {sec} second{(sec == 1 ? "" : "s")}";
                ShowToast(msg, NotificationType.Info);
            };

            _viewModel.Settings.RetentionChanged += days =>
            {
                _config.TranscriptRetentionDays = days;
                _viewModel.AiAgents.RefreshRetentionDisplay();
                bool isEs = LocalizationManager.CurrentLanguage == "es";
                string msg = isEs
                    ? $"Periodo de retención IA: {days} días"
                    : $"AI transcript retention period: {days} days";
                ShowToast(msg, NotificationType.Info);
            };
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            InitHwndHooks();
            InitAppBranding();
            InitTray();

            _timer.Start();
            _ = SampleTelemetryTickAsync();
            _ = ScanBloatInitialAsync();
        }

        private async Task ScanBloatInitialAsync()
        {
            try
            {
                var bloat = await Task.Run(() => BloatDetector.Scan());
                _viewModel.Storage.UpdateBloat(bloat);
            }
            catch (Exception ex)
            {
                CrashLogger.LogException("MainWindow.ScanBloatInitialAsync", ex, false);
            }
        }

        private void InitHwndHooks()
        {
            var helper = new WindowInteropHelper(this);
            var source = HwndSource.FromHwnd(helper.Handle);
            source?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_GETMINMAXINFO)
            {
                WmGetMinMaxInfo(hwnd, lParam);
                handled = true;
            }
            return IntPtr.Zero;
        }

        private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {
            var mmi = Marshal.PtrToStructure<NativeMethods.MINMAXINFO>(lParam);
            IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero)
            {
                var mi = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
                if (NativeMethods.GetMonitorInfo(monitor, ref mi))
                {
                    mmi.ptMaxPosition.X = Math.Abs(mi.rcWork.Left - mi.rcMonitor.Left);
                    mmi.ptMaxPosition.Y = Math.Abs(mi.rcWork.Top - mi.rcMonitor.Top);
                    mmi.ptMaxSize.X = Math.Abs(mi.rcWork.Right - mi.rcWork.Left);
                    mmi.ptMaxSize.Y = Math.Abs(mi.rcWork.Bottom - mi.rcWork.Top);
                }
            }
            Marshal.StructureToPtr(mmi, lParam, true);
        }

        private void InitAppBranding()
        {
            try
            {
                string pngPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.png");
                if (File.Exists(pngPath))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(pngPath, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    ImgAppLogo.Source = bmp;
                    ImgWidgetLogo.Source = bmp;
                }
            }
            catch { }

            var hw = _hw.Sample();
            TxtHwSummaryBadge.Text = $"{hw.CpuModel} • {hw.OsName}";
            TxtCpuCoresBadge.Text = $"{Environment.ProcessorCount} Cores";
        }

        private void InitTray()
        {
            var helper = new WindowInteropHelper(this);
            _trayManager.Initialize(helper.Handle, "System Core Monitor");
        }

        private double _cachedTotalRamGB = 16.0;

        private async Task SampleTelemetryTickAsync()
        {
            if (_isSampling) return;
            _isSampling = true;

            try
            {
                var cpuTask = Task.Run(() => _cpu.Sample());
                var memTask = Task.Run(() => _mem.Sample());
                var diskTask = Task.Run(() => _disk.Sample());
                var netTask = Task.Run(() => _net.Sample());
                var procsTask = Task.Run(() => _proc.Sample(50, _cachedTotalRamGB));
                var aiTask = Task.Run(() => _ai.Sample());
                var svcTask = Task.Run(() => _svc.Sample());
                var startupTask = Task.Run(() => _startup.Sample());

                var accelTask = _config.EnableAcceleratorsMonitoring
                    ? Task.Run(() => _accelEngine.SampleAllEngines())
                    : null;

                if (accelTask != null)
                {
                    await Task.WhenAll(cpuTask, memTask, diskTask, netTask, accelTask, procsTask, aiTask, svcTask, startupTask);
                }
                else
                {
                    await Task.WhenAll(cpuTask, memTask, diskTask, netTask, procsTask, aiTask, svcTask, startupTask);
                }

                var cpu = cpuTask.Result;
                var mem = memTask.Result;
                var disk = diskTask.Result;
                var net = netTask.Result;
                var procs = procsTask.Result;
                var ai = aiTask.Result;
                var svc = svcTask.Result;
                var startup = startupTask.Result;

                if (mem?.TotalGB > 0)
                {
                    _cachedTotalRamGB = mem.TotalGB;
                }

                GpuMetric gpu;
                NpuMetric npu;
                if (accelTask != null)
                {
                    var engineLoads = accelTask.Result;
                    gpu = _gpu.Sample(engineLoads);
                    npu = _npu.Sample(engineLoads, gpu?.LuidString ?? "");
                }
                else
                {
                    gpu = new GpuMetric { Name = "Monitoreo Desactivado", Vendor = "Desactivado en Configuración" };
                    npu = new NpuMetric { Status = "Disabled", Name = "Monitoreo desactivado" };
                }

                // Dispatch to ViewModels
                _viewModel.Dashboard.Update(cpu, mem, disk, net, gpu, npu);
                _viewModel.Processes.Update(procs);
                _viewModel.AiAgents.Update(ai);
                _viewModel.Accelerators.Update(gpu, npu);
                _viewModel.Storage.UpdateDrives(disk);
                _viewModel.Services.Update(svc);
                _viewModel.Startup.Update(startup);

                // Update Widget UI
                TxtWidgetCpu.Text = $"{cpu.LoadPercent:F0}%";
                ProgressWidgetCpu.Value = cpu.LoadPercent;
                TxtWidgetRam.Text = $"{mem.LoadPercent:F0}%";
                ProgressWidgetRam.Value = mem.LoadPercent;
                TxtWidgetGpu.Text = _config.EnableAcceleratorsMonitoring ? $"{gpu.LoadPercent:F0}%" : "Off";
                ProgressWidgetGpu.Value = _config.EnableAcceleratorsMonitoring ? gpu.LoadPercent : 0;
                TxtWidgetNet.Text = net.DownloadDisplay;

                // Uptime
                var hw = _hw.Sample();
                TxtUptime.Text = $"Uptime: {hw.UptimeDisplay}";

                string trayGpuText = _config.EnableAcceleratorsMonitoring ? $" | GPU: {gpu.LoadPercent:F0}%" : "";
                _trayManager.UpdateTooltip($"System Core Monitor\nCPU: {cpu.LoadPercent:F0}% | RAM: {mem.LoadPercent:F0}%{trayGpuText}");
            }
            catch (Exception ex)
            {
                CrashLogger.LogException("MainWindow.SampleTelemetryTickAsync", ex, false);
            }
            finally
            {
                _isSampling = false;
            }
        }

        public void ShowToast(string message) => ShowToast(message, NotificationType.Info);

        public void ShowToast(string message, NotificationType type)
        {
            Dispatcher.Invoke(() =>
            {
                // 1. Update footer status text
                TxtStatusNotification.Text = message;

                // 2. Configure prominent floating banner
                ToastStatusText.Text = message;
                _toastTimer?.Stop();

                if (type == NotificationType.InProgress)
                {
                    ToastOverlayBanner.BorderBrush = (Brush)FindResource("AccentCpu");
                    ToastStatusIcon.Fill = (Brush)FindResource("AccentCpu");
                    ToastStatusIcon.Data = (Geometry)FindResource("IconSync");
                }
                else if (type == NotificationType.Success)
                {
                    ToastOverlayBanner.BorderBrush = (Brush)FindResource("StatusOk");
                    ToastStatusIcon.Fill = (Brush)FindResource("StatusOk");
                    ToastStatusIcon.Data = (Geometry)FindResource("IconCheck");
                }
                else if (type == NotificationType.Error)
                {
                    ToastOverlayBanner.BorderBrush = (Brush)FindResource("StatusCrit");
                    ToastStatusIcon.Fill = (Brush)FindResource("StatusCrit");
                    ToastStatusIcon.Data = (Geometry)FindResource("IconClose");
                }
                else if (type == NotificationType.Warning)
                {
                    ToastOverlayBanner.BorderBrush = (Brush)FindResource("StatusWarn");
                    ToastStatusIcon.Fill = (Brush)FindResource("StatusWarn");
                    ToastStatusIcon.Data = (Geometry)FindResource("IconAlert");
                }
                else
                {
                    ToastOverlayBanner.BorderBrush = (Brush)FindResource("AccentCpu");
                    ToastStatusIcon.Fill = (Brush)FindResource("AccentCpu");
                    ToastStatusIcon.Data = (Geometry)FindResource("IconInfo");
                }

                ToastOverlayBanner.Visibility = Visibility.Visible;
                var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(180));
                ToastOverlayBanner.BeginAnimation(UIElement.OpacityProperty, fadeIn);

                if (type != NotificationType.InProgress)
                {
                    double duration = (type == NotificationType.Error || type == NotificationType.Warning) ? 4.5 : 3.5;
                    _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(duration) };
                    _toastTimer.Tick += (s, e) =>
                    {
                        _toastTimer?.Stop();
                        var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(350));
                        fadeOut.Completed += (s2, e2) => ToastOverlayBanner.Visibility = Visibility.Collapsed;
                        ToastOverlayBanner.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                    };
                    _toastTimer.Start();
                }
            });
        }

        #region Window Caption Controls & View Modes

        private double _prevWidth = 1200;
        private double _prevHeight = 760;
        private double _prevLeft = 100;
        private double _prevTop = 100;
        private WindowState _prevWindowState = WindowState.Normal;
        private bool _isWidgetMode = false;

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                BtnMaximize_Click(sender, e);
            }
            else
            {
                DragMove();
            }
        }

        private void Widget_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                MenuViewFull_Click(sender, e);
            }
            else
            {
                DragMove();
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            if (_config.MinimizeToTray)
            {
                Hide();
            }
            else
            {
                WindowState = WindowState.Minimized;
            }
        }

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            if (_isWidgetMode) return;
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            if (_config.CloseToTray)
            {
                Hide();
            }
            else
            {
                _isClosing = true;
                Close();
            }
        }

        private void BtnPinTop_Click(object sender, RoutedEventArgs e)
        {
            _isPinnedTop = !_isPinnedTop;
            Topmost = _isPinnedTop;
            var pinBrush = _isPinnedTop ? (Brush)FindResource("AccentCpu") : (Brush)FindResource("TextMuted");
            if (PathPin != null) PathPin.Fill = pinBrush;
            if (PathWidgetPin != null) PathWidgetPin.Fill = pinBrush;
            ShowToast(_isPinnedTop ? "Ventana fijada al frente" : "Ventana desfijada");
        }

        public void MenuViewFull_Click(object sender, RoutedEventArgs e)
        {
            _isWidgetMode = false;

            // Remove widget lock constraints
            MaxWidth = double.PositiveInfinity;
            MaxHeight = double.PositiveInfinity;
            MinWidth = 960;
            MinHeight = 600;

            ContainerMainView.Visibility = Visibility.Visible;
            ContainerWidgetView.Visibility = Visibility.Collapsed;

            ResizeMode = ResizeMode.CanResizeWithGrip;

            Width = _prevWidth;
            Height = _prevHeight;
            Left = _prevLeft;
            Top = _prevTop;

            if (_prevWindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Maximized;
            }
        }

        public void MenuViewWidget_Click(object sender, RoutedEventArgs e)
        {
            if (_isWidgetMode) return;

            // Remember previous dashboard geometry
            _prevWindowState = WindowState;
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
            }
            _prevWidth = Width >= 960 ? Width : 1200;
            _prevHeight = Height >= 600 ? Height : 760;
            _prevLeft = Left;
            _prevTop = Top;

            _isWidgetMode = true;
            ContainerMainView.Visibility = Visibility.Collapsed;
            ContainerWidgetView.Visibility = Visibility.Visible;

            ResizeMode = ResizeMode.NoResize;

            // Fixed locked widget dimensions (immune to maximization)
            MinWidth = 360;
            MinHeight = 96;
            MaxWidth = 360;
            MaxHeight = 96;
            Width = 360;
            Height = 96;

            // Ensure widget is within work area bounds
            var workArea = SystemParameters.WorkArea;
            if (Left + Width > workArea.Right || Top + Height > workArea.Bottom || Left < workArea.Left || Top < workArea.Top)
            {
                Left = workArea.Right - Width - 30;
                Top = workArea.Bottom - Height - 30;
            }
        }

        private void MenuWidgetSnap_Click(object sender, RoutedEventArgs e)
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - Width - 30;
            Top = workArea.Bottom - Height - 30;
        }

        #endregion

        #region Tray Context Menu Handlers

        private void TrayMenuRestore_Click(object sender, RoutedEventArgs e)
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void BtnPlanSaver_Click(object sender, RoutedEventArgs e) => PowerPlanManager.SetScheme(PowerSchemeMode.PowerSaver);
        private void BtnPlanBalanced_Click(object sender, RoutedEventArgs e) => PowerPlanManager.SetScheme(PowerSchemeMode.Balanced);
        private void BtnPlanHighPerf_Click(object sender, RoutedEventArgs e) => PowerPlanManager.SetScheme(PowerSchemeMode.HighPerformance);
        private void BtnOptimize_Click(object sender, RoutedEventArgs e) => Task.Run(() => MemoryOptimizer.OptimizeWorkingSet(out _));
        private void BtnCleanTemp_Click(object sender, RoutedEventArgs e) => Task.Run(() => SafeTempCleaner.CleanDeepStorage(false));
        private void BtnFlushDns_Click(object sender, RoutedEventArgs e)
        {
            Task.Run(() =>
            {
                bool ok = NetworkCollector.FlushDnsCache();
                Dispatcher.Invoke(() =>
                {
                    if (ok)
                    {
                        ShowToast(LocalizationManager.Get("ToastDnsFlushed"), NotificationType.Success);
                    }
                    else
                    {
                        ShowToast(LocalizationManager.Get("ToastDnsFailed"), NotificationType.Error);
                    }
                });
            });
        }
        private void TrayMenuMinToTray_Click(object sender, RoutedEventArgs e) => _config.MinimizeToTray = !_config.MinimizeToTray;
        private void TrayMenuCloseToTray_Click(object sender, RoutedEventArgs e) => _config.CloseToTray = !_config.CloseToTray;
        private void TrayMenuRunAtStartup_Click(object sender, RoutedEventArgs e) => _config.RunAtStartup = !_config.RunAtStartup;
        private void BtnTaskMgr_Click(object sender, RoutedEventArgs e) => ToolLauncher.StartTaskManager();
        private void BtnResMon_Click(object sender, RoutedEventArgs e) => ToolLauncher.StartResourceMonitor();
        private void MenuOpenServices_Click(object sender, RoutedEventArgs e) => ToolLauncher.StartServicesConsole();

        private void TrayMenuExit_Click(object sender, RoutedEventArgs e)
        {
            _isClosing = true;
            Close();
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_isClosing && _config.CloseToTray)
            {
                e.Cancel = true;
                Hide();
                return;
            }

            _timer.Stop();
            _trayManager.Dispose();
            _accelEngine.Dispose();
        }

        #endregion
    }
}