using System;
using System.Globalization;
using System.Windows.Data;
namespace OsrsWorldPing.Desktop;
public sealed class BooleanOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? 1d : 0d;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
