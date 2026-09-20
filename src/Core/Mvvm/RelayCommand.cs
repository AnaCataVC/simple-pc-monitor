using System;
using System.Windows.Input;

namespace SystemCoreMonitor.Core.Mvvm
{
    /// <summary>
    /// Synchronous ICommand implementation. Uses WPF CommandManager.RequerySuggested
    /// so CanExecute is re-evaluated automatically after UI interactions.
    /// </summary>
    public sealed class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Predicate<object?>? _canExecute;

        public event EventHandler? CanExecuteChanged
        {
            add => System.Windows.Input.CommandManager.RequerySuggested += value;
            remove => System.Windows.Input.CommandManager.RequerySuggested -= value;
        }

        public RelayCommand(Action execute)
            : this(_ => execute(), null) { }

        public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

        public void Execute(object? parameter) => _execute(parameter);

        /// <summary>Forces WPF to re-query CanExecute for all RelayCommands.</summary>
        public static void NotifyCanExecuteChanged()
            => System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    public sealed class RelayCommand<T> : ICommand
    {
        private readonly Action<T?> _execute;
        private readonly Predicate<T?>? _canExecute;

        public event EventHandler? CanExecuteChanged
        {
            add => System.Windows.Input.CommandManager.RequerySuggested += value;
            remove => System.Windows.Input.CommandManager.RequerySuggested -= value;
        }

        public RelayCommand(Action<T?> execute, Predicate<T?>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) =>
            _canExecute?.Invoke(parameter is T t ? t : default) ?? true;

        public void Execute(object? parameter) =>
            _execute(parameter is T t ? t : default);
    }
}