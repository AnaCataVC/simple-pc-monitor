using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SystemCoreMonitor.Core;
using SystemCoreMonitor.Core.Mvvm;
using SystemCoreMonitor.Models;
using SystemCoreMonitor.Modules;

namespace SystemCoreMonitor.ViewModels
{
    public class DashboardViewModel : ViewModelBase
    {
        private CpuMetric _cpu = new();
        private MemoryMetric _memory = new();
        private List<DiskMetric> _drives = new();
        private NetworkMetric _network = new();
        private GpuMetric _gpu = new();
        private NpuMetric _npu = new();

        public CpuMetric Cpu
        {
            get => _cpu;
            set => SetProperty(ref _cpu, value);
        }

        public MemoryMetric Memory
        {
            get => _memory;
            set => SetProperty(ref _memory, value);
        }

        public List<DiskMetric> Drives
        {
            get => _drives;
            set => SetProperty(ref _drives, value);
        }

        public NetworkMetric Network
        {
            get => _network;
            set => SetProperty(ref _network, value);
        }

        public GpuMetric Gpu
        {
            get => _gpu;
            set => SetProperty(ref _gpu, value);
        }

        public NpuMetric Npu
        {
            get => _npu;
            set => SetProperty(ref _npu, value);
        }

        private string _currentPowerPlan = "Balanced";
        public string CurrentPowerPlan
        {
            get => _currentPowerPlan;
            set => SetProperty(ref _currentPowerPlan, value);
        }

        public AsyncRelayCommand OptimizeMemoryCommand { get; }
        public AsyncRelayCommand CleanTempCommand { get; }
        public AsyncRelayCommand FlushDnsCommand { get; }
        public RelayCommand SwitchPowerPlanCommand { get; }
        public RelayCommand LaunchTaskMgrCommand { get; }
        public RelayCommand LaunchResMonCommand { get; }

        public event Action<string, NotificationType>? ShowToastRequested;
        public event Action<string>? ShowProgressRequested;

        public DashboardViewModel()
        {
            PowerPlanManager.GetActiveScheme(out string initialScheme);
            _currentPowerPlan = initialScheme == "Power Saver" ? "Saver" : initialScheme == "High Performance" ? "HighPerf" : "Balanced";

            OptimizeMemoryCommand = new AsyncRelayCommand(async () =>
            {
                ShowProgressRequested?.Invoke(LocalizationManager.Get("ActionOptimizingRam"));
                double freedMB = await Task.Run(() => MemoryOptimizer.OptimizeWorkingSet(out _));
                ShowToastRequested?.Invoke(string.Format(LocalizationManager.Get("ToastMemoryOptimized"), freedMB), NotificationType.Success);
            });

            CleanTempCommand = new AsyncRelayCommand(async () =>
            {
                ShowProgressRequested?.Invoke(LocalizationManager.Get("ActionCleaningTemp"));
                var result = await Task.Run(() => SafeTempCleaner.CleanDeepStorage(false));
                ShowToastRequested?.Invoke(string.Format(LocalizationManager.Get("ToastTempCleaned"), result.HumanSize), NotificationType.Success);
            });

            FlushDnsCommand = new AsyncRelayCommand(async () =>
            {
                ShowProgressRequested?.Invoke(LocalizationManager.Get("ActionFlushingDns"));
                bool ok = await Task.Run(() => NetworkCollector.FlushDnsCache());
                if (ok)
                {
                    ShowToastRequested?.Invoke(LocalizationManager.Get("ToastDnsFlushed"), NotificationType.Success);
                }
                else
                {
                    ShowToastRequested?.Invoke(LocalizationManager.Get("ToastDnsFailed"), NotificationType.Error);
                }
            });

            SwitchPowerPlanCommand = new RelayCommand(param =>
            {
                if (param is string planName)
                {
                    if (planName == "Saver") PowerPlanManager.SetScheme(PowerSchemeMode.PowerSaver);
                    else if (planName == "HighPerf") PowerPlanManager.SetScheme(PowerSchemeMode.HighPerformance);
                    else PowerPlanManager.SetScheme(PowerSchemeMode.Balanced);

                    CurrentPowerPlan = planName;
                    ShowToastRequested?.Invoke(string.Format(LocalizationManager.Get("ToastPlanChanged"), planName), NotificationType.Success);
                }
            });

            LaunchTaskMgrCommand = new RelayCommand(() => ToolLauncher.StartTaskManager());
            LaunchResMonCommand = new RelayCommand(() => ToolLauncher.StartResourceMonitor());
        }

        public void Update(CpuMetric cpu, MemoryMetric mem, List<DiskMetric> drives, NetworkMetric net, GpuMetric gpu, NpuMetric npu)
        {
            Cpu = cpu;
            Memory = mem;
            Drives = drives;
            Network = net;
            Gpu = gpu;
            Npu = npu;

            PowerPlanManager.GetActiveScheme(out string activeScheme);
            string mapped = activeScheme == "Power Saver" ? "Saver" : activeScheme == "High Performance" ? "HighPerf" : "Balanced";
            if (CurrentPowerPlan != mapped) CurrentPowerPlan = mapped;
        }
    }
}