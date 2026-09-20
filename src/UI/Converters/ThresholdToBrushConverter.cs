using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace SystemCoreMonitor.UI.Converters
{
    public class ThresholdToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double percent)
            {
                if (percent >= 90.0)
                    return Application.Current?.Resources["StatusCrit"] ?? Brushes.Red;
                if (percent >= 75.0)
                    return Application.Current?.Resources["StatusWarn"] ?? Brushes.Orange;
                return Application.Current?.Resources["StatusOk"] ?? Brushes.Green;
            }
            return Application.Current?.Resources["StatusOk"] ?? Brushes.Green;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}