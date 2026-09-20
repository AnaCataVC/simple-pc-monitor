using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SystemCoreMonitor.Core.Mvvm
{
    /// <summary>
    /// Asynchronous ICommand that prevents concurrent executions and exposes IsExecuting
    /// for UI loading indicators. Fires-and-forgets the async delegate from Execute(),
    /// surfacing exceptions via the unobserved task exception handler (caught by CrashLogger).
    /// </summary>
    public sealed class AsyncRelayCommand : ICommand
    {
        private readonly Func<object?, Task> _execute;
        private readonly Predicate<object?>? _canExecute;
        private bool _isExecuting;

        public event EventHandler? CanExecuteChanged;

        public bool IsExecuting
        {
            get => _isExecuting;
            private set
            {
                _isExecuting = value;
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public AsyncRelayCommand(Func<Task> execute)
            : this(_ => execute(), null) { }

        public AsyncRelayCommand(Func<object?, Task> execute, Predicate<object?>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) =>
            !IsExecuting && (_canExecute?.Invoke(parameter) ?? true);

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter)) return;

            try
            {
                IsExecuting = true;
                await _execute(parameter);
            }
            finally
            {
                IsExecuting = false;
            }
        }
    }
}
