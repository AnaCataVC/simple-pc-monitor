using System;

namespace SystemCoreMonitor.Core.Mvvm
{
    /// <summary>
    /// Simple tab/view navigation service. MainViewModel hosts a CurrentViewModel property
    /// bound to a ContentControl in MainWindow.xaml via DataTemplates per ViewModel type.
    /// </summary>
    public interface INavigationService
    {
        ObservableObject? CurrentViewModel { get; }
        void NavigateTo<TViewModel>() where TViewModel : ObservableObject;
        event Action? CurrentViewModelChanged;
    }

    public sealed class NavigationService : ObservableObject, INavigationService
    {
        private readonly Func<Type, ObservableObject> _viewModelFactory;
        private ObservableObject? _currentViewModel;

        public event Action? CurrentViewModelChanged;

        public ObservableObject? CurrentViewModel
        {
            get => _currentViewModel;
            private set
            {
                if (SetProperty(ref _currentViewModel, value))
                    CurrentViewModelChanged?.Invoke();
            }
        }

        public NavigationService(Func<Type, ObservableObject> viewModelFactory)
        {
            _viewModelFactory = viewModelFactory ?? throw new ArgumentNullException(nameof(viewModelFactory));
        }

        public void NavigateTo<TViewModel>() where TViewModel : ObservableObject
        {
            CurrentViewModel = _viewModelFactory(typeof(TViewModel));
        }
    }
}
