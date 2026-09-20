using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SystemCoreMonitor.UI.Converters
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; } = false;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool b = value is bool val && val;
            if (Invert) b = !b;
            return b ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility vis)
            {
                bool b = vis == Visibility.Visible;
                return Invert ? !b : b;
            }
            return false;
        }
    }
}