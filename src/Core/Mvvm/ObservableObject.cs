using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SystemCoreMonitor.Core.Mvvm
{
    /// <summary>
    /// Lightweight zero-NuGet MVVM base class implementing INotifyPropertyChanged and INotifyPropertyChanging.
    /// Replaces CommunityToolkit.Mvvm to preserve the zero-external-dependency invariant.
    /// </summary>
    public abstract class ObservableObject : INotifyPropertyChanged, INotifyPropertyChanging
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public event PropertyChangingEventHandler? PropertyChanging;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected virtual void OnPropertyChanging([CallerMemberName] string? propertyName = null)
        {
            PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(propertyName));
        }

        /// <summary>
        /// Sets the backing field and raises PropertyChanging/PropertyChanged if the value differs.
        /// Returns true if the value changed.
        /// </summary>
        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(storage, value))
                return false;

            OnPropertyChanging(propertyName);
            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
