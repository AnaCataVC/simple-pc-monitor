using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SystemCoreMonitor.Core;
using SystemCoreMonitor.Core.Mvvm;
using SystemCoreMonitor.Models;
using SystemCoreMonitor.Modules;

namespace SystemCoreMonitor.ViewModels
{
    public class StartupViewModel : ViewModelBase
    {
        private ObservableCollection<StartupItem> _startupItems = new();

        public event Action<string, NotificationType>? ShowToastRequested;

        public ObservableCollection<StartupItem> StartupItems
        {
            get => _startupItems;
            set => SetProperty(ref _startupItems, value);
        }

        public RelayCommand<StartupItem> SearchOnlineCommand { get; }
        public RelayCommand<StartupItem> ToggleStartupCommand { get; }
        public RelayCommand OpenStartupSettingsCommand { get; }

        public StartupViewModel()
        {
            SearchOnlineCommand = new RelayCommand<StartupItem>(item =>
            {
                if (item != null)
                {
                    ProcessManager.SearchProcessOnline(item.Name, "windows startup program");
                }
            });

            ToggleStartupCommand = new RelayCommand<StartupItem>(item =>
            {
                if (item == null) return;
                bool newState = !item.IsEnabled;
                bool success = StartupCollector.SetStartupItemEnabled(item.Name, item.LocationType, newState);
                bool isEs = LocalizationManager.CurrentLanguage == "es";
                if (success)
                {
                    item.IsEnabled = newState;
                    item.Status = newState
                        ? (isEs ? "✓ Habilitado" : "✓ Enabled")
                        : (isEs ? "✕ Deshabilitado" : "✕ Disabled");

                    string appName = !string.IsNullOrWhiteSpace(item.DisplayName) ? item.DisplayName : item.Name;
                    string msg = isEs
                        ? $"{(newState ? "Habilitado" : "Deshabilitado")}: {appName}"
                        : $"{(newState ? "Enabled" : "Disabled")}: {appName}";
                    ShowToastRequested?.Invoke(msg, NotificationType.Success);
                }
                else
                {
                    string errMsg = isEs
                        ? $"No se pudo modificar {item.Name} (posiblemente requiere elevación)"
                        : $"Could not modify {item.Name} (may require elevation)";
                    ShowToastRequested?.Invoke(errMsg, NotificationType.Error);
                }
            });

            OpenStartupSettingsCommand = new RelayCommand(() =>
            {
                ToolLauncher.StartStartupSettings();
                bool isEs = LocalizationManager.CurrentLanguage == "es";
                ShowToastRequested?.Invoke(isEs ? "Abriendo Configuración de Inicio de Windows..." : "Opening Windows Startup Settings...", NotificationType.Info);
            });
        }

        public void Update(List<StartupItem> items)
        {
            _startupItems.SyncInPlace(items ?? new());
        }
    }
}