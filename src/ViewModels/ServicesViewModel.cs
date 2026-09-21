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
        public RelayCommand OpenServicesConsoleCommand { get; }

        public event Action<string, NotificationType>? ShowToastRequested;

        public ServicesViewModel()
        {
            OpenServicesConsoleCommand = new RelayCommand(() =>
            {
                ToolLauncher.StartServicesConsole();
                ShowToastRequested?.Invoke(LocalizationManager.CurrentLanguage == "es"
                    ? "Abriendo Consola de Servicios de Windows..."
                    : "Opening Windows Services Console...", NotificationType.Info);
            });

            StartServiceCommand = new AsyncRelayCommand<ServiceItem>(async svc =>
            {
                if (svc == null) return;
                bool ok = await Task.Run(() => ServiceCollector.StartService(svc.ServiceName));
                if (ok)
                {
                    ShowToastRequested?.Invoke(
                        string.Format(LocalizationManager.Get("ToastServiceStarted"), svc.DisplayName),
                        NotificationType.Success);
                }
                else
                {
                    ShowToastRequested?.Invoke(
                        string.Format(LocalizationManager.Get("ToastServiceStartFailed"), svc.DisplayName),
                        NotificationType.Error);
                }
            });

            StopServiceCommand = new AsyncRelayCommand<ServiceItem>(async svc =>
            {
                if (svc == null) return;
                bool ok = await Task.Run(() => ServiceCollector.StopService(svc.ServiceName));
                if (ok)
                {
                    ShowToastRequested?.Invoke(
                        string.Format(LocalizationManager.Get("ToastServiceStopped"), svc.DisplayName),
                        NotificationType.Success);
                }
                else
                {
                    ShowToastRequested?.Invoke(
                        string.Format(LocalizationManager.Get("ToastServiceStopFailed"), svc.DisplayName),
                        NotificationType.Error);
                }
            });

            RestartServiceCommand = new AsyncRelayCommand<ServiceItem>(async svc =>
            {
                if (svc == null) return;
                bool ok = await Task.Run(() => ServiceCollector.RestartService(svc.ServiceName));
                if (ok)
                {
                    ShowToastRequested?.Invoke(
                        string.Format(LocalizationManager.Get("ToastServiceStarted"), svc.DisplayName),
                        NotificationType.Success);
                }
                else
                {
                    ShowToastRequested?.Invoke(
                        string.Format(LocalizationManager.Get("ToastServiceRestartFailed"), svc.DisplayName),
                        NotificationType.Error);
                }
            });
        }

        public void Update(ServiceMetric metric)
        {
            Metric = metric;
            Services = new ObservableCollection<ServiceItem>(metric.CriticalServices);
        }
    }
}