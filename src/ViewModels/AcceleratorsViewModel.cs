using SystemCoreMonitor.Models;

namespace SystemCoreMonitor.ViewModels
{
    public class AcceleratorsViewModel : ViewModelBase
    {
        private GpuMetric _gpu = new();
        private NpuMetric _npu = new();

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

        public void Update(GpuMetric gpu, NpuMetric npu)
        {
            Gpu = gpu;
            Npu = npu;
        }
    }
}