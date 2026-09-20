using System;
using SystemCoreMonitor.Core;
using SystemCoreMonitor.Core.Mvvm;

namespace SystemCoreMonitor.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private AppConfig _config;

        public AppConfig Config
        {
            get => _config;
            set => SetProperty(ref _config, value);
        }

        public RelayCommand<string> SetThemeCommand { get; }
        public RelayCommand<string> SetLanguageCommand { get; }
        public RelayCommand<int> SetIntervalCommand { get; }

        public event Action<string>? ThemeChanged;
        public event Action<string>? LanguageChanged;

        public SettingsViewModel()
        {
            _config = ConfigManager.Load();

            SetThemeCommand = new RelayCommand<string>(theme =>
            {
                if (string.IsNullOrEmpty(theme)) return;
                Config.Theme = theme;
                ConfigManager.Save(Config);
                ThemeChanged?.Invoke(theme);
                OnPropertyChanged(nameof(Config));
            });

            SetLanguageCommand = new RelayCommand<string>(lang =>
            {
                if (string.IsNullOrEmpty(lang)) return;
                Config.Language = lang;
                ConfigManager.Save(Config);
                LocalizationManager.CurrentLanguage = lang;
                LanguageChanged?.Invoke(lang);
                OnPropertyChanged(nameof(Config));
            });

            SetIntervalCommand = new RelayCommand<int>(sec =>
            {
                if (sec <= 0) return;
                Config.RefreshIntervalSeconds = sec;
                ConfigManager.Save(Config);
                OnPropertyChanged(nameof(Config));
            });
        }
    }
}