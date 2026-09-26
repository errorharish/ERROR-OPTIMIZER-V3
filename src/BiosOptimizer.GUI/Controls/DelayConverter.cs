using System;
using System.Globalization;
using System.Windows.Data;

namespace BiosOptimizer.GUI.Controls
{
    public class DelayConverter : IValueConverter
    {
        public static DelayConverter Instance { get; } = new DelayConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int index)
            {
                // Delay each index by 50ms
                return TimeSpan.FromMilliseconds(index * 50);
            }
            return TimeSpan.Zero;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
