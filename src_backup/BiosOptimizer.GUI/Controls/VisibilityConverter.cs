using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace BiosOptimizer.GUI.Controls
{
    public class VisibilityConverter : IValueConverter
    {
        public static readonly VisibilityConverter Instance = new();

        public object VisibilityWhenTrue { get; set; } = Visibility.Visible;
        public object VisibilityWhenFalse { get; set; } = Visibility.Collapsed;
        public object VisibilityWhenNull { get; set; } = Visibility.Collapsed;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return VisibilityWhenNull;

            if (value is bool b)
                return b ? VisibilityWhenTrue : VisibilityWhenFalse;
                
            if (value is string s)
                return string.IsNullOrWhiteSpace(s) ? VisibilityWhenFalse : VisibilityWhenTrue;
                
            return VisibilityWhenTrue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
