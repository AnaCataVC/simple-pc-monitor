using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using SystemCoreMonitor.Core;
using SystemCoreMonitor.Core.Mvvm;
using SystemCoreMonitor.Models;

namespace SystemCoreMonitor.ViewModels
{
    public class StorageViewModel : ViewModelBase
    {
        private ObservableCollection<DiskMetric> _drives = new();
        private ObservableCollection<BloatFinding> _bloatFindings = new();

        public ObservableCollection<DiskMetric> Drives
        {
            get => _drives;
            set => SetProperty(ref _drives, value);
        }

        public ObservableCollection<BloatFinding> BloatFindings
        {
            get => _bloatFindings;
            set => SetProperty(ref _bloatFindings, value);
        }

        public AsyncRelayCommand CleanTempCommand { get; }
        public AsyncRelayCommand EmptyRecycleBinCommand { get; }
        public AsyncRelayCommand<BloatFinding> ExecuteBloatActionCommand { get; }
        public AsyncRelayCommand ScanBloatCommand { get; }

        private bool _isScanningBloat;
        public bool IsScanningBloat
        {
            get => _isScanningBloat;
            set => SetProperty(ref _isScanningBloat, value);
        }

        public event Action<string, NotificationType>? ShowToastRequested;
        public event Action<string>? ShowProgressRequested;

        public StorageViewModel()
        {
            CleanTempCommand = new AsyncRelayCommand(async () =>
            {
                ShowProgressRequested?.Invoke(LocalizationManager.Get("ActionCleaningTemp"));
                var res = await Task.Run(() => SafeTempCleaner.CleanDeepStorage(false));
                ShowToastRequested?.Invoke(string.Format(LocalizationManager.Get("ToastTempCleaned"), res.HumanSize), NotificationType.Success);
            });

            EmptyRecycleBinCommand = new AsyncRelayCommand(async () =>
            {
                ShowProgressRequested?.Invoke(LocalizationManager.Get("ActionEmptyingRecycleBin"));
                await Task.Run(() => NativeMethods.SHEmptyRecycleBin(IntPtr.Zero, null, NativeMethods.SHERB_NOCONFIRMATION | NativeMethods.SHERB_NOPROGRESSUI));
                ShowToastRequested?.Invoke(LocalizationManager.Get("ToastRecycleBinEmptied"), NotificationType.Success);
            });

            ExecuteBloatActionCommand = new AsyncRelayCommand<BloatFinding>(async finding =>
            {
                if (finding == null) return;
                if (finding.ActionKind == "SafeDelete")
                {
                    ShowProgressRequested?.Invoke(string.Format("Limpiando caché {0}...", finding.Category));
                    var res = await Task.Run(() => BloatDetector.DeleteWhitelistedCache(finding.Path));
                    ShowToastRequested?.Invoke(string.Format(LocalizationManager.Get("ToastTempCleaned"), res.HumanSize), NotificationType.Success);
                }
                else if (finding.ActionKind == "LaunchTool")
                {
                    ToolLauncher.StartPCManager();
                }
            });

            ScanBloatCommand = new AsyncRelayCommand(async () =>
            {
                if (IsScanningBloat) return;
                IsScanningBloat = true;
                ShowProgressRequested?.Invoke(LocalizationManager.CurrentLanguage == "es" ? "Analizando cachés y archivos voluminosos..." : "Scanning bloat and cache files...");
                try
                {
                    var findings = await Task.Run(() => BloatDetector.Scan());
                    UpdateBloat(findings);
                    ShowToastRequested?.Invoke(LocalizationManager.CurrentLanguage == "es" ? "Análisis de almacenamiento completado" : "Storage scan completed", NotificationType.Info);
                }
                finally
                {
                    IsScanningBloat = false;
                }
            });
        }

        public void UpdateDrives(List<DiskMetric> drives)
        {
            Drives = new ObservableCollection<DiskMetric>(drives ?? new());
        }

        public void UpdateBloat(List<BloatFinding> findings)
        {
            BloatFindings = new ObservableCollection<BloatFinding>(findings ?? new());
        }

        public void Update(List<DiskMetric> drives, List<BloatFinding> findings)
        {
            UpdateDrives(drives);
            UpdateBloat(findings);
        }
    }
}