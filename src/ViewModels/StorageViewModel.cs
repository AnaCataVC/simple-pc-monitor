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

        public event Action<string>? ShowToastRequested;

        public StorageViewModel()
        {
            CleanTempCommand = new AsyncRelayCommand(async () =>
            {
                var res = await Task.Run(() => SafeTempCleaner.CleanDeepStorage(false));
                ShowToastRequested?.Invoke(string.Format(LocalizationManager.Get("ToastTempCleaned"), res.HumanSize));
            });

            EmptyRecycleBinCommand = new AsyncRelayCommand(async () =>
            {
                await Task.Run(() => NativeMethods.SHEmptyRecycleBin(IntPtr.Zero, null, NativeMethods.SHERB_NOCONFIRMATION | NativeMethods.SHERB_NOPROGRESSUI));
                ShowToastRequested?.Invoke(LocalizationManager.Get("ToastRecycleBinEmptied"));
            });

            ExecuteBloatActionCommand = new AsyncRelayCommand<BloatFinding>(async finding =>
            {
                if (finding == null) return;
                if (finding.ActionKind == "SafeDelete")
                {
                    var res = await Task.Run(() => BloatDetector.DeleteWhitelistedCache(finding.Path));
                    ShowToastRequested?.Invoke(string.Format(LocalizationManager.Get("ToastTempCleaned"), res.HumanSize));
                }
                else if (finding.ActionKind == "LaunchTool")
                {
                    ToolLauncher.StartPCManager();
                }
            });
        }

        public void Update(List<DiskMetric> drives, List<BloatFinding> findings)
        {
            Drives = new ObservableCollection<DiskMetric>(drives ?? new());
            BloatFindings = new ObservableCollection<BloatFinding>(findings ?? new());
        }
    }
}