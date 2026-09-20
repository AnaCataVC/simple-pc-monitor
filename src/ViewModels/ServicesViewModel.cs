using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using SystemCoreMonitor.Core;
using SystemCoreMonitor.Core.Mvvm;
using SystemCoreMonitor.Models;
using SystemCoreMonitor.Modules;

namespace SystemCoreMonitor.ViewModels
{
    public class ServicesViewModel : ViewModelBase
    {
        private ServiceMetric _metric = new();
        private ObservableCollection<ServiceItem> _services = new();

        public ServiceMetric Metric
        {
            get => _metric;
            set => SetProperty(ref _metric, value);
        }

        public ObservableCollection<ServiceItem> Services
        {
            get => _services;
            set => SetProperty(ref _services, value);
        }

        public AsyncRelayCommand<ServiceItem> StartServiceCommand { get; }
        public AsyncRelayCommand<ServiceItem> StopServiceCommand { get; }
        public AsyncRelayCommand<ServiceItem> RestartServiceCommand { get; }

        public event Action<string>? ShowToastRequested;

        public ServicesViewModel()
        {
            StartServiceCommand = new AsyncRelayCommand<ServiceItem>(async svc =>
            {
                if (svc == null) return;
                bool ok = await Task.Run(() => ServiceCollector.StartService(svc.ServiceName));
                ShowToastRequested?.Invoke(ok
                    ? string.Format(LocalizationManager.Get("ToastServiceStarted"), svc.DisplayName)
                    : "Failed to start service");
            });

            StopServiceCommand = new AsyncRelayCommand<ServiceItem>(async svc =>
            {
                if (svc == null) return;
                bool ok = await Task.Run(() => ServiceCollector.StopService(svc.ServiceName));
                ShowToastRequested?.Invoke(ok
                    ? string.Format(LocalizationManager.Get("ToastServiceStopped"), svc.DisplayName)
                    : "Failed to stop service");
            });

            RestartServiceCommand = new AsyncRelayCommand<ServiceItem>(async svc =>
            {
                if (svc == null) return;
                bool ok = await Task.Run(() => ServiceCollector.RestartService(svc.ServiceName));
                ShowToastRequested?.Invoke(ok
                    ? string.Format(LocalizationManager.Get("ToastServiceStarted"), svc.DisplayName)
                    : "Failed to restart service");
            });
        }

        public void Update(ServiceMetric metric)
        {
            Metric = metric;
            Services = new ObservableCollection<ServiceItem>(metric.CriticalServices);
        }
    }
}