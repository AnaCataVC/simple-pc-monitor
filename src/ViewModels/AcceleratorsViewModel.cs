using SystemCoreMonitor.Core;
using SystemCoreMonitor.Core.Mvvm;
using SystemCoreMonitor.Models;

namespace SystemCoreMonitor.ViewModels
{
    public class AcceleratorsViewModel : ViewModelBase
    {
        private GpuMetric _gpu = new();
        private NpuMetric _npu = new();
        private bool _isEnabled = true;

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

        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        public string DisabledNotice => LocalizationManager.Get("AccelMonitoringDisabledNotice");
        public string DisabledHint => LocalizationManager.Get("AccelMonitoringDisabledHint");

        public AcceleratorsViewModel()
        {
            LocalizationManager.LanguageChanged += () =>
            {
                OnPropertyChanged(nameof(DisabledNotice));
                OnPropertyChanged(nameof(DisabledHint));
            };
        }

        public void Update(GpuMetric gpu, NpuMetric npu)
        {
            Gpu = gpu;
            Npu = npu;
        }
    }
}