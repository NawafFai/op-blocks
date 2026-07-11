using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace OPBlocksManager
{
    /// <summary>Maps a null/empty string to Collapsed, otherwise Visible.</summary>
    public sealed class EmptyToCollapsedConverter : IValueConverter
    {
        public static readonly EmptyToCollapsedConverter Instance = new EmptyToCollapsedConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
