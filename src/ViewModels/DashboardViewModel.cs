using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SystemCoreMonitor.Core;
using SystemCoreMonitor.Core.Mvvm;
using SystemCoreMonitor.Models;

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

        public AsyncRelayCommand OptimizeMemoryCommand { get; }
        public AsyncRelayCommand CleanTempCommand { get; }
        public RelayCommand SwitchPowerPlanCommand { get; }
        public RelayCommand LaunchTaskMgrCommand { get; }
        public RelayCommand LaunchResMonCommand { get; }

        public event Action<string>? ShowToastRequested;

        public DashboardViewModel()
        {
            OptimizeMemoryCommand = new AsyncRelayCommand(async () =>
            {
                double freedMB = await Task.Run(() => MemoryOptimizer.OptimizeWorkingSet(out _));
                ShowToastRequested?.Invoke(string.Format(LocalizationManager.Get("ToastMemoryOptimized"), freedMB));
            });

            CleanTempCommand = new AsyncRelayCommand(async () =>
            {
                var result = await Task.Run(() => SafeTempCleaner.CleanDeepStorage(false));
                ShowToastRequested?.Invoke(string.Format(LocalizationManager.Get("ToastTempCleaned"), result.HumanSize));
            });

            SwitchPowerPlanCommand = new RelayCommand(param =>
            {
                if (param is string planName)
                {
                    if (planName == "Saver") PowerPlanManager.SetScheme(PowerSchemeMode.PowerSaver);
                    else if (planName == "HighPerf") PowerPlanManager.SetScheme(PowerSchemeMode.HighPerformance);
                    else PowerPlanManager.SetScheme(PowerSchemeMode.Balanced);
                    ShowToastRequested?.Invoke(string.Format(LocalizationManager.Get("ToastPlanChanged"), planName));
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
        }
    }
}